namespace MacroRecorderGUI.Editor;

/// <summary>Bounded display sampling only. Editing, timing and playback always use the full stream.</summary>
public static class PathDisplay
{
    /// <summary>Direction cues on the displayed trace only, with no connection across frame/segment gaps.</summary>
    public static IReadOnlyList<(PathPosition From, PathPosition To)> Directions(
        IReadOnlyList<PathSample> samples, CoordinateSpace space, double scale, int budget = 8)
    {
        if (budget < 1) throw new ArgumentOutOfRangeException(nameof(budget));
        var candidates = new List<(PathPosition, PathPosition)>();
        double distance = 0;
        for (var i = 1; i < samples.Count; i++)
        {
            var previous = samples[i - 1]; var current = samples[i];
            if (current.StartsSegment || previous.Segment != current.Segment
                || previous.Position is not { } from || current.Position is not { } to
                || from.Space != space || to.Space != space)
            { distance = 0; continue; }
            var dx = (to.X - from.X) * scale; var dy = (to.Y - from.Y) * scale;
            var length = Math.Sqrt(dx * dx + dy * dy);
            distance += length;
            if (distance < 80 || length < 2) continue;
            candidates.Add((from, to)); distance = 0;
        }
        if (candidates.Count <= budget) return candidates;
        return Enumerable.Range(0, budget).Select(i => candidates[budget == 1 ? 0 : (int)((long)i * (candidates.Count - 1) / (budget - 1))]).ToArray();
    }

    public static IReadOnlyList<PathSample> Decimate(IReadOnlyList<PathSample> samples, int start, int count, int budget = 1200)
    {
        if (budget < 2) throw new ArgumentOutOfRangeException(nameof(budget));
        if (count == 0) return [];
        var result = new List<PathSample>(Math.Min(count, budget));
        var size = Math.Min(count, budget);
        for (var i = 0; i < size; i++)
        {
            var offset = size == 1 ? 0 : (int)((long)i * (count - 1) / (size - 1));
            var sample = samples[start + offset];
            // Never connect points across an omitted coordinate-space boundary.
            if (result.Count > 0)
            {
                if (result[^1].Segment != sample.Segment) sample = sample with { StartsSegment = true };
            }
            result.Add(sample);
        }
        return result;
    }
}
