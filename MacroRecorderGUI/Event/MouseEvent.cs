using System;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Utils;
using ProtobufGenerated;

namespace MacroRecorderGUI.Event
{
    public class MouseEvent : InputEvent
    {
        public MouseEvent(int x, int y, MouseActionTypeFlags actionType)
        {
            OriginalProtobufInputEvent = new ProtobufInputEvent
            {
                MouseEvent = new ProtobufInputEvent.Types.MouseEventType
                {
                    ActionType = (uint)actionType, MappedToVirtualDesktop = false, RelativePosition = false,
                    WheelRotation = 0, X = x, Y = y
                }
            };
        }

        public MouseEvent(ProtobufInputEvent protobufInputEvent)
        {
            if (protobufInputEvent.EventCase != ProtobufInputEvent.EventOneofCase.MouseEvent)
                throw new ArgumentException();
            OriginalProtobufInputEvent = protobufInputEvent;
        }

        public bool RelativePosition
        {
            get => OriginalProtobufInputEvent.MouseEvent.RelativePosition;
            set => OriginalProtobufInputEvent.MouseEvent.RelativePosition = value;
        }

        public int X
        {
            get => OriginalProtobufInputEvent.MouseEvent.X;
            set => OriginalProtobufInputEvent.MouseEvent.X = value;
        }

        public int Y
        {
            get => OriginalProtobufInputEvent.MouseEvent.Y;
            set => OriginalProtobufInputEvent.MouseEvent.Y = value;
        }

        public MouseActionTypeFlags ActionType
        {
            get => (MouseActionTypeFlags)OriginalProtobufInputEvent.MouseEvent.ActionType;
            set => OriginalProtobufInputEvent.MouseEvent.ActionType = (uint)value;
        }

        public string ActionName
        {
            get => MouseActionTypeConverter.ToString(ActionType);
            set
            {
                try
                {
                    var result = MouseActionTypeConverter.FromString(value);
                    ActionType = result;
                }
                catch (ArgumentException)
                {
                }
            }
        }
    }
}