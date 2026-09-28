using ProtobufGenerated;
using RecordPlaybackDLLEnums;

namespace MacroRecorderGUI.Models;

public enum RecordingBoundary : uint
{
    Started = 1,
    Stopped = 2,
    Failed = 3,
    WaitHeldInput = 4,
    WaitCancelled = 5,
    WaitStale = 6,
    WaitTimedOut = 7
}

/// <summary>Delivers capture boundaries and input in their original FIFO order.</summary>
public interface IRecordingTransport : IDisposable
{
    event Action<ulong, ProtobufInputEvent>? Input;
    event Action<ulong, RecordingBoundary, RecordingStartKeys, RecordingStartKeys, PointerPosition?>? Boundary;
    event Action<StatusCode>? Status;
    void Start(ulong sessionId, RecordingStopGestures stopGestures, IReadOnlyList<RecordingCaptureGesture>? captureGestures = null);
    void Stop(ulong sessionId, RecordingStopCommand? command);
    bool CapturedWait(ulong sessionId, WaitCondition? condition, RecordingCaptureGesture gesture, uint messageTime);
}
