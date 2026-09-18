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

    public RecordingCapture(IRecordingTransport transport)
    {
        _transport = transport;
        _transport.Input += OnInput;
        _transport.Boundary += OnBoundary;
    }

    public event Action<RecordingSession, ProtobufInputEvent>? Input;
    public event Action<RecordingSession, Exception?>? Ended;

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
                _transport.Start(session.Id);
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

    public void Stop()
    {
        lock (_gate)
        {
            if (_requestedSession is null)
            {
                return;
            }

            _transport.Stop(_requestedSession.Id);
            _requestedSession = null;
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

    private void OnBoundary(ulong sessionId, RecordingBoundary boundary, RecordingStartKeys heldKeys, RecordingStartKeys idleReleasedKeys)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var state))
            {
                return;
            }

            if (boundary == RecordingBoundary.Started)
            {
                state.Filter ??= state.Session.Begin(heldKeys, idleReleasedKeys);
                return;
            }

            _sessions.Remove(sessionId);
            if (_requestedSession?.Id == sessionId)
            {
                _requestedSession = null;
            }

            if (boundary == RecordingBoundary.Failed)
            {
                var error = new InvalidOperationException("The native recorder could not begin capture.");
                Ended?.Invoke(state.Session, error);
                state.Session.Fail(error);
            }
            else
            {
                Ended?.Invoke(state.Session, null);
                state.Session.Complete();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        // Do not hold the state lock while the native collector joins and finishes callbacks.
        _transport.Dispose();
        _transport.Input -= OnInput;
        _transport.Boundary -= OnBoundary;
        lock (_gate)
        {
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
