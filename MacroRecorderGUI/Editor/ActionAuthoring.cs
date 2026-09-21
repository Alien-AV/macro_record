namespace MacroRecorderGUI.Editor;

public enum MouseButton { Left, Right, Middle, X1, X2 }

[Flags]
public enum ShortcutModifiers { None = 0, Control = 1, Shift = 2, Alt = 4, Windows = 8 }

/// <summary>A balanced click at the current pointer position. Delays are exact unsigned microseconds.</summary>
public sealed record ClickDefinition(MouseButton Button, ulong PauseBeforeMicroseconds = 0, ulong HoldMicroseconds = 0);

/// <summary>A virtual-key press with balanced modifiers, not text or keyboard-layout translation.</summary>
public sealed record ShortcutDefinition(uint VirtualKeyCode, ShortcutModifiers Modifiers = ShortcutModifiers.None,
    ulong PauseBeforeMicroseconds = 0, ulong HoldMicroseconds = 0);

/// <summary>
/// One instantaneous pointer report after an exact microsecond pause. AbsolutePrimary/AbsoluteDesktop
/// use signed physical pixel coordinates; RelativeCounts uses signed device deltas, not pixels.
/// Coordinates and delays retain the full Int32/UInt64 wire ranges. No path or duration is inferred.
/// </summary>
public sealed record PointerMovementDefinition(int X, int Y, CoordinateSpace Space, ulong PauseBeforeMicroseconds = 0);

public static class ActionAuthoring
{
    /// <summary>
    /// Supported keyboard virtual keys, including OEM punctuation. Modifiers are supplied separately.
    /// Mouse buttons, reserved codes, gamepad codes and Unicode/IME packet input require raw authoring.
    /// </summary>
    public static bool IsSupportedShortcutKey(uint code) => code is
        0x08 or 0x09 or 0x0C or 0x0D or >= 0x13 and <= 0x15 or 0x17 or 0x19 or >= 0x1B and <= 0x1F
        or >= 0x20 and <= 0x39 or >= 0x41 and <= 0x5A or 0x5D or 0x5F
        or >= 0x60 and <= 0x87 or 0x90 or 0x91 or >= 0xA6 and <= 0xB7
        or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2 or >= 0xF6 and <= 0xFE;
}
