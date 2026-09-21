using System.Numerics;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUI.Editor;

public enum ActionKind { Move, Click, Drag, Scroll, Keys, Sequence, Raw, Wait }
public enum CoordinateSpace { Unknown, AbsolutePrimary, AbsoluteDesktop, RelativeCounts }
public readonly record struct PathPosition(double X, double Y, CoordinateSpace Space);

/// <summary>A range into the original stream, never a replacement for its events.</summary>
public sealed class RecordedAction(int start, InputEvent first, BigInteger startTime) : ViewModelBase
{
    public int Start { get; } = start;
    public int Number { get; internal set; }
    public InputEvent First { get; } = first;
    public int Count { get; internal set; }
    public int MovementCount { get; internal set; }
    private Dictionary<CoordinateSpace, int>? _movementEdges;
    public int? MovementEdgeFor(CoordinateSpace space) =>
        _movementEdges is not null && _movementEdges.TryGetValue(space, out var end) ? end : null;

    internal void ObserveMovement(PathSample? previous, PathSample current)
    {
        MovementCount++;
        if (previous is not { } before || before.Position is not { } from || current.Position is not { } to
            || current.StartsSegment || before.Segment != current.Segment || from.Space != to.Space
            || from.X == to.X && from.Y == to.Y) return;
        // Keep one genuine adjacent pair per frame, even when display sampling omits an excursion.
        (_movementEdges ??= []).TryAdd(to.Space, current.Index);
    }
    public int End => Start + Count;
    public ActionKind Kind { get; internal set; }
    public bool Complete { get; internal set; }
    public BigInteger StartTime { get; } = startTime;
    public ulong Wait => First.TimeSinceLastEvent;
    public BigInteger Duration { get; internal set; }
    public BigInteger EndTime => StartTime + Wait + Duration;
    public string Detail { get; internal set; } = "";
    public string Name { get; internal set; } = "";
    public string Description { get; internal set; } = "";
    public IReadOnlyList<string> KeyLabels { get; internal set; } = [];
    public string DisplayNumber => Number.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    public string DisplayTime => Kind == ActionKind.Wait ? "Conditional" : TimeText.Human((BigInteger)Wait + Duration);
    public string Title => $"{Number}. {Name}";
    public bool CanEditDuration => Count > 1;
    public string EventCountLabel => EditorText.Count(Count, "event");
    public string Summary => Kind == ActionKind.Wait && First is WaitConditionEvent condition
        ? $"{TimeText.Human(Wait)} pause before · up to {TimeText.Human(condition.Condition.TimeoutUs)} · stop on failure"
        : $"{TimeText.Human(Wait)} wait + {TimeText.Human(Duration)} execution";
    public string TechnicalSummary => $"{Detail} · {EditorText.Count(Count, "raw event")} · "
        + (Count == 1 ? $"event {End:N0}" : $"events {Start + 1:N0}–{End:N0}");
    public string Warning => Complete ? "" : "Incomplete";
    public Microsoft.UI.Xaml.Visibility WarningVisibility => Complete ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public string WarningExplanation => Complete ? "" : "Missing or unmatched key/button transitions. Inspect Exact captured input before replaying or editing geometry.";
    public string Glyph => Kind switch
    {
        ActionKind.Move => "\uE7C2", ActionKind.Click => "\uE8B0", ActionKind.Drag => "\uE7C9",
        ActionKind.Wait => "\uE916",
        ActionKind.Scroll => "\uE8CB", ActionKind.Keys => "\uE765", ActionKind.Sequence => "\uE8FD", _ => "\uE713"
    };
    internal void Notify()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(DisplayNumber));
        OnPropertyChanged(nameof(DisplayTime));
        OnPropertyChanged(nameof(TechnicalSummary));
        OnPropertyChanged(nameof(Warning));
        OnPropertyChanged(nameof(WarningVisibility));
        OnPropertyChanged(nameof(WarningExplanation));
        OnPropertyChanged(nameof(Glyph));
    }
}
