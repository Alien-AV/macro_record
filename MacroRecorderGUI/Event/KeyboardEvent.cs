using System.Globalization;
using Windows.System;
using ProtobufGenerated;

namespace MacroRecorderGUI.Event;

public sealed class KeyboardEvent : InputEvent
{
    public KeyboardEvent(VirtualKey keyCode, bool keyUp)
    {
        OriginalProtobufInputEvent = new ProtobufInputEvent
        {
            KeyboardEvent = new ProtobufInputEvent.Types.KeyboardEventType
            {
                KeyUp = keyUp,
                VirtualKeyCode = (uint)keyCode
            }
        };
    }

    public KeyboardEvent(ProtobufInputEvent protobufInputEvent)
    {
        if (protobufInputEvent.EventCase != ProtobufInputEvent.EventOneofCase.KeyboardEvent)
        {
            throw new ArgumentException("A keyboard event payload is required.", nameof(protobufInputEvent));
        }

        OriginalProtobufInputEvent = protobufInputEvent;
    }

    public uint VirtualKeyCode
    {
        get => OriginalProtobufInputEvent.KeyboardEvent.VirtualKeyCode;
        set
        {
            if (value == OriginalProtobufInputEvent.KeyboardEvent.VirtualKeyCode)
            {
                return;
            }

            OriginalProtobufInputEvent.KeyboardEvent.VirtualKeyCode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(KeyCode));
            OnPropertyChanged(nameof(KeyName));
        }
    }

    public string KeyName
    {
        get
        {
            var keyCode = KeyCode;
            return Enum.IsDefined(keyCode)
                ? keyCode.ToString()
                : $"0x{VirtualKeyCode:X2}";
        }
        set
        {
            value = value.Trim();
            if (value.Length == 1 && value[0] is >= '0' and <= '9')
            {
                VirtualKeyCode = value[0];
                return;
            }

            if (Enum.TryParse<VirtualKey>(value, true, out var keyCode))
            {
                KeyCode = keyCode;
                return;
            }

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexadecimalCode))
            {
                VirtualKeyCode = hexadecimalCode;
            }
            else if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericCode))
            {
                VirtualKeyCode = numericCode;
            }
        }
    }

    public VirtualKey KeyCode
    {
        get => (VirtualKey)VirtualKeyCode;
        set => VirtualKeyCode = (uint)value;
    }

    public bool KeyUp
    {
        get => OriginalProtobufInputEvent.KeyboardEvent.KeyUp;
        set
        {
            if (value == OriginalProtobufInputEvent.KeyboardEvent.KeyUp)
            {
                return;
            }

            OriginalProtobufInputEvent.KeyboardEvent.KeyUp = value;
            OnPropertyChanged();
        }
    }
}
