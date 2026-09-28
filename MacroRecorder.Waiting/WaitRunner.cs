using System.Diagnostics;
using ProtobufGenerated;

namespace MacroRecorder.Waiting;

public enum ObservationState { Match, NoMatch, Unavailable, Error }
public sealed record WaitObservation(ObservationState State, string Detail, string Identity = "", uint Value = 0,
    IReadOnlyList<string>? Instances = null, IReadOnlyList<string>? MatchingInstances = null, string? Text = null, ScalarValue? Scalar = null);
public interface IWaitObserver
{
    ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken cancellationToken);
}
public sealed record WaitProgress(string Condition, TimeSpan Elapsed, TimeSpan Remaining, TimeSpan StableFor, string Observation);
public sealed record WaitOutcome(bool Satisfied, string Detail);

// Per-occurrence state only. Preview cannot access this evaluator or its observer.
internal sealed class WaitEvaluator(WaitCondition condition)
{
    private WaitObservation? _baseline;
    private bool _sawFalse;
    private TimeSpan? _stableSince, _lastSample;
    public TimeSpan StableFor { get; private set; }

    public bool Sample(WaitObservation observation, TimeSpan elapsed, bool fresh = true)
    {
        if (_lastSample is { } last && elapsed - last > TimeSpan.FromMicroseconds(condition.PollIntervalUs * 3)) _stableSince = null;
        _lastSample = elapsed;
        StableFor = TimeSpan.Zero;
        if (!fresh || observation.State is ObservationState.Unavailable or ObservationState.Error)
        { _stableSince = null; return false; }
        var matched = observation.State == ObservationState.Match;
        switch (condition.Trigger)
        {
            case WaitTrigger.BecomesTrue:
                if (!matched) _sawFalse = true;
                matched &= _sawFalse;
                break;
            case WaitTrigger.Changes:
                if (observation.Identity.Length == 0) { _stableSince = null; return false; }
                _baseline ??= observation;
                matched = observation.Identity == _baseline.Identity && Changed(observation, _baseline);
                break;
            case WaitTrigger.NewWindow:
                _baseline ??= observation;
                var initial = _baseline.Instances ?? [];
                matched = (observation.MatchingInstances ?? []).Any(id => !initial.Contains(id));
                break;
        }
        if (!matched) { _stableSince = null; return false; }
        _stableSince ??= elapsed;
        StableFor = elapsed - _stableSince.Value;
        return StableFor >= TimeSpan.FromMicroseconds(condition.StableForUs);
    }

    private bool Changed(WaitObservation sample, WaitObservation baseline)
    {
        if (condition.Pixel is { } pixel) return !ColorsClose(sample.Value, baseline.Value, pixel.Tolerance);
        if (condition.Memory is { } memory) return sample.Scalar is { } scalar && baseline.Scalar is { } initial
            && !scalar.Matches(initial, NumericComparison.NumericEquals, memory.Tolerance);
        if ((condition.AccessibilityText?.Predicate ?? condition.OcrText?.Predicate) is { } predicate)
            return sample.Text is not null && baseline.Text is not null && !TextPredicates.Equal(sample.Text, baseline.Text, predicate);
        return sample.Value != baseline.Value;
    }

    internal static bool ColorsClose(uint a, uint b, uint tolerance) =>
        new[] { 0, 8, 16 }.All(shift => Math.Abs((int)((a >> shift) & 255) - (int)((b >> shift) & 255)) <= tolerance);
}

public sealed class WaitRunner
{
    private readonly Func<WaitCondition, IWaitObserver> _factory;
    private readonly bool _ownsObserver;
    public WaitRunner(IWaitObserver observer) { _factory = _ => observer; }
    public WaitRunner(Func<WaitCondition, IWaitObserver> factory) { _factory = factory; _ownsObserver = true; }
    public static WaitRunner Desktop { get; } = new(WaitServices.CreateObserver);
    // The permit covers a whole run, including its last observation and callback
    // teardown. Neither a stuck read nor stuck cancellation can accumulate workers.
    private readonly SemaphoreSlim _observationSlot = new(1, 1);

