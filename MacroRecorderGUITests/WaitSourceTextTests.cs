using Google.Protobuf;
using MacroRecorderGUI.Editor;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitSourceTextTests
{
    internal static WaitCondition Accessibility() => new()
    {
        SemanticsVersion = 2, TimeoutUs = 30_000_000, StableForUs = 200_000, PollIntervalUs = 250_000,
        AccessibilityText = new() { Target = new() { Title = "Export" }, Element = new() { AutomationId = "status", ControlType = 50020 },
            Predicate = new() { Expected = "Ready", Comparison = TextComparison.TextContains } }
    };
    internal static WaitCondition Ocr() => new()
    {
        SemanticsVersion = 2, TimeoutUs = 30_000_000, StableForUs = 200_000, PollIntervalUs = 500_000,
        OcrText = new() { Region = new() { X = -100, Y = 50, Width = 80, Height = 40 }, Language = "eng",
            Predicate = new() { Expected = "Complete", Comparison = TextComparison.TextNotContains } }
    };
    internal static WaitCondition Memory() => new()
    {
        SemanticsVersion = 2, TimeoutUs = 30_000_000, StableForUs = 200_000, PollIntervalUs = 100_000,
        Memory = new() { ExecutablePath = @"C:\Apps\Example.exe", Module = new() { Path = @"C:\Apps\Example.dll", FileVersion = "1.2.3.4", Offset = 32 },
            ScalarType = MemoryScalarType.Uint64, Expected = "18446744073709551615", PointerOffsets = { 16, -8 } }
    };

    [TestMethod]
    public void WordingNamesDistinctSourcesTargetsAndExactValuesWithoutMutation()
    {
        var accessibility = Accessibility(); var ocr = Ocr(); var memory = Memory();
        foreach (var condition in new[] { accessibility, ocr, memory })
        {
            var bytes = condition.ToByteArray(); var words = WaitConditionText.Describe(condition);
            Assert.IsTrue(words.StartsWith("Wait until")); CollectionAssert.AreEqual(bytes, condition.ToByteArray());
        }
        StringAssert.Contains(WaitConditionText.Describe(accessibility), "accessible name");
        StringAssert.Contains(WaitConditionText.Describe(accessibility), "status");
        StringAssert.Contains(WaitConditionText.Describe(ocr), "OCR text (eng)");
        StringAssert.Contains(WaitConditionText.Describe(ocr), "80 × 40");
        StringAssert.Contains(WaitConditionText.Describe(ocr), "does not contain");
        StringAssert.Contains(WaitConditionText.Describe(memory), "unsigned 64-bit");
        StringAssert.Contains(WaitConditionText.Describe(memory), "Example.dll + 0x20");
        StringAssert.Contains(WaitConditionText.Describe(memory), "2 pointer offsets");
        StringAssert.Contains(WaitConditionText.Describe(memory), "18446744073709551615");
    }

    [TestMethod]
    public void ChangesDescribeRuntimeBaselinesAndTransitionsRequireOppositeObservation()
    {
        foreach (var condition in new[] { Accessibility(), Ocr(), Memory() })
        {
            condition.Trigger = WaitTrigger.Changes;
            StringAssert.Contains(WaitConditionText.Describe(condition), "changes from its starting");
            condition.Trigger = WaitTrigger.BecomesTrue;
            StringAssert.Contains(WaitConditionText.Describe(condition), "after first observing the opposite");
            condition.Trigger = WaitTrigger.NewWindow;
            StringAssert.Contains(WaitConditionText.Describe(condition), "Unsupported");
        }
    }

    [TestMethod]
    public void UnsupportedVersionsAndComparisonsDoNotGetDefaultDescriptions()
    {
        var condition = Accessibility(); condition.SemanticsVersion = 1;
        StringAssert.Contains(WaitConditionText.Describe(condition), "Unsupported");
        condition.SemanticsVersion = 2; condition.AccessibilityText.Predicate.Comparison = (TextComparison)99;
        StringAssert.Contains(WaitConditionText.Describe(condition), "Unsupported");
        condition = Memory(); condition.Memory.Comparison = (NumericComparison)99;
        StringAssert.Contains(WaitConditionText.Describe(condition), "Unsupported");
    }
}
