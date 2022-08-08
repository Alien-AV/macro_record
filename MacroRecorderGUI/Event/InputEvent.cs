using System;
using ProtobufGenerated;

namespace MacroRecorderGUI.Event
{
    public class InputEvent
    {
        public ProtobufInputEvent OriginalProtobufInputEvent;
        public enum InputEventType
        {
            KeyboardEvent = ProtobufInputEvent.EventOneofCase.KeyboardEvent,
            MouseEvent = ProtobufInputEvent.EventOneofCase.MouseEvent,
            None = ProtobufInputEvent.EventOneofCase.None
        }

        public InputEventType Type => (InputEventType)OriginalProtobufInputEvent.EventCase;
        public ulong TimeSinceStartOfRecording
        {
            get => OriginalProtobufInputEvent.TimeSinceStartOfRecording;
            set => OriginalProtobufInputEvent.TimeSinceStartOfRecording = value;
        }

        public static InputEvent CreateInputEvent(ProtobufInputEvent protobufEvent)
        {
            switch (protobufEvent.EventCase)
            {
                case ProtobufInputEvent.EventOneofCase.KeyboardEvent:
                    return new KeyboardEvent(protobufEvent);
                case ProtobufInputEvent.EventOneofCase.MouseEvent:
                    return new MouseEvent(protobufEvent);
                case ProtobufInputEvent.EventOneofCase.None:
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
