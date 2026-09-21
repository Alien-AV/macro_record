using System.Diagnostics;
using ProtobufGenerated;

namespace MacroRecorderGUI.Models;

internal enum ObservationState { Match, NoMatch, Unavailable, Error }
internal sealed record WaitObservation(ObservationState State, string Detail, string Identity = "", uint Value = 0,
    IReadOnlyList<string>? Instances = null, IReadOnlyList<string>? MatchingInstances = null);
internal interface IWaitObserver
{
    ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken cancellationToken);
}
public sealed record WaitProgress(string Condition, TimeSpan Elapsed, TimeSpan Remaining, TimeSpan StableFor, string Observation);
internal sealed record WaitOutcome(bool Satisfied, string Detail);

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
                matched = observation.Identity == _baseline.Identity && (condition.Pixel is { } pixel
                    ? !ColorsClose(observation.Value, _baseline.Value, pixel.Tolerance)
                    : observation.Value != _baseline.Value);
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

    internal static bool ColorsClose(uint a, uint b, uint tolerance) =>
        new[] { 0, 8, 16 }.All(shift => Math.Abs((int)((a >> shift) & 255) - (int)((b >> shift) & 255)) <= tolerance);
}

internal sealed class WaitRunner(IWaitObserver observer)
{
    internal static WaitRunner Desktop { get; } = new(new WindowPixelObserver(new WindowsWaitDesktop()));
    // The permit covers a whole run, including its last observation and callback
    // teardown. Neither a stuck read nor stuck cancellation can accumulate workers.
    private readonly SemaphoreSlim _observationSlot = new(1, 1);

    public async Task<WaitOutcome> RunAsync(WaitCondition source, TimeSpan remaining, CancellationToken cancellationToken,
        Action<WaitProgress>? progress = null)
    {
        var condition = source.Clone();
        WaitValidation.Validate(condition);
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
        Task pendingObservation = Task.CompletedTask;
        try
        {
            await _observationSlot.WaitAsync(deadline.Token).ConfigureAwait(false);
            ownsSlot = true;
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
            _ = Task.WhenAll(pendingObservation, observerCancel.CancelAsync()).ContinueWith(task =>
                {
                    _ = task.Exception;
                    observerCancel.Dispose();
                    if (ownsSlot) _observationSlot.Release();
                },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
