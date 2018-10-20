using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

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

        public static IEnumerable<ProtobufGenerated.InputEvent> ReleaseModKeysEvents = ReleaseModifierKeys.ModifierKeys.Select((key) =>
            new ProtobufGenerated.InputEvent
            {
                KeyboardEvent = new ProtobufGenerated.InputEvent.Types.KeyboardEventType
                {
                    KeyUp = true,
                    VirtualKeyCode = Convert.ToUInt32(KeyInterop.VirtualKeyFromKey(key))
                }
            });
    }
}