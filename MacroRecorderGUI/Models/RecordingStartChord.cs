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
public sealed class RecordingStartChord(RecordingStartKeys heldKeys, bool suppressOrphanReleases = false)
{
    private const RecordingStartKeys Controls = RecordingStartKeys.Control
        | RecordingStartKeys.LeftControl | RecordingStartKeys.RightControl;
    private RecordingStartKeys _pendingKeys = heldKeys;
    private RecordingStartKeys _unseenKeys = suppressOrphanReleases ? Controls | RecordingStartKeys.Q : RecordingStartKeys.None;
    private ulong _suppressedDelay;
    internal void ResetTiming() => _suppressedDelay = 0;

    internal void ContinueAtBoundary(RecordingStartKeys heldKeys, RecordingStartKeys idleReleasedKeys)
    {
        // A release followed by a new press in the idle gap still drains the old
        // command. The held snapshot alone cannot distinguish those two presses.
        var heldAliases = ControlAliases(heldKeys);
        var releasedAliases = ControlAliases(idleReleasedKeys);
        _pendingKeys &= heldAliases & ~releasedAliases;
        _unseenKeys &= ~releasedAliases;
    }

    private static RecordingStartKeys ControlAliases(RecordingStartKeys keys)
    {
        if ((keys & RecordingStartKeys.Control) != 0) return keys | Controls;
        return (keys & Controls) != 0 ? keys | RecordingStartKeys.Control : keys;
    }

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

        var orphanRelease = keyboard?.KeyUp == true && (_unseenKeys & key) != 0;
        if (keyboard?.KeyUp == false) _unseenKeys &= ~key;
        if ((_pendingKeys & key) != 0 || orphanRelease)
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
