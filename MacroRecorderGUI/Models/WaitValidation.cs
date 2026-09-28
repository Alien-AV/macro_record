using MacroRecorderGUI.Event;
using ProtobufGenerated;

namespace MacroRecorderGUI.Models;

public static class WaitValidation
{
    public const ulong MaximumTimeoutUs = 86_400_000_000;
    public static WaitCondition NewWindow() => new()
    {
        SemanticsVersion = 1, TimeoutUs = 30_000_000, StableForUs = 200_000, PollIntervalUs = 100_000,
        Window = new() { Target = new(), Test = WindowTest.Visible }
    };

    public static void Validate(WaitCondition wait)
    {
        if (wait.SemanticsVersion != 1 || !Enum.IsDefined(wait.Trigger)) Fail("Unsupported wait semantics or trigger.");
        if (wait.TimeoutUs is < 1_000 or > MaximumTimeoutUs) Fail("Timeout must be between 1 ms and 24 hours.");
        if (wait.StableForUs > wait.TimeoutUs) Fail("Stability must be between zero and the timeout.");
        if (wait.PollIntervalUs is < 10_000 or > 1_000_000) Fail("Polling must be between 10 and 1000 ms.");
        switch (wait.ConditionCase)
        {
            case WaitCondition.ConditionOneofCase.Window:
                Selector(wait.Window.Target);
                if (!Enum.IsDefined(wait.Window.Test)) Fail("Unsupported window condition.");
                if (wait.Trigger == WaitTrigger.NewWindow && wait.Window.Test is not (WindowTest.Exists or WindowTest.Visible))
                    Fail("New window requires Exists or Visible.");
                if (wait.Trigger == WaitTrigger.Changes && (wait.Window.Target.AnyMatch || wait.Window.Test is WindowTest.Absent or WindowTest.Exists))
                    Fail("Changes requires a single target and the Visible or Foreground condition.");
                break;
            case WaitCondition.ConditionOneofCase.Pixel:
                var pixel = wait.Pixel;
                if (!Enum.IsDefined(pixel.Coordinates) || pixel.Rgb > 0xffffff || pixel.Tolerance > 255)
                    Fail("Choose supported pixel coordinates, a six-digit RGB color, and tolerance 0–255.");
                if (wait.Trigger == WaitTrigger.NewWindow) Fail("New window requires a window condition.");
                if (pixel.Coordinates != PixelCoordinates.DesktopPhysical)
                {
                    Selector(pixel.Target);
                    if (pixel.Target.AnyMatch) Fail("A client pixel requires a single target window.");
                    if (pixel.X < 0 || pixel.Y < 0) Fail("Client offsets cannot be negative.");
                    if (pixel.Coordinates == PixelCoordinates.ClientLogical && pixel.ReferenceDpi is < 48 or > 768)
                        Fail("Reference DPI must be between 48 and 768.");
                }
                else if (pixel.Target is not null) Fail("Desktop pixels do not use a window selector.");
                if (pixel.ReferenceWidth > 1_000_000 || pixel.ReferenceHeight > 1_000_000) Fail("Reference dimensions exceed the supported limit.");
                break;
            default: Fail("A supported window or pixel condition is required."); break;
        }
    }

    private static void Selector(WindowSelector? selector)
    {
        if (selector is null) Fail("A window selector is required.");
        if (!Enum.IsDefined(selector!.TitleMatch)) Fail("Unsupported title matching rule.");
        foreach (var value in new[] { selector.ExecutablePath, selector.WindowClass, selector.Title })
            if (value.Length > 1024 || value.Contains('\0')) Fail("Window selector fields must contain at most 1024 characters and no NUL.");
        if (string.IsNullOrWhiteSpace(selector.ExecutablePath) && string.IsNullOrWhiteSpace(selector.WindowClass)
            && string.IsNullOrWhiteSpace(selector.Title)) Fail("Specify an executable path, window class, or title.");
        if (selector.ExecutablePath.Length > 0 && !Path.IsPathFullyQualified(selector.ExecutablePath)) Fail("Use a full executable path.");
    }

    public static void ValidateSchedule(IEnumerable<InputEvent> source, bool loop = false)
    {
        var keys = new HashSet<uint>();
        uint buttons = 0;
        var hasWait = false;
        foreach (var input in source)
        {
            if (input is WaitConditionEvent wait)
            {
                hasWait = true;
                Validate(wait.Condition);
                if (keys.Count != 0 || buttons != 0) Fail("Conditional waits require all recorded keys and mouse buttons to be released.");
            }
            else if (input is DelayEvent delay) DelayEvent.ValidateDuration(delay.DurationMicroseconds);
            else if (input is KeyboardEvent key)
            {
                if (key.VirtualKeyCode is 0 or > 255) Fail("Invalid virtual key.");
                if (key.KeyUp) keys.Remove(key.VirtualKeyCode); else keys.Add(key.VirtualKeyCode);
            }
            else if (input is MouseEvent mouse)
            {
                var flags = (uint)mouse.ActionType;
                uint[] downs = [2, 8, 32, 128, 128], ups = [4, 16, 64, 256, 256];
                for (var i = 0; i < 5; i++)
                {
                    var x = mouse.MouseData == 0 ? 1u : mouse.MouseData;
                    if (i >= 3 && (x & (1u << (i - 3))) == 0) continue;
                    if ((flags & downs[i]) != 0) buttons |= 1u << i;
                    if ((flags & ups[i]) != 0) buttons &= ~(1u << i);
                }
            }
            else Fail("Unsupported event payload.");
        }
        if (loop && hasWait && (keys.Count > 0 || buttons != 0)) Fail("A looping macro with waits must end with all inputs released.");
    }

    public static string Describe(WaitCondition wait)
    {
        var target = wait.Window?.Target ?? wait.Pixel?.Target;
        var name = target is null ? "desktop" : string.Join(" · ", new[] { target.ExecutablePath, target.WindowClass, target.Title }.Where(s => s.Length > 0));
        var basis = wait.Window is { } window ? $"window {window.Test.ToString().ToLowerInvariant()} · {name}"
            : wait.Pixel is { } pixel ? wait.Trigger == WaitTrigger.Changes
                ? $"pixel ({pixel.X}, {pixel.Y}) {pixel.Coordinates} differs from its first valid runtime sample by more than {pixel.Tolerance} per channel · {name}"
                : $"pixel ({pixel.X}, {pixel.Y}) {pixel.Coordinates} {(pixel.NotEqual ? "≠" : "=")} #{pixel.Rgb:X6} ±{pixel.Tolerance} · {name}"
            : "unsupported wait condition";
        return wait.Trigger switch
        {
            WaitTrigger.IsTrue => "Is true: " + basis,
            WaitTrigger.BecomesTrue => "Becomes true after observing false: " + basis,
            WaitTrigger.Changes => "Changes from runtime baseline: " + basis,
            WaitTrigger.NewWindow => "New instance outside the initial matching set: " + basis,
            _ => "Unsupported trigger: " + basis
        };
    }
    private static void Fail(string message) => throw new ArgumentException(message);
}
