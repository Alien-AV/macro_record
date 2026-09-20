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

    public static IReadOnlyList<PathSample> Decimate(IReadOnlyList<PathSample> samples, int start, int count,
        int budget = 1200, int? requiredEdgeEnd = null)
    {
        if (budget < 2) throw new ArgumentOutOfRangeException(nameof(budget));
        if (count == 0) return [];
        if (requiredEdgeEnd is { } edge && (edge <= start || edge >= start + count))
            throw new ArgumentOutOfRangeException(nameof(requiredEdgeEnd));
        var preserveEdge = count > budget && requiredEdgeEnd is not null;
        if (preserveEdge && budget < 4) throw new ArgumentOutOfRangeException(nameof(budget));
        var result = new List<PathSample>(Math.Min(count, budget));
        var size = Math.Min(count, budget - (preserveEdge ? 2 : 0));
        var indices = new List<int>(Math.Min(count, budget));
        for (var i = 0; i < size; i++)
        {
            var offset = size == 1 ? 0 : (int)((long)i * (count - 1) / (size - 1));
            indices.Add(start + offset);
        }
        if (preserveEdge)
        {
            indices.Add(requiredEdgeEnd!.Value - 1); indices.Add(requiredEdgeEnd.Value);
            indices.Sort();
        }
        foreach (var index in indices)
        {
            var sample = samples[index];
            if (result.Count > 0 && result[^1].Index == sample.Index) continue;
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
