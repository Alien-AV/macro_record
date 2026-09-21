using System.Collections.ObjectModel;
using MacroRecorderGUI.Event;

namespace MacroRecorderGUI.Editor;

public sealed record RawEventRow(InputEvent Input, int Index)
{
    public string Label => $"{Index + 1}. {TimeText.Human(Input.TimeSinceLastEvent)} · " + (Input is MouseEvent m
        ? $"{m.ActionName} · ({m.X}, {m.Y}) {(m.RelativePosition ? "counts" : "px")}" : Input is KeyboardEvent k ? $"{k.KeyName} {(k.KeyUp ? "up" : "down")}" : Input is WaitConditionEvent w ? w.Description : "Input");
}

/// <summary>The raw drilldown exists only while expanded; capture appends reuse existing row objects.</summary>
public sealed class RawEventRows : ObservableCollection<RawEventRow>
{
    private RecordedAction[] _actions = [];
    public void Refresh(IList<InputEvent> events, RecordedAction action)
        => Refresh(events, [action]);
    public void Refresh(IList<InputEvent> events, IReadOnlyList<RecordedAction> actions)
    {
        if (!_actions.SequenceEqual(actions)) { Clear(); _actions = actions.ToArray(); }
        var offset = 0;
        foreach (var action in actions)
        {
            for (var i = Math.Max(0, Count - offset); i < action.Count; i++) Add(new(events[action.Start + i], action.Start + i));
            offset += action.Count;
        }
    }
    public void Close() { _actions = []; Clear(); }
}
