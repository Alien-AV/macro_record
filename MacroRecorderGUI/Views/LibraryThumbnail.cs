using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;

namespace MacroRecorderGUI.Views;

/// <summary>A representative capture segment, fitted in its own recorded coordinate frame.</summary>
public sealed record LibraryThumbnail(IReadOnlyList<PathSample> Samples, string Label, string InputSummary)
{
    public bool HasTrace => Samples.Count > 1;

    public static LibraryThumbnail Create(IReadOnlyList<PathSample> samples, IReadOnlyList<InputEvent> events)
    {
        List<PathSample> best = [], current = [];
        PathSample? previous = null;
        void Finish()
        {
            // Compare observed changes, never distances in incompatible units (counts versus pixels).
            if (current.Count > best.Count) best = current;
            current = [];
        }
        foreach (var sample in samples)
        {
            if (sample.Position is not { } position || position.Space == CoordinateSpace.Unknown
                || !double.IsFinite(position.X) || !double.IsFinite(position.Y))
            { Finish(); previous = null; continue; }
            if (previous is not { } before || sample.StartsSegment || before.Segment != sample.Segment
                || before.Position!.Value.Space != position.Space) Finish();
            if (current.Count == 0 || current[^1].Position != sample.Position) current.Add(sample);
            previous = sample;
        }
        Finish();
        var summary = DescribeInput(events);
        if (best.Count < 2) return new([], "No pointer path to show", summary);

        // Keep extrema as well as evenly spaced samples, including brief excursions and closed loops.
        const int budget = 160;
        var indices = new SortedSet<int> { 0, best.Count - 1 };
        for (var i = 0; i < budget - 4; i++) indices.Add((int)((long)i * (best.Count - 1) / (budget - 5)));
        indices.Add(Enumerable.Range(0, best.Count).MinBy(i => best[i].Position!.Value.X));
        indices.Add(Enumerable.Range(0, best.Count).MaxBy(i => best[i].Position!.Value.X));
        indices.Add(Enumerable.Range(0, best.Count).MinBy(i => best[i].Position!.Value.Y));
        indices.Add(Enumerable.Range(0, best.Count).MaxBy(i => best[i].Position!.Value.Y));
        var trace = indices.Select(i => best[i]).ToArray();
        var label = trace[0].Position!.Value.Space switch
        {
            CoordinateSpace.RelativeCounts => "Relative device counts · starting position unknown",
            CoordinateSpace.AbsoluteDesktop => "Path segment · virtual desktop pixels",
            _ => "Path segment · primary screen pixels"
        };
        return new(trace, label, summary);
    }

    private static string DescribeInput(IReadOnlyList<InputEvent> events)
    {
        if (events.Count == 0) return "No input recorded";
        var keyboard = events.Count(input => input is KeyboardEvent);
        var mouse = events.Count(input => input is MouseEvent);
        var other = events.Count - keyboard - mouse;
        var parts = new List<string>();
        void Add(int count, string kind)
        {
            if (count > 0) parts.Add($"{count:N0} {kind} {(count == 1 ? "event" : "events")}");
        }
        Add(keyboard, "keyboard"); Add(mouse, "mouse"); Add(other, "other input");
        return string.Join(" · ", parts);
    }
}
