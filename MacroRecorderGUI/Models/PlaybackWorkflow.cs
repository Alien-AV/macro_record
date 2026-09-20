using System.Diagnostics;
using MacroRecorderGUI.Event;

namespace MacroRecorderGUI.Models;

/// <summary>Owns a cancellable finite run while the existing engine owns each native session.</summary>
public sealed class PlaybackWorkflow
{
    private readonly IPlaybackEngine _engine;
    private readonly object _gate = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private Session? _active;
    private PlaybackState _state = PlaybackState.Idle;
    public PlaybackState State
    {
        get { lock (_gate) return _active is { } session ? _state with { Elapsed = session.Clock.Elapsed } : _state; }
    }
    public event Action<PlaybackState>? StateChanged;
    public bool IsActive { get { lock (_gate) return _active is not null; } }

    public PlaybackWorkflow(IPlaybackEngine engine) : this(engine, Task.Delay) { }
    internal PlaybackWorkflow(IPlaybackEngine engine, Func<TimeSpan, CancellationToken, Task> delay)
    { _engine = engine; _delay = delay; }

    public Task PlayAsync(IEnumerable<InputEvent> events, PlaybackOptions options)
    {
        options.Validate();
        var snapshot = events.Select(input => InputEvent.CreateInputEvent(input.OriginalProtobufInputEvent.Clone())).ToArray();
        if (snapshot.Length == 0) throw new InvalidOperationException("The recording has no captured input.");
        foreach (var input in snapshot)
        {
            var scaled = decimal.Round(input.TimeSinceLastEvent / (decimal)options.Speed, 0, MidpointRounding.AwayFromZero);
            if (scaled > long.MaxValue) throw new ArgumentOutOfRangeException(nameof(options), "A scaled delay is too large to play.");
            input.TimeSinceLastEvent = (ulong)scaled;
        }
        lock (_gate)
        {
            if (_active is not null) throw new InvalidOperationException("Playback is already running.");
            var session = new Session(options);
            _active = session;
            _ = RunAsync(session, snapshot);
            return session.Completion.Task;
        }
    }

    public void Abort()
    {
        lock (_gate)
        {
            if (_active is not { } session) return;
            if (session.Aborting) return;
            session.Aborting = true;
            try
            {
                session.Cancel.Cancel();
                if (!Owns(session)) return;
                Publish(session, PlaybackPhase.Stopping);
                if (!Owns(session) || session.StartingNative) return;
                try
                {
                    if (session.NativeTask is not null) _engine.PlaybackEventAbort();
                }
                catch (PlaybackStoppedException error)
                {
                    if (!Owns(session)) return;
                    Finish(session, PlaybackPhase.Failed, error);
                    throw;
                }
                catch (Exception error)
                {
                    if (!Owns(session)) return;
                    // Interop failure cannot prove injection stopped. Retain
                    // ownership and permit an explicit abort retry.
                    Publish(session, PlaybackPhase.Stopping, error.Message);
                    throw;
                }
                if (!Owns(session)) return;
                var failure = session.NativeTask is { IsFaulted: true } native ? native.Exception!.GetBaseException() : null;
                Finish(session, failure is null ? PlaybackPhase.Cancelled : PlaybackPhase.Failed, failure);
                if (failure is not null) throw new PlaybackStoppedException(failure.Message);
            }
            finally { session.Aborting = false; DisposeCancellationIfFinished(session); }
        }
    }

    private async Task RunAsync(Session session, InputEvent[] snapshot)
    {
        try
        {
            var token = session.Cancel.Token;
            var countdownClock = Stopwatch.StartNew();
            while (countdownClock.Elapsed < session.Options.Countdown)
            {
                TimeSpan remaining;
                lock (_gate)
                {
                    if (!Owns(session)) return;
                    remaining = session.Options.Countdown - countdownClock.Elapsed;
                    Publish(session, PlaybackPhase.Countdown, remaining: remaining);
                    if (!Owns(session)) return;
                }
                await _delay(remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100), token).ConfigureAwait(false);
            }
            lock (_gate)
            {
                if (!Owns(session)) return;
                session.Clock.Start();
            }
            for (var repeat = 1; repeat <= session.Options.RepeatCount; repeat++)
            {
                Task native;
                lock (_gate)
                {
                    if (!Owns(session)) return;
                    token.ThrowIfCancellationRequested();
                    session.Repeat = repeat;
                    Publish(session, PlaybackPhase.Playing);
                    if (!Owns(session)) return; // A state subscriber may request an abort.
                    session.StartingNative = true;
                    try { native = _engine.PlaybackEventsAsync(snapshot, loop: false); }
                    finally { session.StartingNative = false; }
                    if (!Owns(session)) return;
                    session.NativeTask = native;
                    if (token.IsCancellationRequested)
                    {
                        try { Abort(); }
                        catch (Exception) when (Owns(session)) { /* Retain ownership and await the native session after an abort interop failure. */ }
                    }
                    if (!Owns(session)) return;
                }
                // Await native completion even after abort fails: cancellation does
                // not establish that native cleanup has completed.
                await native.ConfigureAwait(false);
                lock (_gate)
                {
                    if (!Owns(session)) return;
                    session.NativeTask = null;
                    token.ThrowIfCancellationRequested();
                    if (repeat == session.Options.RepeatCount)
                    {
                        Finish(session, PlaybackPhase.Completed);
                        return;
                    }
                    Publish(session, PlaybackPhase.BetweenRepeats);
                    if (!Owns(session)) return;
                }
                await Task.Yield();
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate) { if (Owns(session)) Finish(session, PlaybackPhase.Cancelled); }
        }
        catch (Exception error)
        {
            lock (_gate) { if (Owns(session)) Finish(session, PlaybackPhase.Failed, error); }
        }
        finally
        {
            lock (_gate)
            {
                session.RunnerFinished = true;
                DisposeCancellationIfFinished(session);
            }
        }
    }

    private bool Owns(Session session) => ReferenceEquals(_active, session);
    private void Publish(Session session, PlaybackPhase phase, string? error = null, TimeSpan remaining = default)
    {
        if (!Owns(session)) return;
        _state = new(phase, session.Repeat, session.Options.RepeatCount, remaining, session.Clock.Elapsed, error);
        StateChanged?.Invoke(_state);
    }
    private void Finish(Session session, PlaybackPhase phase, Exception? error = null)
    {
        if (!Owns(session)) return;
        session.Clock.Stop();
        _active = null;
        _state = new(phase, session.Repeat, session.Options.RepeatCount, TimeSpan.Zero, session.Clock.Elapsed, error?.Message);
        if (error is not null) session.Completion.TrySetException(error);
        else if (phase == PlaybackPhase.Cancelled) session.Completion.TrySetCanceled();
        else session.Completion.TrySetResult();
        StateChanged?.Invoke(_state);
    }

    private static void DisposeCancellationIfFinished(Session session)
    {
        // A synchronous cancellation continuation can finish RunAsync while
        // Cancel is still enumerating callbacks. Dispose only after it unwinds.
        if (!session.RunnerFinished || session.Aborting || session.CancellationDisposed) return;
        session.CancellationDisposed = true;
        session.Cancel.Dispose();
    }

    private sealed class Session(PlaybackOptions options)
    {
        public PlaybackOptions Options { get; } = options;
        public CancellationTokenSource Cancel { get; } = new();
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? NativeTask { get; set; }
        public Stopwatch Clock { get; } = new();
        public int Repeat { get; set; }
        public bool Aborting { get; set; }
        public bool StartingNative { get; set; }
        public bool RunnerFinished { get; set; }
        public bool CancellationDisposed { get; set; }
    }
}
