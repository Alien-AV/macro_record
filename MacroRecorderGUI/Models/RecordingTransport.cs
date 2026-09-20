using ProtobufGenerated;
using RecordPlaybackDLLEnums;

namespace MacroRecorderGUI.Models;

public enum RecordingBoundary : uint
{
    Started = 1,
    Stopped = 2,
    Failed = 3
}

/// <summary>Delivers capture boundaries and input in their original FIFO order.</summary>
public interface IRecordingTransport : IDisposable
{
    event Action<ulong, ProtobufInputEvent>? Input;
    event Action<ulong, RecordingBoundary, RecordingStartKeys, RecordingStartKeys>? Boundary;
    event Action<StatusCode>? Status;
    void Start(ulong sessionId, RecordingStopGestures stopGestures);
    void Stop(ulong sessionId, RecordingStopCommand? command);
}
