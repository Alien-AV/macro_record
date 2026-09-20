using MacroRecorderGUI.Models;

namespace MacroRecorderGUI.Views;

internal static class RunSettingsPresentation
{
    public static string PlaybackSummary(bool loaded, PlaybackOptions options) => !loaded ? "Loading playback options…"
        : $"{options.Speed:0.##}× · {(options.RepeatUntilStopped ? "Until stopped" : options.RepeatCount == 1 ? "Once" : $"{options.RepeatCount} repeats")} · {options.Countdown.TotalSeconds:0.##}s delay"
            + (options.PointerOrigin == PlaybackPointerOrigin.CurrentPointer ? " · Current pointer" : " · Recorded starting point");
}
