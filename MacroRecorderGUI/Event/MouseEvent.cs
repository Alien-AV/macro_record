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
                MappedToVirtualDesktop = true,
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

    public bool MappedToVirtualDesktop
    {
        get => OriginalProtobufInputEvent.MouseEvent.MappedToVirtualDesktop;
        set
        {
            if (value == MappedToVirtualDesktop) return;
            OriginalProtobufInputEvent.MouseEvent.MappedToVirtualDesktop = value;
            OnPropertyChanged();
        }
    }

    // Field #4 is the SendInput mouseData payload. Keep its unsigned wire type:
    // wheel deltas use signed 32-bit bits, X buttons use the XBUTTON1/2 mask.
    public uint MouseData
    {
        get => OriginalProtobufInputEvent.MouseEvent.WheelRotation;
        set
        {
            if (value == MouseData) return;
            OriginalProtobufInputEvent.MouseEvent.WheelRotation = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Estimates physical absolute positions by accumulating raw relative counts.
    /// Pointer acceleration/speed are not recorded, so this cannot reconstruct an
    /// exact cursor path. Events before the first absolute move stay relative.
    /// </summary>
    public static void ConvertToAbsolutePositioning(IEnumerable<MouseEvent> events)
    {
        var hasAnchor = false;
        var currentX = 0;
        var currentY = 0;
        foreach (var mouseEvent in events)
        {
            if ((mouseEvent.ActionType & MouseActionTypeFlags.Move) == 0) continue;
            if (mouseEvent.RelativePosition)
            {
                if (!hasAnchor) continue;
                currentX = (int)Math.Clamp((long)currentX + mouseEvent.X, int.MinValue, int.MaxValue);
                currentY = (int)Math.Clamp((long)currentY + mouseEvent.Y, int.MinValue, int.MaxValue);
                mouseEvent.X = currentX;
                mouseEvent.Y = currentY;
                mouseEvent.RelativePosition = false;
                // Relative movement can cross monitors, even from a primary-only anchor.
                mouseEvent.MappedToVirtualDesktop = true;
            }
            else
            {
                currentX = mouseEvent.X;
                currentY = mouseEvent.Y;
                hasAnchor = true;
            }
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
