namespace MacroRecorderGUI.Models;

/// <summary>Immutable capture identity retained by callbacks, including queued UI work.</summary>
public sealed class RecordingSession(bool fromHotkey = false, object? context = null)
{
    private static long _nextId;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ulong Id { get; } = unchecked((ulong)Interlocked.Increment(ref _nextId));
    public bool FromHotkey { get; } = fromHotkey;
    public object? Context { get; } = context;
    public Task Completion => _completion.Task;

    internal void Complete() => _completion.TrySetResult();
    internal void Fail(Exception exception) => _completion.TrySetException(exception);
}
