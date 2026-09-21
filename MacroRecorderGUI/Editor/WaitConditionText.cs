using ProtobufGenerated;

namespace MacroRecorderGUI.Editor;

/// <summary>Readable condition wording; exact selectors and timing stay in the fields.</summary>
internal static class WaitConditionText
{
    public static string Describe(WaitCondition condition)
    {
        if (condition.SemanticsVersion != 1 || !Enum.IsDefined(condition.Trigger)) return "Unsupported wait condition — inspect exact input before playback.";
        if (condition.Window is { } window)
        {
            var selector = window.Target;
            var target = !string.IsNullOrWhiteSpace(selector?.Title) ? $"the “{selector.Title}” window"
                : !string.IsNullOrWhiteSpace(selector?.ExecutablePath) ? $"the {System.IO.Path.GetFileName(selector.ExecutablePath)} window"
                : !string.IsNullOrWhiteSpace(selector?.WindowClass) ? $"a {selector.WindowClass} window" : "the selected window";
            var predicate = window.Test switch
            {
                WindowTest.Exists => "exists", WindowTest.Visible => "is visible",
                WindowTest.Foreground => "is in the foreground", WindowTest.Absent => "is absent", _ => "matches an unsupported condition"
            };
            var state = window.Test switch
            {
                WindowTest.Exists => "existence", WindowTest.Absent => "absence",
                WindowTest.Visible => "visibility", WindowTest.Foreground => "foreground", _ => "unsupported window"
            };
            return condition.Trigger switch
            {
                WaitTrigger.BecomesTrue => $"Wait until {target} {predicate}, after first observing the opposite",
                WaitTrigger.Changes => $"Wait until {target} changes from its initial {state} state",
                WaitTrigger.NewWindow => $"Wait until a new matching window {predicate}",
                _ => $"Wait until {target} {predicate}"
            };
        }
        if (condition.Pixel is { } pixel)
        {
            var target = $"the pixel at ({pixel.X}, {pixel.Y})";
            if (condition.Trigger == WaitTrigger.Changes) return $"Wait until {target} changes from its starting color";
            return $"Wait until {target} is {(pixel.NotEqual ? "different from" : "close to")} #{pixel.Rgb:X6}"
                + (condition.Trigger == WaitTrigger.BecomesTrue ? ", after first observing the opposite" : "");
        }
        return "Unsupported wait condition — inspect exact input before playback.";
    }
}
