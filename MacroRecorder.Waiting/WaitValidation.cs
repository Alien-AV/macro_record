using ProtobufGenerated;

namespace MacroRecorder.Waiting;

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
        if (wait.ConditionCase == WaitCondition.ConditionOneofCase.None) Fail("A supported wait condition is required.");
        var classic = wait.ConditionCase is WaitCondition.ConditionOneofCase.Window or WaitCondition.ConditionOneofCase.Pixel;
        if (wait.SemanticsVersion != (classic ? 1u : 2u) || !Enum.IsDefined(wait.Trigger)) Fail("Unsupported wait semantics or trigger.");
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
            case WaitCondition.ConditionOneofCase.AccessibilityText:
                var text = wait.AccessibilityText;
                SingleSelector(text.Target);
                AccessibilitySelector(text.Element);
                if (text.Ancestors.Count > 16) Fail("Accessibility ancestor context is limited to 16 entries.");
                foreach (var ancestor in text.Ancestors) AccessibilitySelector(ancestor);
                if (!Enum.IsDefined(text.Source)) Fail("Unsupported accessibility text source.");
                Predicate(text.Predicate);
                ProviderTiming(wait, 250_000);
                break;
            case WaitCondition.ConditionOneofCase.OcrText:
                var ocr = wait.OcrText;
                Region(ocr.Region);
                if (ocr.Language.Length is < 1 or > 64 || !ocr.Language.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                    Fail("Choose one installed OCR language identifier.");
                Predicate(ocr.Predicate);
                ProviderTiming(wait, 500_000);
                break;
            case WaitCondition.ConditionOneofCase.Memory:
                var memory = wait.Memory;
                FullPath(memory.ExecutablePath);
                if (memory.AddressCase == MemoryCondition.AddressOneofCase.Module)
                {
                    FullPath(memory.Module.Path);
                    if (string.IsNullOrWhiteSpace(memory.Module.FileVersion) || memory.Module.FileVersion.Length > 128 || memory.Module.FileVersion.Contains('\0'))
                        Fail("Specify the module's exact file version.");
                }
                else if (memory.AddressCase != MemoryCondition.AddressOneofCase.AbsoluteAddress || memory.AbsoluteAddress == 0)
                    Fail("Specify a nonzero absolute address or a module-relative base.");
                if (memory.PointerOffsets.Count > 16) Fail("Pointer chains are limited to 16 explicit offsets.");
                if (!Enum.IsDefined(memory.ScalarType) || !Enum.IsDefined(memory.Comparison)) Fail("Unsupported memory scalar type or comparison.");
                var expected = ScalarValue.Parse(memory.ScalarType, memory.Expected);
                if (!double.IsFinite(memory.Tolerance) || memory.Tolerance < 0 || (!expected.IsFloating && memory.Tolerance != 0)
                    || (memory.Comparison is not (NumericComparison.NumericEquals or NumericComparison.NumericNotEquals) && memory.Tolerance != 0))
                    Fail("Tolerance must be finite and nonnegative, and applies only to floating-point equality or inequality.");
                ProviderTiming(wait, 100_000);
                break;
            default: Fail("A supported wait condition is required."); break;
        }
    }

    public static WaitCondition NewAccessibilityText() => new()
    {
        SemanticsVersion = 2, TimeoutUs = 30_000_000, StableForUs = 200_000, PollIntervalUs = 250_000,
        AccessibilityText = new() { Target = new(), Element = new(), Predicate = new() }
    };
    public static WaitCondition NewOcrText() => new()
    {
        SemanticsVersion = 2, TimeoutUs = 30_000_000, StableForUs = 200_000, PollIntervalUs = 500_000,
        OcrText = new() { Region = new() { Width = 320, Height = 100 }, Language = "eng", Predicate = new() }
    };
    public static WaitCondition NewMemory() => new()
    {
        SemanticsVersion = 2, TimeoutUs = 30_000_000, StableForUs = 200_000, PollIntervalUs = 100_000,
        Memory = new() { ScalarType = MemoryScalarType.Uint32, Expected = "0" }
    };
    private static void ProviderTiming(WaitCondition wait, ulong minimum)
    {
        if (wait.Trigger == WaitTrigger.NewWindow) Fail("New window requires a window condition.");
        if (wait.PollIntervalUs < minimum) Fail($"This provider requires polling of at least {minimum / 1000} ms.");
    }
    internal static void FullPath(string value)
    {
        if (value.Length is 0 or > 1024 || value.Contains('\0') || !Path.IsPathFullyQualified(value)) Fail("Specify a full path of at most 1024 characters, without NUL.");
    }
    private static void SingleSelector(WindowSelector? selector)
    {
        Selector(selector);
        if (selector!.AnyMatch) Fail("This condition requires a single target window.");
    }
    private static void AccessibilitySelector(AccessibilitySelector? selector)
    {
        if (selector is null || selector.AutomationId.Length > 1024 || selector.AutomationId.Contains('\0')
            || (selector.AutomationId.Length == 0 && selector.ControlType == 0)
            || (selector.ControlType != 0 && selector.ControlType is < 50000 or > 50040)) Fail("Specify an automation ID or a supported UIA control type.");
    }
    private static void Predicate(TextPredicate? predicate)
    {
        if (predicate is null || predicate.Expected.Length > TextPredicates.MaximumCharacters || predicate.Expected.Contains('\0')
            || !Enum.IsDefined(predicate.Comparison) || !Enum.IsDefined(predicate.Whitespace)) Fail("Unsupported text predicate or expected text exceeds 64 KiB.");
    }
    internal static void Region(OcrRegion? region)
    {
        if (region is null || !Enum.IsDefined(region.Coordinates) || region.Width is 0 or > 4096 || region.Height is 0 or > 4096
            || (ulong)region.Width * region.Height > 1_000_000 || (long)region.X + region.Width > int.MaxValue || (long)region.Y + region.Height > int.MaxValue)
            Fail("OCR region must be nonempty, at most one megapixel and at most 4096 pixels per dimension.");
        if (region!.Coordinates == PixelCoordinates.DesktopPhysical)
        { if (region.Target is not null) Fail("Desktop OCR regions do not have a window selector."); }
        else
        {
            SingleSelector(region.Target);
            if (region.X < 0 || region.Y < 0) Fail("Client region offsets cannot be negative.");
            if (region.Coordinates == PixelCoordinates.ClientLogical && region.ReferenceDpi is < 48 or > 768) Fail("Reference DPI must be between 48 and 768.");
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

    public static void ValidateSchedule(IEnumerable<IWaitScheduleEvent> source, bool loop = false)
    {
        var keys = new HashSet<uint>();
        uint buttons = 0;
        var hasWait = false;
        foreach (var input in source)
        {
            var wire = input.OriginalProtobufInputEvent;
            if (wire.WaitCondition is { } wait)
            {
                hasWait = true;
                Validate(wait);
                if (keys.Count != 0 || buttons != 0) Fail("Conditional waits require all recorded keys and mouse buttons to be released.");
            }
            else if (wire.Delay is { } delay)
            {
                if (delay.DurationMicroseconds > MaximumTimeoutUs) Fail("Fixed delay exceeds 24 hours.");
            }
            else if (wire.KeyboardEvent is { } key)
            {
                if (key.VirtualKeyCode is 0 or > 255) Fail("Invalid virtual key.");
                if (key.KeyUp) keys.Remove(key.VirtualKeyCode); else keys.Add(key.VirtualKeyCode);
            }
            else if (wire.MouseEvent is { } mouse)
            {
                var flags = (uint)mouse.ActionType;
                uint[] downs = [2, 8, 32, 128, 128], ups = [4, 16, 64, 256, 256];
                for (var i = 0; i < 5; i++)
                {
                    var x = mouse.WheelRotation == 0 ? 1u : mouse.WheelRotation;
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
        var target = wait.Window?.Target ?? wait.Pixel?.Target ?? wait.AccessibilityText?.Target ?? wait.OcrText?.Region?.Target;
        var name = target is null ? "desktop" : string.Join(" · ", new[] { target.ExecutablePath, target.WindowClass, target.Title }.Where(s => s.Length > 0));
        var basis = wait.Window is { } window ? $"window {window.Test.ToString().ToLowerInvariant()} · {name}"
            : wait.Pixel is { } pixel ? wait.Trigger == WaitTrigger.Changes
                ? $"pixel ({pixel.X}, {pixel.Y}) {pixel.Coordinates} differs from its first valid runtime sample by more than {pixel.Tolerance} per channel · {name}"
                : $"pixel ({pixel.X}, {pixel.Y}) {pixel.Coordinates} {(pixel.NotEqual ? "≠" : "=")} #{pixel.Rgb:X6} ±{pixel.Tolerance} · {name}"
            : wait.AccessibilityText is { } text ? $"accessibility {text.Source} · {text.Element?.AutomationId} (control {text.Element?.ControlType}) · {TextDescription(text.Predicate, wait.Trigger)} · {name}"
            : wait.OcrText is { } ocr ? $"OCR {ocr.Language} · {ocr.Region?.Coordinates} region ({ocr.Region?.X}, {ocr.Region?.Y}) {ocr.Region?.Width}×{ocr.Region?.Height} · {TextDescription(ocr.Predicate, wait.Trigger)} · {name}"
            : wait.Memory is { } memory ? $"memory {memory.ScalarType} "
                + (wait.Trigger == WaitTrigger.Changes ? $"differs from its first valid runtime sample (tolerance {memory.Tolerance})" : $"{memory.Comparison} {memory.Expected} (tolerance {memory.Tolerance})")
                + $" · {memory.ExecutablePath} · " + (memory.Module is { } module ? $"{module.Path} [{module.FileVersion}] + 0x{module.Offset:X}" : $"absolute 0x{memory.AbsoluteAddress:X}")
                + $" · {memory.PointerOffsets.Count} pointer step(s)"
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
    private static string TextDescription(TextPredicate? predicate, WaitTrigger trigger) => trigger == WaitTrigger.Changes
        ? "text differs from its first valid runtime sample" : $"{predicate?.Comparison} “{predicate?.Expected}”";
    private static void Fail(string message) => throw new ArgumentException(message);
}
