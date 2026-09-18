using ProtobufGenerated;

namespace MacroRecorderGUI.Models;

/// <summary>Keeps each capture's filter alive until its ordered native stop boundary.</summary>
public sealed class RecordingCapture
{
    private readonly IRecordingTransport _transport;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, SessionState> _sessions = [];
    private RecordingSession? _requestedSession;

    public RecordingCapture(IRecordingTransport transport)
    {
        _transport = transport;
        _transport.Input += OnInput;
        _transport.Boundary += OnBoundary;
    }

    public event Action<RecordingSession, ProtobufInputEvent>? Input;

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

    private void OnBoundary(ulong sessionId, RecordingBoundary boundary, RecordingStartKeys heldKeys)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var state))
            {
                return;
            }

            if (boundary == RecordingBoundary.Started)
            {
                state.Filter ??= new RecordingStartChord(state.Session.FromHotkey ? heldKeys : RecordingStartKeys.None);
                return;
            }

            _sessions.Remove(sessionId);
            if (_requestedSession?.Id == sessionId)
            {
                _requestedSession = null;
            }

            if (boundary == RecordingBoundary.Failed)
            {
                state.Session.Fail(new InvalidOperationException("The native recorder could not change capture registration."));
            }
            else
            {
                state.Session.Complete();
            }
        }
    }

    private sealed class SessionState(RecordingSession session)
    {
        public RecordingSession Session { get; } = session;
        public RecordingStartChord? Filter { get; set; }
    }
}
