namespace MacroRecorderGUI.Models;

/// <summary>Capture identity retained by callbacks, including queued UI work.</summary>
public sealed class RecordingSession
{
    private static long _nextId;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StartState _start;

    public RecordingSession(bool fromHotkey = false, object? context = null,
        RecordingStopGestures stopGestures = RecordingStopGestures.None, IReadOnlyList<RecordingCaptureGesture>? captureGestures = null)
        : this(context, new StartState(fromHotkey), stopGestures, captureGestures ?? []) { }

    private RecordingSession(object? context, StartState start, RecordingStopGestures stopGestures, IReadOnlyList<RecordingCaptureGesture> captureGestures)
    {
        Context = context;
        _start = start;
        StopGestures = stopGestures;
        CaptureGestures = Array.AsReadOnly(captureGestures.ToArray());
    }

    public ulong Id { get; } = unchecked((ulong)Interlocked.Increment(ref _nextId));
    public bool FromHotkey => _start.FromHotkey;
    public object? Context { get; }
    public Task Completion => _completion.Task;
    public RecordingStopGestures StopGestures { get; }
    public IReadOnlyList<RecordingCaptureGesture> CaptureGestures { get; }
    internal uint RequestedAt { get; } = unchecked((uint)Environment.TickCount);
    internal bool Accepts(RecordingStopCommand command) => command.Gesture != RecordingStopGestures.None
        && (StopGestures & command.Gesture) == command.Gesture
        && ((uint)command.Gesture & ((uint)command.Gesture - 1)) == 0
        && unchecked((int)(command.MessageTime - RequestedAt)) >= 0;

    // A content clear starts a new destination revision, not a new command chord.
    // Share only the small drain state, without retaining a chain of old sessions.
    public RecordingSession Continue(object? context = null) => new(context, _start, StopGestures, CaptureGestures);

    internal RecordingStartChord Begin(RecordingStartKeys heldKeys, RecordingStartKeys idleReleasedKeys)
    {
        if (_start.Filter is null)
            _start.Filter = new RecordingStartChord(FromHotkey ? heldKeys : RecordingStartKeys.None, FromHotkey);
        else
            _start.Filter.ContinueAtBoundary(heldKeys, idleReleasedKeys);
        _start.Filter.ResetTiming();
        return _start.Filter;
    }

    internal void Complete() => _completion.TrySetResult();
    internal void Fail(Exception exception) => _completion.TrySetException(exception);
    internal void Cancel() => _completion.TrySetCanceled();

    private sealed class StartState(bool fromHotkey)
    {
        public bool FromHotkey { get; } = fromHotkey;
        public RecordingStartChord? Filter { get; set; }
    }
}
