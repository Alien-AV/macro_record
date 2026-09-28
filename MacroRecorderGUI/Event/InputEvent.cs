using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProtobufGenerated;

namespace MacroRecorderGUI.Event;

public abstract class InputEvent : INotifyPropertyChanged, IWaitScheduleEvent
{
    public enum InputEventType
    {
        KeyboardEvent = ProtobufInputEvent.EventOneofCase.KeyboardEvent,
        MouseEvent = ProtobufInputEvent.EventOneofCase.MouseEvent,
        WaitCondition = ProtobufInputEvent.EventOneofCase.WaitCondition,
        Delay = ProtobufInputEvent.EventOneofCase.Delay,
        None = ProtobufInputEvent.EventOneofCase.None
    }

    public ProtobufInputEvent OriginalProtobufInputEvent { get; protected init; } = null!;

    public InputEventType Type => (InputEventType)OriginalProtobufInputEvent.EventCase;

    public ulong TimeSinceLastEvent
    {
        get => OriginalProtobufInputEvent.TimeSinceLastEvent;
        set
        {
            if (value == OriginalProtobufInputEvent.TimeSinceLastEvent)
            {
                return;
            }

            OriginalProtobufInputEvent.TimeSinceLastEvent = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static InputEvent CreateInputEvent(ProtobufInputEvent protobufEvent)
    {
        return protobufEvent.EventCase switch
        {
            ProtobufInputEvent.EventOneofCase.KeyboardEvent => new KeyboardEvent(protobufEvent),
            ProtobufInputEvent.EventOneofCase.MouseEvent => new MouseEvent(protobufEvent),
            ProtobufInputEvent.EventOneofCase.WaitCondition => new WaitConditionEvent(protobufEvent),
            ProtobufInputEvent.EventOneofCase.Delay => new DelayEvent(protobufEvent),
            _ => throw new ArgumentOutOfRangeException(nameof(protobufEvent), "The protobuf event has no supported payload.")
        };
    }
}
