namespace MacroRecorderGUI.Models;

public sealed record RecordingOptions
{
    public int CountdownSeconds { get; init; } = 3;
    public ulong? OverrideDelay { get; init; }

    internal void Validate()
    {
        if (CountdownSeconds is < 0 or > 30)
            throw new ArgumentOutOfRangeException(nameof(CountdownSeconds), "Countdown must be between zero and 30 seconds.");
        if (OverrideDelay > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(OverrideDelay), "The delay is too large to play safely.");
    }
}