    public async Task<WaitOutcome> RunAsync(WaitCondition source, TimeSpan remaining, CancellationToken cancellationToken,
        Action<WaitProgress>? progress = null)
    {
        var condition = source.Clone();
        WaitValidation.Validate(condition);
        if (remaining > TimeSpan.FromMicroseconds(condition.TimeoutUs)) remaining = TimeSpan.FromMicroseconds(condition.TimeoutUs);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (remaining <= TimeSpan.Zero) return new(false, "Condition timed out before observation started.");
        deadline.CancelAfter(remaining);
        // Provider callbacks never register on the runner's deadline signal.
        // A callback that throws or blocks cannot prevent WaitAsync from ending.
        var observerCancel = new CancellationTokenSource();
        var observerToken = observerCancel.Token;
        var clock = Stopwatch.StartNew();
        var evaluator = new WaitEvaluator(condition);
        var last = "No observation available";
        var ownsSlot = false;
        IWaitObserver? observer = null;
        Task pendingObservation = Task.CompletedTask;
        try
        {
            await _observationSlot.WaitAsync(deadline.Token).ConfigureAwait(false);
            ownsSlot = true;
            observer = _factory(condition);
            while (true)
            {
                var started = clock.Elapsed;
                var observationTask = Task.Run(async () =>
                {
                    try { return await observer.ObserveAsync(condition.Clone(), observerToken).ConfigureAwait(false); }
                    catch (Exception error) { return new WaitObservation(ObservationState.Error, error.Message); }
                });
                pendingObservation = observationTask;
                var sample = await observationTask.WaitAsync(deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                var elapsed = clock.Elapsed;
                last = sample.Detail;
                var satisfied = evaluator.Sample(sample, elapsed, elapsed - started <= TimeSpan.FromMicroseconds(condition.PollIntervalUs * 3));
                progress?.Invoke(new(WaitValidation.Describe(condition), elapsed, remaining > elapsed ? remaining - elapsed : TimeSpan.Zero,
                    evaluator.StableFor, sample.Detail));
                if (sample.State == ObservationState.Error) return new(false, sample.Detail);
                if (satisfied) return new(true, sample.Detail);
                await Task.Delay(TimeSpan.FromMicroseconds(condition.PollIntervalUs), deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, $"Condition timed out. Last observation: {last}"); }
        finally
        {
            _ = Task.Run(() => TeardownAsync(pendingObservation, observerCancel, ownsSlot, _ownsObserver ? observer as IAsyncDisposable : null));
        }
    }

    private async Task TeardownAsync(Task pending, CancellationTokenSource cancellation, bool ownsSlot, IAsyncDisposable? resource = null)
    {
        try { await Task.WhenAll(pending, cancellation.CancelAsync()).ConfigureAwait(false); }
        catch (Exception error) { Trace.TraceError($"Wait observation/cancellation teardown failed ({error.GetType().Name}, 0x{error.HResult:X8})."); }
        var disposed = false;
        try { if (resource is not null) await resource.DisposeAsync().ConfigureAwait(false); disposed = true; }
        catch (Exception error) { Trace.TraceError($"Wait resource disposal failed ({error.GetType().Name}, 0x{error.HResult:X8}); observation permit retained. Restart is required."); }
        finally { cancellation.Dispose(); if (ownsSlot && disposed) _observationSlot.Release(); }
    }

    // Explicit picker reads use the same permit as Test and playback. Timeout
    // ends the caller's wait, but work and cancellation retain the permit.
    internal async Task<T> ExecuteExclusiveAsync<T>(Func<CancellationToken, Task<T>> work, TimeSpan timeout, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        var providerCancellation = new CancellationTokenSource();
        var ownsSlot = false;
        Task pending = Task.CompletedTask;
        try
        {
            await _observationSlot.WaitAsync(deadline.Token).ConfigureAwait(false);
            ownsSlot = true;
            var operation = Task.Run(() => work(providerCancellation.Token));
            pending = operation;
            return await operation.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        finally { _ = Task.Run(() => TeardownAsync(pending, providerCancellation, ownsSlot)); }
    }
}
