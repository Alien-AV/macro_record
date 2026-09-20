namespace MacroRecorderGUI.Models;

public sealed record PlaybackOptions
{
    public TimeSpan Countdown { get; init; } = TimeSpan.FromSeconds(3);
    public int RepeatCount { get; init; } = 1;
    /// <summary>Use one native looping session; RepeatCount is ignored.</summary>
    public bool RepeatUntilStopped { get; init; }
    public double Speed { get; init; } = 1;
    public PlaybackPointerOrigin PointerOrigin { get; init; } = PlaybackPointerOrigin.RecordedStartingPoint;

    internal void Validate()
    {
        if (!Enum.IsDefined(PointerOrigin)) throw new ArgumentOutOfRangeException(nameof(PointerOrigin), "Choose a supported pointer origin.");
        if (Countdown < TimeSpan.Zero || Countdown > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(Countdown), "Countdown must be between zero and 60 seconds.");
        if (!RepeatUntilStopped && RepeatCount is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(RepeatCount), "Repeat count must be between 1 and 1000.");
        if (!double.IsFinite(Speed) || Speed is < 0.1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(Speed), "Speed must be between 0.1 and 10.");
    }
}

public enum PlaybackPhase { Idle, Countdown, Playing, BetweenRepeats, Stopping, Completed, Cancelled, Failed }

/// <summary>Repeat counters are zero when RepeatUntilStopped is true: native loop progress is unavailable.</summary>
public sealed record PlaybackState(PlaybackPhase Phase, int CurrentRepeat, int RepeatCount,
    TimeSpan CountdownRemaining, TimeSpan Elapsed, string? Error = null, bool RepeatUntilStopped = false)
{
    public static PlaybackState Idle { get; } = new(PlaybackPhase.Idle, 0, 0, TimeSpan.Zero, TimeSpan.Zero);
    public bool IsActive => Phase is PlaybackPhase.Countdown or PlaybackPhase.Playing
        or PlaybackPhase.BetweenRepeats or PlaybackPhase.Stopping;
}
