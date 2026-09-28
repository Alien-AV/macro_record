using ProtobufGenerated;

namespace MacroRecorderGUI.Models;

/// <summary>Keeps each capture's filter alive until its ordered native stop boundary.</summary>
public sealed class RecordingCapture : IDisposable
{
    private readonly IRecordingTransport _transport;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, SessionState> _sessions = [];
    private RecordingSession? _requestedSession;
    private bool _disposed;
    private bool _transportDisposed;

    public RecordingCapture(IRecordingTransport transport)
    {
        _transport = transport;
        _transport.Input += OnInput;
        _transport.Boundary += OnBoundary;
    }

    public event Action<RecordingSession, ProtobufInputEvent>? Input;
    public event Action<RecordingSession, PointerPosition?>? Started;
    public event Action<RecordingSession, Exception?>? Ended;
    public event Action<RecordingSession, RecordingBoundary>? WaitRejected;

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _requestedSession is not null;
            }
        }
    }

    public bool Start(RecordingSession session)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_requestedSession is not null)
            {
                return false;
            }

            if (session.Completion.IsCompleted)
            {
                throw new InvalidOperationException("A completed recording session cannot be restarted.");
            }

            _sessions.Add(session.Id, new SessionState(session));
            _requestedSession = session;
            try
            {
                _transport.Start(session.Id, session.StopGestures, session.CaptureGestures);
                return true;
            }
            catch (Exception exception)
            {
                _sessions.Remove(session.Id);
                _requestedSession = null;
                session.Fail(exception);
                throw;
            }
        }
    }

    public bool Stop(RecordingStopCommand? command = null)
    {
        lock (_gate)
        {
            if (_requestedSession is null || command is { } hotkey && !_requestedSession.Accepts(hotkey))
            {
                return false;
            }

            _transport.Stop(_requestedSession.Id, command);
            _requestedSession = null;
            return true;
        }
    }

    private void OnInput(ulong sessionId, ProtobufInputEvent inputEvent)
    {
        RecordingSession session;
        ProtobufInputEvent? accepted;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var state) || state.Filter is null)
            {
                return;
            }

            accepted = state.Filter.Accept(inputEvent);
            session = state.Session;
        }

        if (accepted is not null)
        {
            Input?.Invoke(session, accepted);
        }
    }

    public bool AcceptsRecordingCapture(RecordingCaptureGesture gesture, uint messageTime)
    {
        lock (_gate) return CaptureRejection(gesture, messageTime) is null;
    }

    private CapturedWaitSubmission? CaptureRejection(RecordingCaptureGesture gesture, uint messageTime)
    {
        if (_disposed || _requestedSession is not { } session) return CapturedWaitSubmission.Inactive;
        if (unchecked((int)(messageTime - session.RequestedAt)) < 0) return CapturedWaitSubmission.Stale;
        if (!session.CaptureGestures.Contains(gesture)) return CapturedWaitSubmission.Unregistered;
        return null;
    }

    public CapturedWaitSubmission CapturedWait(WaitCondition? condition, RecordingCaptureGesture gesture, uint messageTime)
    {
        lock (_gate)
        {
            if (CaptureRejection(gesture, messageTime) is { } rejection) return rejection;
            var session = _requestedSession!;
            // Even an invalid sample must release its native reservation.
            if (condition is not null)
            {
                try { WaitValidation.Validate(condition); }
                catch { _transport.CapturedWait(session.Id, null, gesture, messageTime); throw; }
            }
            try { return _transport.CapturedWait(session.Id, condition?.Clone(), gesture, messageTime)
                ? CapturedWaitSubmission.Queued : CapturedWaitSubmission.EnqueueFailed; }
            catch { return CapturedWaitSubmission.EnqueueFailed; }
        }
    }

    private void OnBoundary(ulong sessionId, RecordingBoundary boundary, RecordingStartKeys heldKeys, RecordingStartKeys idleReleasedKeys, PointerPosition? origin)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var state))
            {
                return;
            }

            if (boundary == RecordingBoundary.Started)
            {
                if (state.Filter is not null) return;
                state.Filter = state.Session.Begin(heldKeys, idleReleasedKeys);
                Started?.Invoke(state.Session, origin);
                return;
            }
            if (boundary is RecordingBoundary.WaitHeldInput or RecordingBoundary.WaitCancelled or RecordingBoundary.WaitStale or RecordingBoundary.WaitTimedOut)
            {
                WaitRejected?.Invoke(state.Session, boundary);
                return;
            }

            _sessions.Remove(sessionId);
            if (_requestedSession?.Id == sessionId)
            {
                _requestedSession = null;
            }

            var error = boundary == RecordingBoundary.Failed
                ? new InvalidOperationException("The native recorder could not complete capture.") : null;
            try { Ended?.Invoke(state.Session, error); }
            catch (Exception callbackError)
            {
                state.Session.Fail(error is null ? callbackError : new AggregateException(error, callbackError));
                throw;
            }
            if (error is not null) state.Session.Fail(error);
            else state.Session.Complete();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_transportDisposed) return;
            _disposed = true;
        }
        // Do not hold the state lock while the native collector joins and finishes callbacks.
        _transport.Dispose();
        _transport.Input -= OnInput;
        _transport.Boundary -= OnBoundary;
        lock (_gate)
        {
            _transportDisposed = true;
            foreach (var state in _sessions.Values) state.Session.Cancel();
            _sessions.Clear();
            _requestedSession = null;
        }
    }

    private sealed class SessionState(RecordingSession session)
    {
        public RecordingSession Session { get; } = session;
        public RecordingStartChord? Filter { get; set; }
    }
}
