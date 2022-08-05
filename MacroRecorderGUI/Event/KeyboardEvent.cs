using System;
using System.Windows.Input;
using ProtobufGenerated;

namespace MacroRecorderGUI.Event
{
    public class KeyboardEvent : InputEvent
    {
        public KeyboardEvent(Key keyCode, bool keyUp)
        {
            OriginalProtobufInputEvent = new ProtobufInputEvent
            {
                KeyboardEvent = new ProtobufInputEvent.Types.KeyboardEventType
                {
                    KeyUp = keyUp, VirtualKeyCode = Convert.ToUInt32(KeyInterop.VirtualKeyFromKey(keyCode))
                }
            };
        }

        public KeyboardEvent(ProtobufInputEvent protobufInputEvent)
        {
            if (protobufInputEvent.EventCase != ProtobufInputEvent.EventOneofCase.KeyboardEvent)
                throw new ArgumentException();
            OriginalProtobufInputEvent = protobufInputEvent;
            Type = InputEventType.KeyboardEvent;
        }

        public uint VirtualKeyCode
        {
            get => OriginalProtobufInputEvent.KeyboardEvent.VirtualKeyCode;
            set => OriginalProtobufInputEvent.KeyboardEvent.VirtualKeyCode = value;
        }

        public Key KeyCode
        {
            get => KeyInterop.KeyFromVirtualKey(Convert.ToInt32(OriginalProtobufInputEvent.KeyboardEvent.VirtualKeyCode));
            set => OriginalProtobufInputEvent.KeyboardEvent.VirtualKeyCode = Convert.ToUInt32(KeyInterop.VirtualKeyFromKey(value));
        }

        public bool KeyUp
        {
            get => OriginalProtobufInputEvent.KeyboardEvent.KeyUp;
            set => OriginalProtobufInputEvent.KeyboardEvent.KeyUp = value;
        }
    }
}