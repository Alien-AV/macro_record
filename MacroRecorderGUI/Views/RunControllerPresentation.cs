using MacroRecorderGUI.Models;

namespace MacroRecorderGUI.Views;

internal sealed record RunControllerPresentation(string State, string Clock, string Detail, string Note,
    bool Recording = false, bool Countdown = false, bool CanStop = true)
{
    public static RunControllerPresentation RecordingCountdown(double seconds) => new("Switch to your app",
        Math.Ceiling(seconds).ToString("0"), "seconds until recording",
        "No input is being recorded yet.", true, true);

    public static RunControllerPresentation ForRecording(bool capturing, bool finalizing, bool saving, TimeSpan elapsed, int count)
        => new(capturing ? "Recording" : finalizing ? "Finishing recording" : saving ? "Saving recording" : "Stopped",
            Elapsed(elapsed), $"{count:N0} raw events captured",
            capturing ? "Use Ctrl + W to stop without a pointer click."
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
        var detail = countdown ? "seconds until playback"
            : state.Phase == PlaybackPhase.Idle ? "Playback has not started"
            : state.Phase == PlaybackPhase.Stopping ? "Waiting for playback to stop"
            : state.RepeatUntilStopped ? (state.IsActive ? "Repeating until stopped" : "Repeat until stopped")
            : state.CurrentRepeat > 0 && state.RepeatCount > 1 ? $"Repeat {state.CurrentRepeat} of {state.RepeatCount}"
            : "";
        var note = state.Error ?? (countdown ? "No input yet. Playback uses the focused app."
            : state.Phase == PlaybackPhase.Playing ? "Sending input to the focused app."
            : state.Phase == PlaybackPhase.BetweenRepeats ? "Waiting for the next repeat."
            : state.Phase == PlaybackPhase.Stopping ? "Input may continue until playback stops."
            : saving ? "Saving to your local library"
            : "No playback is running");
        return new(title, countdown ? Math.Ceiling(state.CountdownRemaining.TotalSeconds).ToString("0") : Elapsed(state.Elapsed),
            detail, note, Countdown: countdown, CanStop: state.IsActive);
    }
    private static string Elapsed(TimeSpan time) => $"{(long)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}";
}
