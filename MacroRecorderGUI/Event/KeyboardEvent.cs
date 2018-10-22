using System;
using System.Windows.Input;

namespace MacroRecorderGUI.Event
{
    public class KeyboardEvent : Event
    {
        public Key KeyCode
        {
            get => KeyInterop.KeyFromVirtualKey(Convert.ToInt32(OriginalInputEvent.KeyboardEvent.VirtualKeyCode));
            set => OriginalInputEvent.KeyboardEvent.VirtualKeyCode = Convert.ToUInt32(KeyInterop.VirtualKeyFromKey(value));
        }

        public bool KeyUp
        {
            get => OriginalInputEvent.KeyboardEvent.KeyUp;
            set => OriginalInputEvent.KeyboardEvent.KeyUp = value;
        }
    }
}