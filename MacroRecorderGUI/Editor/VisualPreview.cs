using System.Numerics;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;

namespace MacroRecorderGUI.Editor;

public sealed record PreviewFrame(RecordedAction? Current, RecordedAction? Next, bool Waiting,
    PathSample? Pointer, IReadOnlyList<uint> HeldKeys, IReadOnlyList<string> HeldButtons);

public readonly record struct PreviewSegment(BigInteger Start, BigInteger End, bool Waiting, int FirstAction, int LastAction);

/// <summary>Reads recorded transitions only. It has no connection to playback or native input.</summary>
public sealed class VisualPreview(IList<InputEvent> events, ActionProjection projection)
{
    private readonly HashSet<uint> _keys = [];
    private readonly HashSet<string> _buttons = [];
    private int _processed;

    public PreviewFrame Seek(BigInteger time)
    {
        time = BigInteger.Clamp(time, 0, projection.TotalTime);
        var sample = projection.SampleAt(time);
        var target = (sample?.Index ?? -1) + 1;
        if (target < _processed) { _processed = 0; _keys.Clear(); _buttons.Clear(); }
        while (_processed < target)
        {
            switch (events[_processed++])
            {
                case KeyboardEvent key:
                    if (key.KeyUp) _keys.Remove(key.VirtualKeyCode); else _keys.Add(key.VirtualKeyCode);
                    break;
                case MouseEvent mouse:
                    Button(mouse, MouseActionTypeFlags.LeftDown, MouseActionTypeFlags.LeftUp, "Left mouse");
                    Button(mouse, MouseActionTypeFlags.RightDown, MouseActionTypeFlags.RightUp, "Right mouse");
                    Button(mouse, MouseActionTypeFlags.MiddleDown, MouseActionTypeFlags.MiddleUp, "Middle mouse");
                    if ((mouse.MouseData & 1) != 0) Button(mouse, MouseActionTypeFlags.XDown, MouseActionTypeFlags.XUp, "Mouse X1");
                    if ((mouse.MouseData & 2) != 0) Button(mouse, MouseActionTypeFlags.XDown, MouseActionTypeFlags.XUp, "Mouse X2");
                    if ((mouse.MouseData & 3) == 0) Button(mouse, MouseActionTypeFlags.XDown, MouseActionTypeFlags.XUp, "Mouse X (unspecified)");
                    break;
            }
        }
        var actions = projection.Actions;
        var low = 0; var high = actions.Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (actions[mid].EndTime <= time) low = mid + 1; else high = mid;
        }
        var index = Math.Min(low, actions.Count - 1);
        var current = index < 0 ? null : actions[index];
        var waiting = current is not null && time < current.StartTime + current.Wait;
        return new(current, index + 1 < actions.Count ? actions[index + 1] : null, waiting, sample,
            _keys.OrderBy(k => k).ToArray(), _buttons.OrderBy(b => b, StringComparer.Ordinal).ToArray());
    }

    private void Button(MouseEvent mouse, MouseActionTypeFlags down, MouseActionTypeFlags up, string name)
    {
        if ((mouse.ActionType & down) != 0) _buttons.Add(name);
        if ((mouse.ActionType & up) != 0) _buttons.Remove(name);
    }

    public BigInteger NextActionTime(BigInteger time)
    {
        foreach (var action in projection.Actions)
        {
            if (action.StartTime > time) return action.StartTime;
            if (action.StartTime + action.Wait > time) return action.StartTime + action.Wait;
        }
        return projection.TotalTime;
    }

    public IReadOnlyList<PreviewSegment> Segments(int maximum = 256)
    {
        if (maximum < 2) throw new ArgumentOutOfRangeException(nameof(maximum));
        var result = new List<PreviewSegment>();
        var actions = projection.Actions;
        // Large recordings use contiguous groups; no time is dropped to fit the display.
        var groupSize = Math.Max(1, (actions.Count + maximum / 2 - 1) / (maximum / 2));
        for (var i = 0; i < actions.Count; i += groupSize)
        {
            var last = Math.Min(i + groupSize, actions.Count) - 1;
            var a = actions[i];
            if (groupSize == 1 && a.Wait > 0)
                result.Add(new(a.StartTime, a.StartTime + a.Wait, true, i, i));
            var start = groupSize == 1 ? a.StartTime + a.Wait : a.StartTime;
            result.Add(new(start, actions[last].EndTime, false, i, last));
        }
        return result;
    }
}
