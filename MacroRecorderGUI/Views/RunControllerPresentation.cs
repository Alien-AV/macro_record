using MacroRecorderGUI.Models;

namespace MacroRecorderGUI.Views;

internal sealed record RunControllerPresentation(string State, string Clock, string Detail, string Note,
    bool Recording = false, bool Countdown = false, bool CanStop = true)
{
    public static RunControllerPresentation RecordingCountdown(double seconds) => new("Switch to your app",
        Math.Ceiling(seconds).ToString("0"), "Recording starts after the countdown",
        "Cancel at any time. No input is being recorded yet.", true, true);

    public static RunControllerPresentation ForRecording(bool capturing, bool finalizing, bool saving, TimeSpan elapsed, int count)
        => new(capturing ? "Recording" : finalizing ? "Finishing recording" : saving ? "Saving recording" : "Stopped",
            Elapsed(elapsed), $"{count:N0} raw events captured",
            capturing ? "Keyboard & mouse · use Ctrl + W to stop without clicking"
                : finalizing ? "Finishing the captured input" : saving ? "Saving to your local library" : "Recording has stopped",
            Recording: true, CanStop: capturing || finalizing);

    public static RunControllerPresentation ForPlayback(PlaybackState state, bool saving)
    {
        var title = state.Phase switch
        {
            PlaybackPhase.Idle => saving ? "Saving recording" : "Ready",
            PlaybackPhase.Countdown => "Switch to your app",
            PlaybackPhase.Playing => "Playing",
            PlaybackPhase.BetweenRepeats => "Between repeats",
            PlaybackPhase.Stopping => "Stopping…",
            PlaybackPhase.Completed => "Finished",
            PlaybackPhase.Cancelled => "Stopped",
            PlaybackPhase.Failed => "Playback failed",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        var countdown = state.Phase == PlaybackPhase.Countdown;
        var detail = countdown ? "Playback starts after the countdown"
            : state.Phase == PlaybackPhase.Idle ? "Playback has not started"
            : state.RepeatUntilStopped ? "Elapsed · until stopped"
            : state.CurrentRepeat > 0 ? $"Elapsed · repeat {state.CurrentRepeat} of {state.RepeatCount}"
            : "Elapsed time";
        var note = state.Error ?? (countdown ? "Real input will be sent to the focused app."
            : state.IsActive ? "Real input · elapsed time, not completion progress"
            : "No playback is running");
        return new(title, countdown ? Math.Ceiling(state.CountdownRemaining.TotalSeconds).ToString("0") : Elapsed(state.Elapsed),
            detail, note, Countdown: countdown, CanStop: state.IsActive);
    }
    private static string Elapsed(TimeSpan time) => $"{(long)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}";
}
