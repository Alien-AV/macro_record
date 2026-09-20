using Windows.System;
using MacroRecorderGUI.Utils;

namespace MacroRecorderGUI.Models;

public sealed record HotkeyGesture(VirtualKey Key, HotKeyModifiers Modifiers)
{
    public string DisplayName => string.Join(" + ", new[]
    {
        Modifiers.HasFlag(HotKeyModifiers.Control) ? "Ctrl" : null,
        Modifiers.HasFlag(HotKeyModifiers.Alt) ? "Alt" : null,
        Modifiers.HasFlag(HotKeyModifiers.Shift) ? "Shift" : null,
        Modifiers.HasFlag(HotKeyModifiers.Windows) ? "Win" : null,
        Key.ToString()
    }.Where(part => part is not null));
    public override string ToString() => DisplayName;
}

public static class KeyboardShortcuts
{
    public static HotkeyGesture StartRecording { get; } = new(VirtualKey.Q, HotKeyModifiers.Control);
    public static HotkeyGesture StopRecording { get; } = new(VirtualKey.W, HotKeyModifiers.Control);
    public static HotkeyGesture StartPlayback { get; } = new(VirtualKey.E, HotKeyModifiers.Control);
    public static IReadOnlyList<HotkeyGesture> EmergencyStopChoices { get; } = Array.AsReadOnly(new[]
    {
        new HotkeyGesture(VirtualKey.R, HotKeyModifiers.Control),
        new HotkeyGesture(VirtualKey.F12, HotKeyModifiers.Control | HotKeyModifiers.Alt),
        new HotkeyGesture(VirtualKey.F12, HotKeyModifiers.Control | HotKeyModifiers.Shift)
    });
}
