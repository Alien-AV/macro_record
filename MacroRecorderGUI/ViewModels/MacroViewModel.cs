using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Google.Protobuf;
using MacroRecorderGUI.Commands;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using ProtobufGenerated;

namespace MacroRecorderGUI.ViewModels
{
    public class MacroViewModel : ViewModelBase
    {
        private readonly IPlaybackEngine _playbackEngine;

        public MacroViewModel(string name, IPlaybackEngine playbackEngine)
        {
            _playbackEngine = playbackEngine;
            Name = name;
        }

        public ObservableCollection<InputEvent> Events { get; } = new ObservableCollection<InputEvent>();

        public string Name
        {
            get => _name;
            set { if (value == _name) return; _name = value; OnPropertyChanged(); }
        }
        private string _name;

        private ICommand _closeTabCommand;

        public ICommand CloseTabCommand
        {
            get
            {
                return _closeTabCommand ?? (_closeTabCommand =
                           new DelegateCommand<ObservableCollection<MacroViewModel>>(macroTabs => macroTabs.Remove(this)));
            }
        }

        public void PlayMacro()
        {
            if (!Events.Any()) return;
            var eventsWrappedWithReleasingModKeys = ReleaseModifierKeys.ReleaseModKeysEvents
                .Concat(Events).Concat(ReleaseModifierKeys.ReleaseModKeysEvents);
            _playbackEngine.PlaybackEvents(eventsWrappedWithReleasingModKeys);
        }

        public void Clear()
        {
            Events.Clear();
        }

        public List<InputEvent> SelectedEvents { get; set; } = new List<InputEvent>();
        public void RemoveSelectedEvents()
        {
            var copyOfInputEvents = SelectedEvents.ToList();
            foreach (var eventToRemove in copyOfInputEvents)
            {
                Events.Remove(eventToRemove);
            }
        }

        public void ChangeDelaysOnSelected(ulong delay)
        {
            var copyOfInputEvents = SelectedEvents.ToList();
            ChangeDelaysOnList(delay, copyOfInputEvents);
        }

        public void ChangeDelaysOnAll(ulong delay)
        {
            ChangeDelaysOnList(delay, Events);
        }

        private void ChangeDelaysOnList(ulong delay, IEnumerable<InputEvent> events)
        {
            foreach (var inputEvent in events)
            {
                inputEvent.TimeSinceLastEvent = delay;
            }

            //TODO: implement the events as wrapper class around protobuf class, and implement PropertyChanged event listeners on them
            CollectionViewSource.GetDefaultView(Events).Refresh();
        }

        public void SaveToFile()
        {
            var newName = FileOperations.SaveEventsToFile(Events, Name);
            if (newName != null)
            {
                Name = newName;
            }
        }

        public void LoadFromFile()
        {
            var deserializedEvents = FileOperations.LoadEventsFromFile(out var newName);
            if (deserializedEvents == null) return;
            PopulateEventCollectionWithNewEvents(deserializedEvents);
            Name = newName;
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

        public void ConvertMouseEventsToAbsolutePositioning()
        {
            int currentX = 0, currentY = 0;

            foreach (var mouseEvent in Events.OfType<MouseEvent>())
            {
                if (mouseEvent.RelativePosition)
                {
                    mouseEvent.RelativePosition = false;
                    mouseEvent.X += currentX;
                    mouseEvent.Y += currentY;
                }
                
                currentX = mouseEvent.X;
                currentY = mouseEvent.Y;
            }
            CollectionViewSource.GetDefaultView(Events).Refresh();
        }

        public void CreateKeyboardEventManually()
        {
            PlaceManuallyCreatedEvent(new KeyboardEvent(Key.Escape, false));
        }

        public void CreateMouseEventManually()
        {
            PlaceManuallyCreatedEvent(new MouseEvent(0,0,MouseActionTypeFlags.Move));
        }

        private void PlaceManuallyCreatedEvent(InputEvent inputEvent){
            if (SelectedEvents.Count != 0)
            {
                var lastSelectedEvent = SelectedEvents[SelectedEvents.Count - 1];
                var lastSelectedEventIndex = Events.IndexOf(lastSelectedEvent);
                Events.Insert(lastSelectedEventIndex + 1, inputEvent);
            }
            else
            {
                Events.Add(inputEvent);
            }
        }
    }
}