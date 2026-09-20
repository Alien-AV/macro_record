namespace MacroRecorderGUI.Models;

public sealed record PlaybackOptions
{
    public TimeSpan Countdown { get; init; } = TimeSpan.FromSeconds(3);
    public int RepeatCount { get; init; } = 1;
    public double Speed { get; init; } = 1;

    internal void Validate()
    {
        if (Countdown < TimeSpan.Zero || Countdown > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(Countdown), "Countdown must be between zero and 60 seconds.");
        if (RepeatCount is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(RepeatCount), "Repeat count must be between 1 and 1000.");
        if (!double.IsFinite(Speed) || Speed is < 0.1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(Speed), "Speed must be between 0.1 and 10.");
    }
}

public enum PlaybackPhase { Idle, Countdown, Playing, BetweenRepeats, Stopping, Completed, Cancelled, Failed }

public sealed record PlaybackState(PlaybackPhase Phase, int CurrentRepeat, int RepeatCount,
    TimeSpan CountdownRemaining, TimeSpan Elapsed, string? Error = null)
{
    public static PlaybackState Idle { get; } = new(PlaybackPhase.Idle, 0, 0, TimeSpan.Zero, TimeSpan.Zero);
    public bool IsActive => Phase is PlaybackPhase.Countdown or PlaybackPhase.Playing
        or PlaybackPhase.BetweenRepeats or PlaybackPhase.Stopping;
}
