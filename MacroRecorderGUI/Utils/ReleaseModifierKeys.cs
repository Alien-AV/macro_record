using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using MacroRecorderGUI.Event;

namespace MacroRecorderGUI.Utils
{
    public class ReleaseModifierKeys
    {
        private static readonly Key[] ModifierKeys =
        {
            Key.LeftShift,
            Key.LeftCtrl,
            Key.LeftAlt,
            Key.RightShift,
            Key.RightCtrl,
            Key.RightAlt
        };
        //todo: should return InputEvents?
        public static IEnumerable<InputEvent> ReleaseModKeysEvents = 
            ModifierKeys.Select((key) => new KeyboardEvent(key, true));
    }
}