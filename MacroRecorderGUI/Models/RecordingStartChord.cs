using ProtobufGenerated;

namespace MacroRecorderGUI.Models;

[Flags]
public enum RecordingStartKeys : uint
{
    None = 0,
    Q = 1,
    Control = 2,
    LeftControl = 4,
    RightControl = 8
}

/// <summary>Drains only keys still held from the recording shortcut at capture readiness.</summary>
public sealed class RecordingStartChord(RecordingStartKeys heldKeys)
{
    private const RecordingStartKeys Controls = RecordingStartKeys.Control
        | RecordingStartKeys.LeftControl | RecordingStartKeys.RightControl;
    private RecordingStartKeys _pendingKeys = heldKeys;
    private ulong _suppressedDelay;

    public ProtobufInputEvent? Accept(ProtobufInputEvent inputEvent)
    {
        var keyboard = inputEvent.KeyboardEvent;
        var key = keyboard?.VirtualKeyCode switch
        {
            0x51 => RecordingStartKeys.Q,
            0x11 => Controls,
            0xA2 => RecordingStartKeys.Control | RecordingStartKeys.LeftControl,
            0xA3 => RecordingStartKeys.Control | RecordingStartKeys.RightControl,
            _ => RecordingStartKeys.None
        };

        if ((_pendingKeys & key) != 0)
        {
            _suppressedDelay = AddDelay(_suppressedDelay, inputEvent.TimeSinceLastEvent);
            if (keyboard!.KeyUp)
            {
                _pendingKeys &= ~key;
            }

            return null;
        }

        if (_suppressedDelay == 0)
        {
            return inputEvent;
        }

        var acceptedEvent = inputEvent.Clone();
        acceptedEvent.TimeSinceLastEvent = AddDelay(_suppressedDelay, inputEvent.TimeSinceLastEvent);
        _suppressedDelay = 0;
        return acceptedEvent;
    }

    private static ulong AddDelay(ulong first, ulong second) =>
        ulong.MaxValue - first < second ? ulong.MaxValue : first + second;
}
