using System.Collections.ObjectModel;
using Windows.System;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Editor;

namespace MacroRecorderGUI.ViewModels;

public sealed partial class MacroViewModel : ViewModelBase, IDisposable
{
    private readonly IPlaybackEngine _playbackEngine;
    private readonly Func<MacroViewModel, Task>? _playMacro;
    private string _name;
    private ActionEditor? _editor;
    public ActionEditor Editor => _editor ??= new ActionEditor(this);
    public void Dispose()
    {
        DetachPersistenceTracking();
        _editor?.Dispose();
    }
    internal void InvalidateEditorUndo() => _editor?.InvalidateUndo();

    public MacroViewModel(string name, IPlaybackEngine playbackEngine, Func<MacroViewModel, Task>? playMacro = null)
    {
        _playbackEngine = playbackEngine;
        _playMacro = playMacro;
        _name = name;
        Events.CollectionChanged += EventsChanged;
    }

    public ObservableCollection<InputEvent> Events { get; } = [];
    public List<InputEvent> SelectedEvents { get; } = [];
    public long SelectionRevision { get; private set; }
    public long ContentRevision { get; private set; }
    public event EventHandler? ContentReplaced;

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
            MarkChanged();
        }
    }

    public Task PlayMacro()
    {
        if (_playMacro is not null) return _playMacro(this);
        return Events.Count == 0 && PointerOrigins.Count == 0 ? Task.CompletedTask : new PlaybackWorkflow(_playbackEngine).PlayAsync(Events,
            new PlaybackOptions { Countdown = TimeSpan.Zero }, PointerOrigins);
    }

    public void Clear()
    {
        ContentRevision++;
        Events.Clear();
        RestoreOriginState(new() { Events = [] });
        SelectedEvents.Clear();
        ContentReplaced?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceSelection(IEnumerable<InputEvent> selectedEvents)
    {
        SelectionRevision++;
        SelectedEvents.Clear();
        SelectedEvents.AddRange(selectedEvents);
    }

    public void RemoveSelectedEvents()
    {
        Editor.Execute("Remove selected input", () =>
        {
            var selected = SelectedEvents.ToHashSet();
            SelectedEvents.Clear();
            for (var index = Events.Count - 1; index >= 0; index--)
                if (selected.Contains(Events[index])) Events.RemoveAt(index);
        });
    }

    public void ChangeDelaysOnSelected(ulong delay)
    {
        Editor.Execute("Set each selected raw delay", () => ChangeDelaysOnList(delay, SelectedEvents.ToList()));
    }

    public void PopulateEventCollectionWithNewEvents(IEnumerable<InputEvent> deserializedEvents)
    {
        Clear();

        foreach (var deserializedEvent in deserializedEvents)
        {
            Events.Add(deserializedEvent);
        }
    }

    public void AddEvent(InputEvent parsedEvent)
    {
        Events.Add(parsedEvent);
    }

    public void CreateKeyboardEventManually()
    {
        Editor.Execute("Add keyboard event", () => PlaceManuallyCreatedEvent(new KeyboardEvent(VirtualKey.Escape, false)));
    }

    public void CreateMouseEventManually()
    {
        Editor.Execute("Add mouse event", () => PlaceManuallyCreatedEvent(new MouseEvent(0, 0, MouseActionTypeFlags.Move)));
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
