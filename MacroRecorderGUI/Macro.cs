using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Data;
using Google.Protobuf;
using MacroRecorderGUI.Utils;
using ProtobufGenerated;

namespace MacroRecorderGUI
{
    public class Macro
    {
        public ObservableCollection<InputEvent> Events { get; } = new ObservableCollection<InputEvent>();

        internal static byte[] SerializeEventsToByteArray(IEnumerable<InputEvent> inputEventList)
        {
            var serializedEvents = new InputEventList();
            serializedEvents.InputEvents.AddRange(inputEventList);
            var serializedEventsByteArray = serializedEvents.ToByteArray();
            return serializedEventsByteArray;
        }

        internal static IEnumerable<InputEvent> DeserializeEventsFromByteArray(byte[] serializedEvents)
        {
            var deserializedEvents = InputEventList.Parser.ParseFrom(serializedEvents);
            return deserializedEvents.InputEvents;
        }

        public void PlayMacro()
        {
            if (!Events.Any()) return;
            var eventsWrappedWithReleasingModKeys = ReleaseModifierKeys.ReleaseModKeysEvents
                .Concat(Events).Concat(ReleaseModifierKeys.ReleaseModKeysEvents);
            var serializedEventsByteArray = SerializeEventsToByteArray(eventsWrappedWithReleasingModKeys);
            InjectAndCaptureDll.InjectEvents(serializedEventsByteArray);
        }

        public void Clear()
        {
            Events.Clear();
        }

        public void RemoveSelectedEvents(IEnumerable<InputEvent> selectedItems)
        {
            if (selectedItems == null) return;
            foreach (var eventToRemove in selectedItems)
            {
                Events.Remove(eventToRemove);
            }
        }

        public void ChangeDelays(ulong timeIncrement)
        {
            var currentTimeOffset = 0ul;
            foreach (var inputEvent in Events)
            {
                inputEvent.TimeSinceStartOfRecording = currentTimeOffset;
                currentTimeOffset += timeIncrement;
            }

            //TODO: implement the events as wrapper class around protobuf class, and implement PropertyChanged event listeners on them
            CollectionViewSource.GetDefaultView(Events).Refresh();
        }

        public void SaveToFile()
        {
            FileOperations.SaveEventsToFile(Events);
        }

        public void LoadFromFile()
        {
            var deserializedEvents = FileOperations.LoadEventsFromFile();
            if (deserializedEvents != null)
            {
                PopulateEventCollectionWithNewEvents(deserializedEvents);
            }
        }

        public void PopulateEventCollectionWithNewEvents(IEnumerable<InputEvent> deserializedEvents)
        {
            Events.Clear();
            foreach (var deserializedEvent in deserializedEvents)
            {
                Events.Add(deserializedEvent);
            }
        }

        public void AddEvent(InputEvent parsedEvent)
        {
            Events.Add(parsedEvent);
        }
    }
}