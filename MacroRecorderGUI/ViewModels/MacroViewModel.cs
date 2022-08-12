using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using Google.Protobuf;
using MacroRecorderGUI.Commands;
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

        public void ChangeDelays(ulong delay)
        {
            foreach (var inputEvent in Events)
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
    }
}