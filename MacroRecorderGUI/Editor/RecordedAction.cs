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
    public InputEvent First { get; } = first;
    public int Count { get; internal set; }
    public int End => Start + Count;
    public ActionKind Kind { get; internal set; }
    public bool Complete { get; internal set; }
    public BigInteger StartTime { get; } = startTime;
    public ulong Wait => First.TimeSinceLastEvent;
    public BigInteger Duration { get; internal set; }
    public BigInteger EndTime => StartTime + Wait + Duration;
    public string Detail { get; internal set; } = "";
    public string Title => $"{Start + 1}. {Detail}";
    public string Summary => $"{Count:N0} events · wait {TimeText.Human(Wait)} · duration {TimeText.Human(Duration)}";
    internal void Notify()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Summary));
    }
}
