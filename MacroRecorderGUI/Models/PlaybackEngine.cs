using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Utils;

[assembly: InternalsVisibleTo("MacroRecorderGUITests")]

namespace MacroRecorderGUI.Models;

public interface IPlaybackEngine : IDisposable
{
    Task PlaybackEventsAsync(IEnumerable<InputEvent> events, bool loop = false);
    void PlaybackEventAbort();
    void SetLoopPlayback(bool loop);
}

internal enum PlaybackResult
{
    Running, Finished, Cancelled, InvalidInput, Busy, InjectionFailed, InternalError, StaleSession
}

internal interface IPlaybackNativeApi
{
    PlaybackResult Start(byte[] events, bool loop, out ulong sessionId);
    PlaybackResult Poll(ulong sessionId);
    PlaybackResult Abort(ulong sessionId);
    PlaybackResult SetLoop(ulong sessionId, bool loop);
}

internal sealed class PlaybackEngine : IPlaybackEngine
{
    private sealed record Session(ulong Id, TaskCompletionSource Completion);
    private readonly object _gate = new();
    private readonly IPlaybackNativeApi _native;
    private Session? _active;
    private bool _disposed;

    public PlaybackEngine() : this(new PlaybackNativeApi()) { }
    internal PlaybackEngine(IPlaybackNativeApi native) => _native = native;

    public Task PlaybackEventsAsync(IEnumerable<InputEvent> events, bool loop = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active is not null)
                throw new InvalidOperationException("Playback is already running. Abort it before starting another macro.");

            // Serialize on the caller's thread before yielding: edits/tab switches
            // cannot change the active native session or a later loop iteration.
            var bytes = SerializeEvents.SerializeEventsToByteArray(events);
            var result = _native.Start(bytes, loop, out var id);
            if (result != PlaybackResult.Running) throw PlaybackError(result);
            var session = new Session(id, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            _active = session;
            _ = ObserveAsync(session);
            return session.Completion.Task;
        }
    }

    private async Task ObserveAsync(Session session)
    {
        try
        {
            while (true)
            {
                await Task.Delay(10).ConfigureAwait(false);
                lock (_gate)
                {
                    if (!ReferenceEquals(_active, session)) return;
                    var result = _native.Poll(session.Id);
                    if (result == PlaybackResult.Running) continue;
                    _active = null;
                    Complete(session, result);
                    return;
                }
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_active, session)) return;
                try { _native.Abort(session.Id); }
                catch (Exception cleanupError) { error = new AggregateException(error, cleanupError); }
                _active = null;
                session.Completion.TrySetException(error);
            }
        }
    }

    public void PlaybackEventAbort()
    {
        lock (_gate) AbortActive();
    }

    public void SetLoopPlayback(bool loop)
    {
        lock (_gate)
        {
            if (_active is not { } session) return;
            var result = _native.SetLoop(session.Id, loop);
            if (result is not (PlaybackResult.Running or PlaybackResult.Finished or PlaybackResult.Cancelled))
                throw PlaybackError(result);
        }
    }

    private void AbortActive()
    {
        if (_active is not { } session) return;
        // Native Abort does not return until injection and cleanup have stopped.
        // Keep ownership if interop itself fails, so another start cannot overlap.
        var result = _native.Abort(session.Id);
        _active = null;
        Complete(session, result == PlaybackResult.Finished ? PlaybackResult.Cancelled : result);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            AbortActive();
        }
    }

    private static void Complete(Session session, PlaybackResult result)
    {
        if (result == PlaybackResult.Finished) session.Completion.TrySetResult();
        else if (result == PlaybackResult.Cancelled) session.Completion.TrySetCanceled();
        else session.Completion.TrySetException(PlaybackError(result));
    }

    private static Exception PlaybackError(PlaybackResult result) => new InvalidOperationException(result switch
    {
        PlaybackResult.InvalidInput => "The macro is empty, malformed, or contains a delay too large to play.",
        PlaybackResult.Busy => "Another macro is already playing.",
        PlaybackResult.InjectionFailed => "Windows could not inject or release macro input. Check the target application's permissions.",
        PlaybackResult.StaleSession => "The playback session is no longer available.",
        _ => "Playback failed unexpectedly."
    });

    private sealed class PlaybackNativeApi : IPlaybackNativeApi
    {
        public PlaybackResult Start(byte[] events, bool loop, out ulong sessionId) =>
            StartNative(events, (nuint)events.Length, loop ? 1 : 0, out sessionId);
        public PlaybackResult Poll(ulong sessionId) => PollNative(sessionId);
        public PlaybackResult Abort(ulong sessionId) => AbortNative(sessionId);
        public PlaybackResult SetLoop(ulong sessionId, bool loop) => SetLoopNative(sessionId, loop ? 1 : 0);

        [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_start", CallingConvention = CallingConvention.Cdecl)]
        private static extern PlaybackResult StartNative([In] byte[] buffer, nuint size, int loop, out ulong sessionId);
        [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_poll", CallingConvention = CallingConvention.Cdecl)]
        private static extern PlaybackResult PollNative(ulong sessionId);
        [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_abort", CallingConvention = CallingConvention.Cdecl)]
        private static extern PlaybackResult AbortNative(ulong sessionId);
        [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_set_loop", CallingConvention = CallingConvention.Cdecl)]
        private static extern PlaybackResult SetLoopNative(ulong sessionId, int loop);
    }
}
