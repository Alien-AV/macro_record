using System.Collections.Generic;
using System.Collections.ObjectModel;
using Google.Protobuf;
using ProtobufGenerated;

namespace MacroRecorderGUI
{
    public class Macro
    {
        public ObservableCollection<InputEvent> Events = new ObservableCollection<InputEvent>();

        internal static byte[] SerializeEventsToByteArray(IEnumerable<ProtobufGenerated.InputEvent> inputEventList)
        {
            var serializedEvents = new ProtobufGenerated.InputEventList();
            serializedEvents.InputEvents.AddRange(inputEventList);
            var serializedEventsByteArray = serializedEvents.ToByteArray();
            return serializedEventsByteArray;
        }

        internal static IEnumerable<ProtobufGenerated.InputEvent> DeserializeEventsFromByteArray(byte[] serializedEvents)
        {
            var deserializedEvents = ProtobufGenerated.InputEventList.Parser.ParseFrom(serializedEvents);
            return deserializedEvents.InputEvents;
        }
    }
}