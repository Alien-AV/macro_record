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
        public static IEnumerable<InputEvent> ReleaseModKeysEvents = 
            ModifierKeys.Select((key) => new KeyboardEvent(key, true));
    }
}