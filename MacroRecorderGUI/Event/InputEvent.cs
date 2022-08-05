using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProtobufGenerated;

namespace MacroRecorderGUI.Event
{
    public class InputEvent
    {
        public ProtobufInputEvent OriginalProtobufInputEvent;
        public enum InputEventType
        {
            KeyboardEvent,
            MouseEvent
        }

        public InputEventType Type;
        public ulong TimeSinceStartOfRecording
        {
            get => OriginalProtobufInputEvent.TimeSinceStartOfRecording;
            set => OriginalProtobufInputEvent.TimeSinceStartOfRecording = value;
        }

        public static InputEvent CreateInputEvent(byte[] serializedEvent)
        {
            var deserializedEvent = ProtobufInputEvent.Parser.ParseFrom(serializedEvent);
            return CreateEventByType(deserializedEvent);
        }

        public static InputEvent CreateInputEvent(ProtobufInputEvent protobufEvent)
        {
            return CreateEventByType(protobufEvent);
        }
        
        public static IEnumerable<InputEvent> ParseEvents(byte[] serializedEvents)
        {
            var deserializedEvents = ProtobufInputEventList.Parser.ParseFrom(serializedEvents);
            return deserializedEvents.InputEvents.Select(CreateEventByType);
        }

        private static InputEvent CreateEventByType(ProtobufInputEvent deserializedEvent)
        {
            switch (deserializedEvent.EventCase)
            {
                case ProtobufInputEvent.EventOneofCase.KeyboardEvent:
                    return new KeyboardEvent(deserializedEvent);
                case ProtobufInputEvent.EventOneofCase.MouseEvent:
                    return new MouseEvent(deserializedEvent);
            }

            return null;
        }
    }
}
