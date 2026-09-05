using MacroRecorderGUI.Common;
using MacroRecorderGUI.Utils;
using ProtobufGenerated;

namespace MacroRecorderGUI.Event;

public sealed class MouseEvent : InputEvent
{
    public MouseEvent(int x, int y, MouseActionTypeFlags actionType)
    {
        OriginalProtobufInputEvent = new ProtobufInputEvent
        {
            MouseEvent = new ProtobufInputEvent.Types.MouseEventType
            {
                ActionType = (uint)actionType,
                MappedToVirtualDesktop = false,
                RelativePosition = false,
                WheelRotation = 0,
                X = x,
                Y = y
            }
        };
    }

    public MouseEvent(ProtobufInputEvent protobufInputEvent)
    {
        if (protobufInputEvent.EventCase != ProtobufInputEvent.EventOneofCase.MouseEvent)
        {
            throw new ArgumentException("A mouse event payload is required.", nameof(protobufInputEvent));
        }

        OriginalProtobufInputEvent = protobufInputEvent;
    }

    public bool RelativePosition
    {
        get => OriginalProtobufInputEvent.MouseEvent.RelativePosition;
        set
        {
            if (value == OriginalProtobufInputEvent.MouseEvent.RelativePosition)
            {
                return;
            }

            OriginalProtobufInputEvent.MouseEvent.RelativePosition = value;
            OnPropertyChanged();
        }
    }

    public int X
    {
        get => OriginalProtobufInputEvent.MouseEvent.X;
        set
        {
            if (value == OriginalProtobufInputEvent.MouseEvent.X)
            {
                return;
            }

            OriginalProtobufInputEvent.MouseEvent.X = value;
            OnPropertyChanged();
        }
    }

    public int Y
    {
        get => OriginalProtobufInputEvent.MouseEvent.Y;
        set
        {
            if (value == OriginalProtobufInputEvent.MouseEvent.Y)
            {
                return;
            }

            OriginalProtobufInputEvent.MouseEvent.Y = value;
            OnPropertyChanged();
        }
    }

    public MouseActionTypeFlags ActionType
    {
        get => (MouseActionTypeFlags)OriginalProtobufInputEvent.MouseEvent.ActionType;
        set
        {
            if (value == ActionType)
            {
                return;
            }

            OriginalProtobufInputEvent.MouseEvent.ActionType = (uint)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionName));
        }
    }

    public string ActionName
    {
        get => MouseActionTypeConverter.ToString(ActionType);
        set
        {
            try
            {
                ActionType = MouseActionTypeConverter.FromString(value);
            }
            catch (ArgumentException)
            {
            }
        }
    }
}
