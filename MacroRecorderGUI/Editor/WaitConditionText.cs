using ProtobufGenerated;

namespace MacroRecorderGUI.Editor;

/// <summary>Readable condition wording; exact selectors and timing stay in the fields.</summary>
internal static class WaitConditionText
{
    public static string Describe(WaitCondition condition)
    {
        var extended = condition.AccessibilityText is not null || condition.OcrText is not null || condition.Memory is not null;
        if (condition.SemanticsVersion != (extended ? 2 : 1) || !Enum.IsDefined(condition.Trigger)
            || extended && condition.Trigger == WaitTrigger.NewWindow) return Unsupported;
        if (condition.Window is { } window)
        {
            var target = WindowName(window.Target);
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
        if (condition.AccessibilityText is { } accessibility)
        {
            if (!Enum.IsDefined(accessibility.Source)) return Unsupported;
            var source = accessibility.Source switch
            {
                AccessibilityTextSource.AccessibleName => "accessible name",
                AccessibilityTextSource.TextPattern => "document text",
                _ => "control value"
            };
            var element = string.IsNullOrWhiteSpace(accessibility.Element?.AutomationId) ? "the selected element" : $"element “{Short(accessibility.Element.AutomationId)}”";
            return TextSentence(condition, $"the {source} of {element} in {WindowName(accessibility.Target)}", accessibility.Predicate);
        }
        if (condition.OcrText is { } ocr)
        {
            if (ocr.Region is not { } region || !Enum.IsDefined(region.Coordinates)) return Unsupported;
            var location = region.Coordinates == PixelCoordinates.DesktopPhysical ? "desktop" : $"client area of {WindowName(region.Target)}";
            return TextSentence(condition, $"OCR text ({ocr.Language}) in the {region.Width} × {region.Height} region at ({region.X}, {region.Y}) in the {location}", ocr.Predicate);
        }
        if (condition.Memory is { } memory)
        {
            if (!Enum.IsDefined(memory.ScalarType) || !Enum.IsDefined(memory.Comparison)) return Unsupported;
            var kind = memory.ScalarType switch
            {
                MemoryScalarType.Uint8 => "unsigned 8-bit", MemoryScalarType.Int8 => "signed 8-bit",
                MemoryScalarType.Uint16 => "unsigned 16-bit", MemoryScalarType.Int16 => "signed 16-bit",
                MemoryScalarType.Uint32 => "unsigned 32-bit", MemoryScalarType.Int32 => "signed 32-bit",
                MemoryScalarType.Uint64 => "unsigned 64-bit", MemoryScalarType.Int64 => "signed 64-bit",
                MemoryScalarType.Float32 => "32-bit floating-point", _ => "64-bit floating-point"
            };
            var location = memory.AddressCase switch
            {
                MemoryCondition.AddressOneofCase.AbsoluteAddress => $"0x{memory.AbsoluteAddress:X}",
                MemoryCondition.AddressOneofCase.Module => $"{System.IO.Path.GetFileName(memory.Module.Path)} + 0x{memory.Module.Offset:X}",
                _ => "an unspecified address"
            };
            var chain = memory.PointerOffsets.Count == 0 ? "" : $", through {memory.PointerOffsets.Count} pointer {(memory.PointerOffsets.Count == 1 ? "offset" : "offsets")}";
            var target = $"the {kind} memory value in {System.IO.Path.GetFileName(memory.ExecutablePath)} at {location}{chain}";
            if (condition.Trigger == WaitTrigger.Changes) return $"Wait until {target} changes from its starting value";
            var comparison = memory.Comparison switch
            {
                NumericComparison.NumericEquals => "equals", NumericComparison.NumericNotEquals => "does not equal",
                NumericComparison.Less => "is less than", NumericComparison.LessOrEqual => "is at most",
                NumericComparison.Greater => "is greater than", _ => "is at least"
            };
            return $"Wait until {target} {comparison} {memory.Expected}"
                + (memory.Tolerance == 0 ? "" : $" (tolerance {memory.Tolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture)})") + Transition(condition);
        }
        return Unsupported;
    }

    private const string Unsupported = "Unsupported wait condition — inspect exact input before playback.";
    private static string WindowName(WindowSelector? selector) => !string.IsNullOrWhiteSpace(selector?.Title) ? $"the “{Short(selector.Title)}” window"
        : !string.IsNullOrWhiteSpace(selector?.ExecutablePath) ? $"the {System.IO.Path.GetFileName(selector.ExecutablePath)} window"
        : !string.IsNullOrWhiteSpace(selector?.WindowClass) ? $"a {selector.WindowClass} window" : "the selected window";
    private static string Transition(WaitCondition condition) => condition.Trigger == WaitTrigger.BecomesTrue ? ", after first observing the opposite" : "";
    private static string TextSentence(WaitCondition condition, string target, TextPredicate? predicate)
    {
        if (predicate is null || !Enum.IsDefined(predicate.Comparison) || !Enum.IsDefined(predicate.Whitespace)) return Unsupported;
        if (condition.Trigger == WaitTrigger.Changes) return $"Wait until {target} changes from its starting text";
        var comparison = predicate.Comparison switch
        {
            TextComparison.TextEquals => "equals", TextComparison.TextContains => "contains",
            TextComparison.TextNotEquals => "does not equal", _ => "does not contain"
        };
        return $"Wait until {target} {comparison} “{Short(predicate.Expected)}”" + Transition(condition);
    }
    private static string Short(string text)
    {
        var singleLine = text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        return singleLine.Length > 100 ? singleLine[..100] + "…" : singleLine;
    }
}
