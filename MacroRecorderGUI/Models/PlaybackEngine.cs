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
    Running, Finished, Cancelled, InvalidInput, Busy, InjectionFailed, InternalError, StaleSession, WaitTimedOut, WaitFailed, StaleWait
}

internal interface IPlaybackNativeApi
{
    PlaybackResult Start(byte[] events, bool loop, out ulong sessionId);
    PlaybackResult Poll(ulong sessionId);
    PlaybackResult Abort(ulong sessionId);
    PlaybackResult SetLoop(ulong sessionId, bool loop);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeWaitRequest { public ulong Occurrence, EventIndex, RemainingUs; }
internal interface IPlaybackWaitNativeApi : IPlaybackNativeApi
{
    PlaybackResult WaitRequest(ulong sessionId, out NativeWaitRequest request);
    PlaybackResult ResolveWait(ulong sessionId, ulong occurrence, bool satisfied);
}
internal interface IWaitPlaybackProgress { WaitProgress? CurrentWait { get; } }

// Abort joined this session, but cleanup failed. The abort caller reports the
// error synchronously; the session task carries the same failure for awaiters.
internal sealed class PlaybackStoppedException(string message) : InvalidOperationException(message);

internal sealed class PlaybackEngine : IPlaybackEngine, IWaitPlaybackProgress
{
    private sealed record Session(ulong Id, TaskCompletionSource Completion, InputEvent[] Events)
    {
        public CancellationTokenSource Cancel { get; } = new();
        public ulong Occurrence;
        public WaitProgress? Progress;
        public WaitProgress? LastWait;
        public ulong WaitEventIndex;
        public string? WaitFailure;
    }
    private readonly object _gate = new();
    private readonly IPlaybackNativeApi _native;
    private Session? _active;
    private bool _disposed;
    private readonly WaitRunner _waitRunner;
    public WaitProgress? CurrentWait { get { lock (_gate) return _active?.Progress; } }

    public PlaybackEngine() : this(new PlaybackNativeApi()) { }
    internal PlaybackEngine(IPlaybackNativeApi native, IWaitObserver? observer = null)
    { _native = native; _waitRunner = observer is null ? WaitRunner.Desktop : new(observer); }

    public Task PlaybackEventsAsync(IEnumerable<InputEvent> events, bool loop = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active is not null)
                throw new InvalidOperationException("Playback is already running. Abort it before starting another macro.");

