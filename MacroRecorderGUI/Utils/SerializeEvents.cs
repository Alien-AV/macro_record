using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using MacroRecorderGUI.Event;
using ProtobufGenerated;

namespace MacroRecorderGUI.Utils
{
    public class SerializeEvents
    {
        internal static byte[] SerializeEventsToByteArray(IEnumerable<InputEvent> inputEventList)
        {
            var serializedEvents = new ProtobufInputEventList();
            serializedEvents.InputEvents.AddRange(inputEventList.Select(inputEvent=>inputEvent.OriginalProtobufInputEvent));
            var serializedEventsByteArray = serializedEvents.ToByteArray();
            return serializedEventsByteArray;
        }

        internal static IEnumerable<InputEvent> DeserializeEventsFromByteArray(byte[] serializedEvents)
        {
            var deserializedEvents = ProtobufInputEventList.Parser.ParseFrom(serializedEvents);
            return deserializedEvents.InputEvents.Select(InputEvent.CreateInputEvent);
        }
    }
}