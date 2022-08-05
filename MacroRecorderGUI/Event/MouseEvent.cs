﻿using System;
using ProtobufGenerated;

namespace MacroRecorderGUI.Event
{
    public class MouseEvent : InputEvent
    {
        public MouseEvent(int x, int y, uint actionType)
        {
            OriginalProtobufInputEvent = new ProtobufInputEvent
            {
                MouseEvent = new ProtobufInputEvent.Types.MouseEventType
                {
                    ActionType = actionType, MappedToVirtualDesktop = false, RelativePosition = false,
                    WheelRotation = 0, X = x, Y = y
                }
            };
        }

        public MouseEvent(ProtobufInputEvent protobufInputEvent)
        {
            if (protobufInputEvent.EventCase != ProtobufInputEvent.EventOneofCase.MouseEvent)
                throw new ArgumentException();
            Type = InputEventType.MouseEvent;
            OriginalProtobufInputEvent = protobufInputEvent;
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

        public uint ActionType
        {
            get => OriginalProtobufInputEvent.MouseEvent.ActionType;
            set => OriginalProtobufInputEvent.MouseEvent.ActionType = value;
        }
    }
}