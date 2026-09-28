using System.Text.Json;
using Google.Protobuf;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Utils;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class ExtendedWaitFormatTests
{
    private static IEnumerable<WaitCondition> Conditions()
    {
        yield return MemoryWaitBackendTests.Condition();
        var text = WaitValidation.NewAccessibilityText(); text.AccessibilityText.Target.Title = "fake"; text.AccessibilityText.Element.AutomationId = "status"; yield return text;
        yield return WaitValidation.NewOcrText();
    }

    [TestMethod]
    public void EveryNewSourceRequiresVersionFourWhilePreservingNestedUnknownFields()
    {
        foreach (var condition in Conditions())
        {
            if (condition.Memory is { } memory) condition.Memory = MemoryCondition.Parser.ParseFrom([.. memory.ToByteArray(), 0xa0, 6, 7]);
            if (condition.AccessibilityText is { } text) condition.AccessibilityText = AccessibilityTextCondition.Parser.ParseFrom([.. text.ToByteArray(), 0xa0, 6, 7]);
            if (condition.OcrText is { } ocr) condition.OcrText = OcrTextCondition.Parser.ParseFrom([.. ocr.ToByteArray(), 0xa0, 6, 7]);
            var input = new WaitConditionEvent(WaitCondition.Parser.ParseFrom([.. condition.ToByteArray(), 0xa0, 6, 8]));
            var list = new ProtobufInputEventList(); list.InputEvents.Add(input.OriginalProtobufInputEvent);
            var bytes = list.ToByteArray(); var document = new RecordingDocument { Events = bytes };
            var saved = document.Write();
            Assert.AreEqual(4, RecordingDocument.Read(saved).Version);
            CollectionAssert.AreEqual(bytes, RecordingDocument.Read(saved).Events);
            Assert.Throws<InvalidDataException>(() => RecordingDocument.Read(bytes));
            foreach (var version in new[] { 2, 3 })
            {
                byte[] older = [.. "\0MACRO2\n"u8, .. JsonSerializer.SerializeToUtf8Bytes(new { Version = version, Events = bytes, Origins = Array.Empty<object>() })];
                Assert.Throws<InvalidDataException>(() => RecordingDocument.Read(older));
            }
            Assert.Throws<InvalidOperationException>(() => document.ExportLegacy());
        }
    }

    [TestMethod]
    public void UnknownWaitAndUnknownEventCannotBeSilentlyDropped()
    {
        var condition = WaitValidation.NewMemory(); condition.ClearCondition();
        condition = WaitCondition.Parser.ParseFrom([.. condition.ToByteArray(), 0x5a, 0]); // future source field 11
        var list = new ProtobufInputEventList(); list.InputEvents.Add(new ProtobufInputEvent { WaitCondition = condition });
        Assert.Throws<InvalidDataException>(() => new RecordingDocument { Events = list.ToByteArray() }.Write());
        list.InputEvents.Clear(); list.InputEvents.Add(ProtobufInputEvent.Parser.ParseFrom(new byte[] { 0x32, 0 })); // future event field 6
        Assert.Throws<InvalidDataException>(() => RecordingDocument.Read(list.ToByteArray()));
    }

    [TestMethod]
    public void ProviderBoundsAndLocalOptInAreNotImportable()
    {
        var settings = new WaitLocalSettings(); Assert.IsFalse(settings.MemoryEnabled);
        foreach (var condition in Conditions())
        {
            WaitValidation.Validate(condition);
            var invalid = condition.Clone(); invalid.PollIntervalUs = 10_000;
            Assert.Throws<ArgumentException>(() => WaitValidation.Validate(invalid));
            invalid = condition.Clone(); invalid.Trigger = WaitTrigger.NewWindow;
            Assert.Throws<ArgumentException>(() => WaitValidation.Validate(invalid));
        }
        var memory = MemoryWaitBackendTests.Condition();
        memory.Memory.PointerOffsets.AddRange(Enumerable.Repeat(0L, 17));
        Assert.Throws<ArgumentException>(() => WaitValidation.Validate(memory));
        memory.Memory.PointerOffsets.Clear(); memory.Memory.Expected = "18446744073709551616";
        Assert.Throws<ArgumentException>(() => WaitValidation.Validate(memory));
        Assert.IsFalse(settings.MemoryEnabled);
    }
}
