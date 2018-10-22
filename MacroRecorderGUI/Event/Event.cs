using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProtobufGenerated;

namespace MacroRecorderGUI.Event
{
    public class Event
    {
        protected InputEvent OriginalInputEvent;

        public static Event ParseEvent(byte[] serializedEvent)
        {
            var deserializedEvent = InputEvent.Parser.ParseFrom(serializedEvent);
            return CreateEventByType(deserializedEvent);
        }
        
        public static IEnumerable<Event> ParseEvents(byte[] serializedEvents)
        {
            var deserializedEvents = InputEventList.Parser.ParseFrom(serializedEvents);
            return deserializedEvents.InputEvents.Select(CreateEventByType);
        }

        private static Event CreateEventByType(InputEvent deserializedEvent)
        {
            switch (deserializedEvent.EventCase)
            {
                case InputEvent.EventOneofCase.KeyboardEvent:
                    return new KeyboardEvent {OriginalInputEvent = deserializedEvent};
                case InputEvent.EventOneofCase.MouseEvent:
                    return new MouseEvent {OriginalInputEvent = deserializedEvent};
            }

            return null;
        }
    }
}
