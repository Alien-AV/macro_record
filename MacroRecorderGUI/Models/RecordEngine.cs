using RecordPlaybackDLLEnums;
using ProtobufGenerated;

namespace MacroRecorderGUI.Models;

public interface IRecordEngine : IDisposable
{
    event RecordEngine.RecordEventsEventHandler? RecordedEvent;
    event RecordEngine.RecordStatusEventHandler? RecordStatus;
    event Action<RecordingSession, Exception?>? RecordingEnded;
    bool StartRecord(RecordingSession session);
    void StopRecord();
}

public sealed class RecordEngine : IRecordEngine
{
    public delegate void RecordEventsEventHandler(object? sender, RecordEventsEventArgs e);
    public delegate void RecordStatusEventHandler(object? sender, RecordStatusEventArgs e);
    private readonly RecordingCapture _capture;

    public RecordEngine() : this(new NativeRecordingTransport()) { }

    public RecordEngine(IRecordingTransport transport)
    {
        _capture = new RecordingCapture(transport);
        _capture.Input += (session, input) => RecordedEvent?.Invoke(this, new RecordEventsEventArgs(input, session));
        _capture.Ended += (session, error) => RecordingEnded?.Invoke(session, error);
        transport.Status += status => RecordStatus?.Invoke(this, new RecordStatusEventArgs(status));
    }

    public event RecordEventsEventHandler? RecordedEvent;
    public event RecordStatusEventHandler? RecordStatus;
    public event Action<RecordingSession, Exception?>? RecordingEnded;
    public bool StartRecord(RecordingSession session) => _capture.Start(session);
    public void StopRecord() => _capture.Stop();
    public void Dispose() => _capture.Dispose();

    public sealed class RecordEventsEventArgs(ProtobufInputEvent inputEvent, RecordingSession session) : EventArgs
    {
        public ProtobufInputEvent InputEvent { get; } = inputEvent;
        public RecordingSession Session { get; } = session;
    }

    public sealed class RecordStatusEventArgs(StatusCode statusCode) : EventArgs
    {
        public StatusCode StatusCode { get; } = statusCode;
    }
}
