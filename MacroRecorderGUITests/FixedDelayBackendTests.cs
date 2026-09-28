using Google.Protobuf;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using ProtobufGenerated;
using System.Text.Json;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class FixedDelayBackendTests
{
    [TestMethod]
    public void DelayPreservesExactDurationPreDelayAndUnknownFields()
    {
        var wire = new ProtobufInputEvent { TimeSinceLastEvent = 123, Delay = new() { DurationMicroseconds = 86_400_000_000 } };
        wire.Delay = FixedDelay.Parser.ParseFrom([.. wire.Delay.ToByteArray(), 0xa0, 0x06, 9]);
        wire = ProtobufInputEvent.Parser.ParseFrom([.. wire.ToByteArray(), 0xa0, 0x06, 7]);
        var delay = (DelayEvent)InputEvent.CreateInputEvent(wire.Clone());
        Assert.AreEqual(86_400_000_000UL, delay.DurationMicroseconds);
        Assert.AreEqual(123UL, delay.TimeSinceLastEvent);
        CollectionAssert.AreEqual(wire.ToByteArray(), delay.OriginalProtobufInputEvent.ToByteArray());
        WaitValidation.ValidateSchedule([new KeyboardEvent(new ProtobufInputEvent { KeyboardEvent = new() { VirtualKeyCode = 65 } }), delay]);
    }

    [TestMethod]
    public void DelayRequiresVersionFourAndCannotBeExportedAsLegacy()
    {
        var wire = new ProtobufInputEventList();
        wire.InputEvents.Add(new DelayEvent(123).OriginalProtobufInputEvent);
        var document = new RecordingDocument { Events = wire.ToByteArray() };
        var bytes = document.Write();
        Assert.AreEqual(4, RecordingDocument.Read(bytes).Version);
        CollectionAssert.AreEqual(wire.ToByteArray(), RecordingDocument.Read(bytes).Events);
        Assert.Throws<InvalidOperationException>(() => document.ExportLegacy());
        Assert.Throws<InvalidDataException>(() => RecordingDocument.Read(wire.ToByteArray()));
        foreach (var version in new[] { 2, 3 })
        {
            byte[] old = [.. "\0MACRO2\n"u8, .. JsonSerializer.SerializeToUtf8Bytes(new { Version = version, Events = wire.ToByteArray(), Origins = Array.Empty<object>() })];
            Assert.Throws<InvalidDataException>(() => RecordingDocument.Read(old));
        }
    }

    [TestMethod]
    public void InvalidDelayAndIncompleteWaitSourceFailClosed()
    {
        Assert.AreEqual(0UL, new DelayEvent(0).DurationMicroseconds);
        Assert.Throws<ArgumentException>(() => new DelayEvent(DelayEvent.MaximumDurationMicroseconds + 1));
        var wait = WaitValidation.NewWindow(); wait.SemanticsVersion = 2; wait.Memory = new();
        Assert.Throws<ArgumentException>(() => WaitValidation.Validate(wait));
    }

    [TestMethod]
    public async Task ScalingCanRoundExplicitDelayToZeroAndRejectsOverflowBeforeStarting()
    {
        var engine = new FakePlaybackEngine(); var workflow = new PlaybackWorkflow(engine);
        var delay = new DelayEvent(1) { TimeSinceLastEvent = 1 };
        await workflow.PlayAsync([delay], new() { Countdown = TimeSpan.Zero, Speed = 10 });
        var played = (DelayEvent)engine.PlayedEvents.Single();
        Assert.AreEqual(0UL, played.DurationMicroseconds); Assert.AreEqual(0UL, played.TimeSinceLastEvent);
        Assert.AreEqual(1UL, delay.DurationMicroseconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => workflow.PlayAsync([new DelayEvent(DelayEvent.MaximumDurationMicroseconds)], new() { Countdown = TimeSpan.Zero, Speed = .1 }));
        Assert.AreEqual(1, engine.Starts);
    }
}