            // Serialize on the caller's thread before yielding: edits/tab switches
            // cannot change the active native session or a later loop iteration.
            var snapshot = events.Select(e => InputEvent.CreateInputEvent(e.OriginalProtobufInputEvent.Clone())).ToArray();
            WaitValidation.ValidateSchedule(snapshot, loop);
            if (snapshot.Any(e => e is WaitConditionEvent) && _native is not IPlaybackWaitNativeApi)
                throw new InvalidOperationException("The playback backend does not support conditional waits.");
            var bytes = SerializeEvents.SerializeEventsToByteArray(snapshot);
            var result = _native.Start(bytes, loop, out var id);
            if (result != PlaybackResult.Running) throw PlaybackError(result);
            var session = new Session(id, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), snapshot);
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
                    if (result == PlaybackResult.Running)
                    {
                        if (_native is IPlaybackWaitNativeApi waitApi)
                        {
                            var waitResult = waitApi.WaitRequest(session.Id, out var request);
                            if (waitResult == PlaybackResult.Running && request.Occurrence != 0 && request.Occurrence != session.Occurrence)
                            {
                                if (request.EventIndex >= (ulong)session.Events.Length || session.Events[(int)request.EventIndex] is not WaitConditionEvent wait)
                                    throw new InvalidOperationException("Native wait event does not match the playback snapshot.");
                                session.Occurrence = request.Occurrence;
                                session.WaitEventIndex = request.EventIndex;
                                session.WaitFailure = null;
                                session.Progress = new(wait.Description, TimeSpan.Zero, TimeSpan.FromMicroseconds(request.RemainingUs), TimeSpan.Zero, "Waiting for observation");
                                session.LastWait = session.Progress;
                                _ = Task.Run(() => RunWaitAsync(session, request, wait.Condition));
                            }
                            else if (request.Occurrence == 0) session.Progress = null;
                        }
                        continue;
                    }
                    _active = null;
                    Complete(session, result);
                    CancelObserver(session);
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
                CancelObserver(session);
            }
        }
    }

    private static void CancelObserver(Session session)
    {
        // Cancellation callbacks belong to the observer. Never execute them on
        // the native ownership thread or make terminal completion depend on them.
        _ = session.Cancel.CancelAsync().ContinueWith(task => { _ = task.Exception; session.Cancel.Dispose(); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task RunWaitAsync(Session session, NativeWaitRequest request, ProtobufGenerated.WaitCondition condition)
    {
        try
        {
            var outcome = await _waitRunner.RunAsync(condition, TimeSpan.FromMicroseconds(request.RemainingUs), session.Cancel.Token,
                progress => { lock (_gate) { if (ReferenceEquals(_active, session) && session.Occurrence == request.Occurrence) session.LastWait = session.Progress = progress; } }).ConfigureAwait(false);
            lock (_gate)
            {
                if (!ReferenceEquals(_active, session) || session.Occurrence != request.Occurrence) return;
                if (!outcome.Satisfied) session.WaitFailure = $"Wait at event {request.EventIndex + 1}: {WaitValidation.Describe(condition)}. {outcome.Detail}";
                var result = ((IPlaybackWaitNativeApi)_native).ResolveWait(session.Id, request.Occurrence, outcome.Satisfied);
                if (result is not (PlaybackResult.Running or PlaybackResult.StaleWait or PlaybackResult.StaleSession))
                    throw PlaybackError(result);
            }
        }
        catch (OperationCanceledException) when (session.Cancel.IsCancellationRequested) { }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_active, session) || session.Occurrence != request.Occurrence) return;
                session.WaitFailure = $"Wait at event {request.EventIndex + 1} failed: {error.Message}";
                try { ((IPlaybackWaitNativeApi)_native).ResolveWait(session.Id, request.Occurrence, false); }
                catch { /* Native deadline remains authoritative if resolution interop fails. */ }
            }
        }
    }

    public void PlaybackEventAbort()
    {
        lock (_gate)
        {
            var failure = AbortActive();
            if (failure is not null) throw failure;
        }
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

    private PlaybackStoppedException? AbortActive()
    {
        if (_active is not { } session) return null;
        // Native Abort does not return until injection and cleanup have stopped.
        // Keep ownership if interop itself fails, so another start cannot overlap.
        var result = _native.Abort(session.Id);
        _active = null;
        CancelObserver(session);
        if (result is PlaybackResult.Finished or PlaybackResult.Cancelled)
        {
            session.Completion.TrySetCanceled();
            return null;
        }

        var failure = new PlaybackStoppedException(PlaybackError(result).Message);
        session.Completion.TrySetException(failure);
        return failure;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            // The session task preserves cleanup errors. A terminal failure must
            // not escape through the window's synchronous shutdown callback.
            _ = AbortActive();
        }
    }

    private static void Complete(Session session, PlaybackResult result)
    {
        if (result == PlaybackResult.Finished) session.Completion.TrySetResult();
        else if (result == PlaybackResult.Cancelled) session.Completion.TrySetCanceled();
        else session.Completion.TrySetException(result is PlaybackResult.WaitFailed or PlaybackResult.WaitTimedOut
            ? new InvalidOperationException(session.WaitFailure ?? $"Wait at event {session.WaitEventIndex + 1}: {PlaybackError(result).Message} {session.LastWait?.Condition}. Last observation: {session.LastWait?.Observation ?? "unavailable"}")
            : PlaybackError(result));
    }

    private static Exception PlaybackError(PlaybackResult result) => new InvalidOperationException(result switch
    {
        PlaybackResult.InvalidInput => "The macro is empty, malformed, or contains a delay too large to play.",
        PlaybackResult.Busy => "Another macro is already playing.",
        PlaybackResult.InjectionFailed => "Windows could not inject or release macro input. Check the target application's permissions.",
        PlaybackResult.StaleSession => "The playback session is no longer available.",
        PlaybackResult.WaitTimedOut => "The conditional wait reached its native deadline.",
        PlaybackResult.WaitFailed => "The conditional wait failed.",
        _ => "Playback failed unexpectedly."
    });

    private sealed class PlaybackNativeApi : IPlaybackWaitNativeApi
    {
        public PlaybackResult Start(byte[] events, bool loop, out ulong sessionId) =>
            StartNative(events, (nuint)events.Length, loop ? 1 : 0, out sessionId);
        public PlaybackResult Poll(ulong sessionId) => PollNative(sessionId);
        public PlaybackResult Abort(ulong sessionId) => AbortNative(sessionId);
        public PlaybackResult SetLoop(ulong sessionId, bool loop) => SetLoopNative(sessionId, loop ? 1 : 0);
        public PlaybackResult WaitRequest(ulong sessionId, out NativeWaitRequest request) => WaitRequestNative(sessionId, out request);
        public PlaybackResult ResolveWait(ulong sessionId, ulong occurrence, bool satisfied) => ResolveWaitNative(sessionId, occurrence, satisfied ? 1 : 0);

        [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_wait_request", CallingConvention = CallingConvention.Cdecl)]
        private static extern PlaybackResult WaitRequestNative(ulong sessionId, out NativeWaitRequest request);
        [DllImport("RecordPlaybackDLL.dll", EntryPoint = "iac_dll_playback_resolve_wait", CallingConvention = CallingConvention.Cdecl)]
        private static extern PlaybackResult ResolveWaitNative(ulong sessionId, ulong occurrence, int satisfied);

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
