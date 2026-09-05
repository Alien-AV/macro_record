using System.Collections.ObjectModel;
using Windows.System;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;

namespace MacroRecorderGUI.ViewModels;

public sealed class MacroViewModel : ViewModelBase
{
    private readonly IPlaybackEngine _playbackEngine;
    private string _name;

    public MacroViewModel(string name, IPlaybackEngine playbackEngine)
    {
        _playbackEngine = playbackEngine;
        _name = name;
    }

    public ObservableCollection<InputEvent> Events { get; } = [];
    public List<InputEvent> SelectedEvents { get; } = [];

    public string Name
    {
        get => _name;
        set
        {
            if (value == _name)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    public void PlayMacro()
    {
        if (Events.Count == 0)
        {
            return;
        }

        var eventsWrappedWithReleasingModKeys = ReleaseModifierKeys.ReleaseModKeysEvents
            .Concat(Events)
            .Concat(ReleaseModifierKeys.ReleaseModKeysEvents);
        _playbackEngine.PlaybackEvents(eventsWrappedWithReleasingModKeys);
    }

    public void Clear()
    {
        Events.Clear();
        SelectedEvents.Clear();
    }

    public void ReplaceSelection(IEnumerable<InputEvent> selectedEvents)
    {
        SelectedEvents.Clear();
        SelectedEvents.AddRange(selectedEvents);
    }

    public void RemoveSelectedEvents()
    {
        foreach (var eventToRemove in SelectedEvents.ToList())
        {
            Events.Remove(eventToRemove);
        }

        SelectedEvents.Clear();
    }

    public void ChangeDelaysOnSelected(ulong delay)
    {
        ChangeDelaysOnList(delay, SelectedEvents.ToList());
    }

    public void ChangeDelaysOnAll(ulong delay)
    {
        ChangeDelaysOnList(delay, Events);
    }

    public void PopulateEventCollectionWithNewEvents(IEnumerable<InputEvent> deserializedEvents)
    {
        Events.Clear();
        SelectedEvents.Clear();

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
        var currentX = 0;
        var currentY = 0;

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
    }

    public void CreateKeyboardEventManually()
    {
        PlaceManuallyCreatedEvent(new KeyboardEvent(VirtualKey.Escape, false));
    }

    public void CreateMouseEventManually()
    {
        PlaceManuallyCreatedEvent(new MouseEvent(0, 0, MouseActionTypeFlags.Move));
    }

    private static void ChangeDelaysOnList(ulong delay, IEnumerable<InputEvent> events)
    {
        foreach (var inputEvent in events)
        {
            inputEvent.TimeSinceLastEvent = delay;
        }
    }

    private void PlaceManuallyCreatedEvent(InputEvent inputEvent)
    {
        if (SelectedEvents.Count > 0)
        {
            var lastSelectedEventIndex = Events.IndexOf(SelectedEvents[^1]);
            Events.Insert(lastSelectedEventIndex + 1, inputEvent);
        }
        else
        {
            Events.Add(inputEvent);
        }
    }
}
