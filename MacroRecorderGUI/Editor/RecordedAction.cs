using System.Numerics;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUI.Editor;

public enum ActionKind { Move, Click, Drag, Scroll, Keys, Sequence, Raw }
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
    public string DisplayTime => TimeText.Human((BigInteger)Wait + Duration);
    public string Title => $"{Number}. {Name}";
    public bool CanEditDuration => Count > 1;
    public string EventCountLabel => EditorText.Count(Count, "event");
    public string Summary => $"{TimeText.Human(Wait)} wait + {TimeText.Human(Duration)} execution";
    public string TechnicalSummary => $"{Detail} · {EditorText.Count(Count, "raw event")} · "
        + (Count == 1 ? $"event {End:N0}" : $"events {Start + 1:N0}–{End:N0}");
    public string Warning => Complete ? "" : "Incomplete";
    public Microsoft.UI.Xaml.Visibility WarningVisibility => Complete ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public string WarningExplanation => Complete ? "" : "Missing or unmatched key/button transitions. Inspect Exact captured input before replaying or editing geometry.";
    public string Glyph => Kind switch
    {
        ActionKind.Move => "\uE7C2", ActionKind.Click => "\uE8B0", ActionKind.Drag => "\uE7C9",
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
