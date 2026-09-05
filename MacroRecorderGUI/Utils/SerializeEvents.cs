using Google.Protobuf;
using MacroRecorderGUI.Event;
using ProtobufGenerated;

namespace MacroRecorderGUI.Utils;

public static class SerializeEvents
{
    internal static byte[] SerializeEventsToByteArray(IEnumerable<InputEvent> inputEventList)
    {
        var serializedEvents = new ProtobufInputEventList();
        serializedEvents.InputEvents.AddRange(
            inputEventList.Select(inputEvent => inputEvent.OriginalProtobufInputEvent));
        return serializedEvents.ToByteArray();
    }

    internal static IEnumerable<InputEvent> DeserializeEventsFromByteArray(byte[] serializedEvents)
    {
        var deserializedEvents = ProtobufInputEventList.Parser.ParseFrom(serializedEvents);
        return deserializedEvents.InputEvents.Select(InputEvent.CreateInputEvent);
    }
}
