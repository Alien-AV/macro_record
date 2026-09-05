using Windows.System;
using MacroRecorderGUI.Event;

namespace MacroRecorderGUI.Utils;

public static class ReleaseModifierKeys
{
    private static readonly VirtualKey[] ModifierKeys =
    [
        VirtualKey.LeftShift,
        VirtualKey.LeftControl,
        VirtualKey.LeftMenu,
        VirtualKey.RightShift,
        VirtualKey.RightControl,
        VirtualKey.RightMenu
    ];

    public static IEnumerable<InputEvent> ReleaseModKeysEvents =>
        ModifierKeys.Select(key => new KeyboardEvent(key, true));
}
