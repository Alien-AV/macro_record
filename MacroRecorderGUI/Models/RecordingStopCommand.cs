namespace MacroRecorderGUI.Models;

// Shared with the native capture ABI. Only successfully registered gestures are enabled.
[Flags]
public enum RecordingStopGestures : uint
{
    None = 0,
    ControlW = 1,
    ControlR = 2,
    ControlAltF12 = 4,
    ControlShiftF12 = 8
}

public readonly record struct RecordingStopCommand(RecordingStopGestures Gesture, uint MessageTime)
{
    public static RecordingStopGestures For(HotkeyGesture gesture) => gesture switch
    {
        { Key: Windows.System.VirtualKey.W, Modifiers: Utils.HotKeyModifiers.Control } => RecordingStopGestures.ControlW,
        { Key: Windows.System.VirtualKey.R, Modifiers: Utils.HotKeyModifiers.Control } => RecordingStopGestures.ControlR,
        { Key: Windows.System.VirtualKey.F12, Modifiers: Utils.HotKeyModifiers.Control | Utils.HotKeyModifiers.Alt } => RecordingStopGestures.ControlAltF12,
        { Key: Windows.System.VirtualKey.F12, Modifiers: Utils.HotKeyModifiers.Control | Utils.HotKeyModifiers.Shift } => RecordingStopGestures.ControlShiftF12,
        _ => RecordingStopGestures.None
    };
}
