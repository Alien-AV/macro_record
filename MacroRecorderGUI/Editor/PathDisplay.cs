namespace MacroRecorderGUI.Editor;

/// <summary>Bounded display sampling only. Editing, timing and playback always use the full stream.</summary>
public static class PathDisplay
{
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
