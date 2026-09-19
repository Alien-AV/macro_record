using System.Collections.Specialized;
using System.ComponentModel;
using System.Numerics;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;

namespace MacroRecorderGUI.Editor;

/// <summary>UI-independent editor. Capture appends are incremental; mutations are explicit transactions.</summary>
public sealed class ActionEditor : IDisposable
{
    private readonly MacroViewModel _macro;
    private readonly HashSet<InputEvent> _observed = [];
    private readonly List<Edit> _undo = [];
    private readonly Dictionary<RecordedAction, int> _selectedActionCounts = [];
    private long _selectionRevision = -1;
    private bool _mutating, _reset = true, _disposed;
    public ActionProjection Projection { get; } = new();
    public bool IsDirty { get; private set; } = true;
    public bool CanUndo => _undo.Count > 0;
    public bool RawSelection { get; private set; }
    public event EventHandler? Invalidated;

    public ActionEditor(MacroViewModel macro)
    {
        _macro = macro;
        macro.Events.CollectionChanged += CollectionChanged;
        macro.ContentReplaced += ContentReplaced;
        foreach (var input in macro.Events) Observe(input);
    }

    public int Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsDirty) return 0;
        if (_reset) Projection.Reset();
        var previousCount = Projection.ProcessedCount;
        for (var i = Projection.ProcessedCount; i < _macro.Events.Count; i++) Projection.Append(_macro.Events[i]);
        Projection.FlushNotifications();
        IsDirty = false; _reset = false;
        return Projection.ProcessedCount - previousCount;
    }

    public IReadOnlyList<InputEvent> EventsFor(RecordedAction action)
    {
        RequireCurrent(action);
        return _macro.Events.Skip(action.Start).Take(action.Count).ToArray();
    }

    public void SelectActions(IEnumerable<RecordedAction> actions)
    {
        Refresh();
        RawSelection = false;
        var current = actions.Where(Projection.IsCurrent).Distinct().OrderBy(a => a.Start).ToArray();
        var selected = current
            .SelectMany(a => _macro.Events.Skip(a.Start).Take(a.Count)).Distinct();
        _macro.ReplaceSelection(selected);
        _selectedActionCounts.Clear();
        foreach (var action in current) _selectedActionCounts.Add(action, action.Count);
        _selectionRevision = _macro.SelectionRevision;
    }

    public void SelectRawEvents(IEnumerable<InputEvent> events)
    {
        RawSelection = true;
        _selectedActionCounts.Clear();
        var selected = events.ToHashSet();
        _macro.ReplaceSelection(_macro.Events.Where(selected.Contains));
    }

    public int RefreshSelection(IEnumerable<RecordedAction> visibleSelection)
    {
        // A projection refresh must never broaden an explicitly selected raw subset.
        if (RawSelection) return 0;
        var actions = visibleSelection.ToArray();
        if (_selectionRevision != _macro.SelectionRevision || actions.Length != _selectedActionCounts.Count
            || actions.Any(a => !_selectedActionCounts.ContainsKey(a)))
        {
            SelectActions(actions);
            return _macro.SelectedEvents.Count;
        }
        var added = 0;
        foreach (var action in actions)
        {
            // Only the stream's final action can grow during append-only capture.
            for (var offset = _selectedActionCounts[action]; offset < action.Count; offset++)
            { _macro.SelectedEvents.Add(_macro.Events[action.Start + offset]); added++; }
            _selectedActionCounts[action] = action.Count;
        }
        return added;
    }

    public IReadOnlyList<RecordedAction> SelectedActions()
    {
        Refresh();
        var selection = _macro.SelectedEvents.ToHashSet();
        var result = new HashSet<RecordedAction>();
        for (var i = 0; i < _macro.Events.Count; i++)
            if (selection.Contains(_macro.Events[i]) && Projection.ActionAt(i) is { } action) result.Add(action);
        return result.OrderBy(a => a.Start).ToArray();
    }

    public void SetWait(RecordedAction action, ulong microseconds)
    {
        RequireCurrent(action);
        Execute("Change wait", () => action.First.TimeSinceLastEvent = microseconds);
    }

    public void SetDuration(RecordedAction action, BigInteger microseconds)
    {
        RequireCurrent(action);
        var count = action.Count - 1;
        if (microseconds < 0 || microseconds > (BigInteger)ulong.MaxValue * count)
            throw new ArgumentException("Duration cannot fit in this action's internal event delays.");
        if (count == 0 && microseconds != 0) throw new ArgumentException("A single event has no internal duration. Edit its wait instead.");
        if (count == 0) return;
        var delays = new ulong[count];
        BigInteger cumulative = 0, assigned = 0;
        for (var i = 0; i < count; i++)
        {
            cumulative += _macro.Events[action.Start + i + 1].TimeSinceLastEvent;
            var target = action.Duration == 0 ? microseconds * (i + 1) / count : microseconds * cumulative / action.Duration;
            var delay = target - assigned;
            if (delay > ulong.MaxValue) throw new ArgumentException("A scaled event delay would exceed UInt64. Use a smaller duration.");
            delays[i] = (ulong)delay; assigned = target;
        }
        Execute("Change duration", () =>
        {
            for (var i = 0; i < count; i++) _macro.Events[action.Start + i + 1].TimeSinceLastEvent = delays[i];
        });
    }

    public string? GeometryBlockReason(RecordedAction action)
    {
        // Passive presentation queries never refresh or throw on a stale selection.
        if (IsDirty || !Projection.IsCurrent(action)) return "The recording changed. Refresh the selection before editing geometry.";
        if (action.Kind is not (ActionKind.Move or ActionKind.Drag)) return "Select a complete move or drag. Stationary actions inherit the preceding position.";
        if (action.Kind == ActionKind.Drag && Projection.Samples[action.Start].Position is null)
            return "The drag starts at an unknown position. An absolute movement anchor is required before the button press.";
        if (Projection.IncompleteActionCount > 0) return "Incomplete or anomalous input sequences prevent safe geometric edits. Inspect the raw input first.";
        if (Projection.HasRelativeMovement) return "Relative movement records device counts, not pixels. Explicitly convert an anchored estimate before editing geometry.";
        if (Projection.MovementSpaceCount != 1) return "Mixed primary-screen and virtual-desktop coordinate frames cannot be edited as one connected path.";
        return null;
    }

    public void SetDestination(RecordedAction action, int x, int y)
    {
        RequireCurrent(action);
        var reason = GeometryBlockReason(action);
        if (reason is not null) throw new ArgumentException(reason);
        var moves = _macro.Events.Skip(action.Start).Take(action.Count).OfType<MouseEvent>().Where(ActionProjection.HasMove).ToArray();
        var dx = (long)x - moves[^1].X; var dy = (long)y - moves[^1].Y;
        var changes = new List<(MouseEvent Event, int X, int Y)>();
        var anchored = action.Start > 0 && Projection.Samples[action.Start - 1].Position is not null;
        for (var i = 0; i < moves.Length; i++)
        {
            var weight = moves.Length == 1 ? 1d : anchored ? (i + 1d) / moves.Length : (double)i / (moves.Length - 1);
            Plan(moves[i], weight);
        }
        // Display groups may contain multiple mouse gestures under a held modifier. Preserve the first
        // downstream approach's own endpoint, before a button/key transition or a new movement pause.
        var following = new List<MouseEvent>();
        for (var index = action.End; index < _macro.Events.Count; index++)
        {
            var input = _macro.Events[index];
            if (following.Count > 0 && (input is KeyboardEvent || input.TimeSinceLastEvent >= ActionProjection.MovementPause)) break;
            if (input is not MouseEvent mouse) continue;
            if (ActionProjection.HasMove(mouse)) following.Add(mouse);
            if (following.Count > 0 && mouse.ActionType != Common.MouseActionTypeFlags.Move) break;
        }
        for (var i = 0; i < following.Count; i++) Plan(following[i], 1d - (i + 1d) / following.Count);
        Execute("Move destination", () => { foreach (var c in changes) { c.Event.X = c.X; c.Event.Y = c.Y; } });
        void Plan(MouseEvent input, double weight)
        {
            var nx = Math.Round(input.X + dx * weight); var ny = Math.Round(input.Y + dy * weight);
            if (nx < int.MinValue || nx > int.MaxValue || ny < int.MinValue || ny > int.MaxValue)
                throw new ArgumentException("The edited path would exceed the signed 32-bit coordinate range.");
            changes.Add((input, (int)nx, (int)ny));
        }
    }

    public void ConvertAnchoredEstimate()
    {
        // Validate the entire conversion first; never clamp or half-convert overflowing input.
        var changes = new List<(MouseEvent Event, int X, int Y)>();
        long x = 0, y = 0; var anchored = false;
        foreach (var input in _macro.Events.OfType<MouseEvent>().Where(ActionProjection.HasMove))
        {
            if (!input.RelativePosition) { x = input.X; y = input.Y; anchored = true; continue; }
            if (!anchored) continue;
            x += input.X; y += input.Y;
            if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
                throw new ArgumentException("The estimated path overflows pixel coordinates. No events were converted.");
            changes.Add((input, (int)x, (int)y));
        }
        if (changes.Count == 0) throw new ArgumentException("No convertible relative moves follow an absolute anchor. Unanchored counts cannot be located on a screen.");
        Execute("Convert anchored count estimate", () =>
        {
            foreach (var c in changes) { c.Event.X = c.X; c.Event.Y = c.Y; c.Event.RelativePosition = false; c.Event.MappedToVirtualDesktop = true; }
        });
    }

    public void EditRaw(InputEvent input, ProtobufInputEvent value)
    {
        if (!_macro.Events.Contains(input) || input.Type != (InputEvent.InputEventType)value.EventCase)
            throw new ArgumentException("Select a current raw event of the same type.");
        Execute("Edit raw event", () => Apply(input, value));
    }

    public void Execute(string description, Action mutation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var before = _macro.Events.ToArray();
        var selection = _macro.SelectedEvents.ToArray();
        var rawSelection = RawSelection;
        var values = before.ToDictionary(e => e, e => e.OriginalProtobufInputEvent.Clone());
        _mutating = true;
        try { mutation(); }
        finally { _mutating = false; MarkDirty(reset: true); }
        var after = _macro.Events.ToArray();
        var patches = before.Where(e => !values[e].Equals(e.OriginalProtobufInputEvent))
            .Select(e => new Patch(e, values[e], e.OriginalProtobufInputEvent.Clone())).ToArray();
        if (patches.Length == 0 && before.SequenceEqual(after)) return;
        _undo.Add(new(description, before, after, patches, selection, rawSelection));
        // Bound retained raw references as well as command count on large recordings.
        while (_undo.Count > 1 && (_undo.Count > 30 || _undo.Sum(e => (long)e.Before.Length + e.After.Length) > 1_000_000)) _undo.RemoveAt(0);
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var edit = _undo[^1];
        if (_macro.Events.Count < edit.After.Length || !_macro.Events.Take(edit.After.Length).SequenceEqual(edit.After)
            || edit.Patches.Any(p => !p.After.Equals(p.Event.OriginalProtobufInputEvent)))
        { _undo.Clear(); Invalidated?.Invoke(this, EventArgs.Empty); return false; }
        _undo.RemoveAt(_undo.Count - 1);
        var appended = _macro.Events.Skip(edit.After.Length).ToArray();
        _mutating = true;
        try
        {
            foreach (var patch in edit.Patches) Apply(patch.Event, patch.Before);
            if (!edit.Before.SequenceEqual(edit.After))
            {
                _macro.Events.Clear();
                foreach (var input in edit.Before.Concat(appended)) _macro.Events.Add(input);
            }
            var currentEvents = _macro.Events.ToHashSet();
            _macro.ReplaceSelection(edit.Selection.Where(currentEvents.Contains));
            RawSelection = edit.RawSelection;
        }
        finally { _mutating = false; MarkDirty(reset: true); }
        Invalidated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static void Apply(InputEvent input, ProtobufInputEvent value)
    {
        input.TimeSinceLastEvent = value.TimeSinceLastEvent;
        if (input is MouseEvent m)
        {
            var v = value.MouseEvent;
            m.X = v.X; m.Y = v.Y; m.ActionType = (Common.MouseActionTypeFlags)v.ActionType;
            m.MouseData = v.WheelRotation; m.RelativePosition = v.RelativePosition; m.MappedToVirtualDesktop = v.MappedToVirtualDesktop;
        }
        else if (input is KeyboardEvent k) { k.VirtualKeyCode = value.KeyboardEvent.VirtualKeyCode; k.KeyUp = value.KeyboardEvent.KeyUp; }
    }

    private void RequireCurrent(RecordedAction action)
    {
        Refresh();
        if (!Projection.IsCurrent(action)) throw new ArgumentException("The recording changed. Select the action again.");
    }
    public void InvalidateUndo()
    {
        if (_undo.Count == 0) return;
        _undo.Clear();
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
    private void Observe(InputEvent input) { if (_observed.Add(input)) input.PropertyChanged += EventChanged; }
    private void Unobserve(InputEvent input) { if (_observed.Remove(input)) input.PropertyChanged -= EventChanged; }
    private void EventChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_mutating) _undo.Clear();
        MarkDirty(reset: true);
    }
    private void ContentReplaced(object? sender, EventArgs args)
    {
        if (!_mutating) _undo.Clear();
        MarkDirty(reset: true);
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        var append = args.Action == NotifyCollectionChangedAction.Add && args.NewStartingIndex + args.NewItems!.Count == _macro.Events.Count;
        if (args.Action == NotifyCollectionChangedAction.Reset)
        { foreach (var e in _observed.ToArray()) Unobserve(e); foreach (var e in _macro.Events) Observe(e); }
        else
        {
            if (args.OldItems is not null) foreach (InputEvent e in args.OldItems) Unobserve(e);
            if (args.NewItems is not null) foreach (InputEvent e in args.NewItems) Observe(e);
        }
        if (!_mutating && !append) _undo.Clear();
        if (args.Action != NotifyCollectionChangedAction.Add) _macro.SelectedEvents.RemoveAll(e => !_observed.Contains(e));
        MarkDirty(reset: !append);
    }
    private void MarkDirty(bool reset)
    {
        var notify = !IsDirty;
        IsDirty = true; _reset |= reset;
        if (!_mutating && notify) Invalidated?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _macro.Events.CollectionChanged -= CollectionChanged;
        _macro.ContentReplaced -= ContentReplaced;
        foreach (var e in _observed.ToArray()) Unobserve(e);
        _undo.Clear(); Invalidated = null;
    }
    private sealed record Patch(InputEvent Event, ProtobufInputEvent Before, ProtobufInputEvent After);
    private sealed record Edit(string Description, InputEvent[] Before, InputEvent[] After, Patch[] Patches, InputEvent[] Selection, bool RawSelection);
}
