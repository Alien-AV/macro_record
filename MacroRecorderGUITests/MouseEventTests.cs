using Google.Protobuf;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public class MouseEventTests
{
    private static void Convert(params MouseEvent[] events)
    {
        using var macro = new MacroViewModel("conversion", new FakePlaybackEngine());
        foreach (var input in events) macro.AddEvent(input);
        macro.Editor.ConvertAnchoredEstimate();
    }
    [TestMethod]
    public void NewAbsoluteEventsUsePhysicalVirtualDesktopCoordinates()
    {
        var mouse = new MouseEvent(-1920, -1080, MouseActionTypeFlags.Move);
        Assert.IsFalse(mouse.RelativePosition);
        Assert.IsTrue(mouse.MappedToVirtualDesktop);
        Assert.AreEqual(-1920, mouse.X);
        Assert.AreEqual(-1080, mouse.Y);
    }

    [TestMethod]
    public void LegacyProtobufPayloadLoadsWithoutGuessingUnitsOrMapping()
    {
        // Existing fields only: delay=123, x=100, y=200, XDown, omitted payload/mapping.
        byte[] wire = [0x08, 0x7b, 0x1a, 0x08, 0x08, 0x64, 0x10, 0xc8, 0x01, 0x18, 0x80, 0x01];
        var mouse = new MouseEvent(ProtobufInputEvent.Parser.ParseFrom(wire));
        Assert.AreEqual(123ul, mouse.TimeSinceLastEvent);
        Assert.AreEqual(100, mouse.X);
        Assert.AreEqual(200, mouse.Y);
        Assert.AreEqual(MouseActionTypeFlags.XDown, mouse.ActionType);
        Assert.AreEqual(0u, mouse.MouseData);
        Assert.IsFalse(mouse.MappedToVirtualDesktop);
        CollectionAssert.AreEqual(wire, mouse.OriginalProtobufInputEvent.ToByteArray());
    }

    [TestMethod]
    [DataRow(-32768, MouseActionTypeFlags.Wheel)]
    [DataRow(-120, MouseActionTypeFlags.HorizontalWheel)]
    [DataRow(-1, MouseActionTypeFlags.Wheel)]
    [DataRow(30, MouseActionTypeFlags.HorizontalWheel)]
    [DataRow(120, MouseActionTypeFlags.Wheel)]
    [DataRow(1, MouseActionTypeFlags.XDown)]
    [DataRow(2, MouseActionTypeFlags.XUp)]
    [DataRow(3, MouseActionTypeFlags.XDown)]
    public void MousePayloadBitsRoundTripInUnchangedUint32Field(int payload, MouseActionTypeFlags action)
    {
        var mouse = new MouseEvent(0, 0, action) { MouseData = unchecked((uint)payload) };
        var parsed = new MouseEvent(ProtobufInputEvent.Parser.ParseFrom(mouse.OriginalProtobufInputEvent.ToByteArray()));
        Assert.AreEqual(unchecked((uint)payload), parsed.MouseData);
        Assert.AreEqual(unchecked((uint)payload), parsed.OriginalProtobufInputEvent.MouseEvent.WheelRotation);
        Assert.AreEqual(action, parsed.ActionType);
    }

    [TestMethod]
    public void NewMousePropertiesNotifyOnlyOnChange()
    {
        var mouse = new MouseEvent(0, 0, MouseActionTypeFlags.Wheel);
        var changed = new List<string?>();
        mouse.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        mouse.MouseData = unchecked((uint)-120);
        mouse.MouseData = unchecked((uint)-120);
        mouse.MappedToVirtualDesktop = false;
        mouse.MappedToVirtualDesktop = false;
        CollectionAssert.AreEqual(new[] { nameof(MouseEvent.MouseData), nameof(MouseEvent.MappedToVirtualDesktop) }, changed);
    }

    [TestMethod]
    public void WheelActionNamesRemainEditableAlongsideExistingFlags()
    {
        const MouseActionTypeFlags flags = MouseActionTypeFlags.Move | MouseActionTypeFlags.HorizontalWheel;
        Assert.AreEqual("Move, HorizontalWheel", MouseActionTypeConverter.ToString(flags));
        Assert.AreEqual(flags, MouseActionTypeConverter.FromString("Move, HorizontalWheel"));
        var mouse = new MouseEvent(0, 0, MouseActionTypeFlags.Move) { ActionName = "Wheel" };
        Assert.AreEqual(MouseActionTypeFlags.Wheel, mouse.ActionType);
    }

    [TestMethod]
    public void ConversionCrossesPrimaryBoundaryAndUsesVirtualDesktop()
    {
        var anchor = new MouseEvent(10, 20, MouseActionTypeFlags.Move) { MappedToVirtualDesktop = false };
        var relative = new MouseEvent(-30, -50, MouseActionTypeFlags.Move) { RelativePosition = true, TimeSinceLastEvent = 77 };
        Convert(anchor, relative);
        Assert.IsFalse(anchor.MappedToVirtualDesktop);
        Assert.IsTrue(relative.MappedToVirtualDesktop);
        Assert.IsFalse(relative.RelativePosition);
        Assert.AreEqual(-20, relative.X);
        Assert.AreEqual(-30, relative.Y);
        Assert.AreEqual(77ul, relative.TimeSinceLastEvent);
    }

    [TestMethod]
    public void ConversionDoesNotTreatButtonAndWheelPositionsAsAnchors()
    {
        var anchor = new MouseEvent(-1000, 500, MouseActionTypeFlags.Move);
        var wheel = new MouseEvent(0, 0, MouseActionTypeFlags.Wheel) { RelativePosition = true, MouseData = unchecked((uint)-120) };
        var button = new MouseEvent(999, 999, MouseActionTypeFlags.XDown) { MouseData = 2 };
        var relative = new MouseEvent(-5, 7, MouseActionTypeFlags.Move) { RelativePosition = true };
        var wheelBefore = wheel.OriginalProtobufInputEvent.ToByteArray();
        var buttonBefore = button.OriginalProtobufInputEvent.ToByteArray();
        Convert(anchor, wheel, button, relative);
        Assert.AreEqual(-1005, relative.X);
        Assert.AreEqual(507, relative.Y);
        CollectionAssert.AreEqual(wheelBefore, wheel.OriginalProtobufInputEvent.ToByteArray());
        CollectionAssert.AreEqual(buttonBefore, button.OriginalProtobufInputEvent.ToByteArray());
    }

    [TestMethod]
    public void ConversionLeavesUnanchoredRelativeEventsUntouched()
    {
        var relative = new MouseEvent(5, -5, MouseActionTypeFlags.Move) { RelativePosition = true };
        var button = new MouseEvent(100, 100, MouseActionTypeFlags.LeftDown);
        var before = relative.OriginalProtobufInputEvent.ToByteArray();
        Assert.ThrowsExactly<ArgumentException>(() => Convert(button, relative));
        CollectionAssert.AreEqual(before, relative.OriginalProtobufInputEvent.ToByteArray());
    }

    [TestMethod]
    public void ConversionRejectsOverflowWithoutClampingOrPartialChanges()
    {
        var anchor = new MouseEvent(int.MaxValue - 2, int.MinValue + 2, MouseActionTypeFlags.Move);
        var overflow = new MouseEvent(100, -100, MouseActionTypeFlags.Move) { RelativePosition = true };
        var back = new MouseEvent(-5, 7, MouseActionTypeFlags.Move) { RelativePosition = true };
        Assert.ThrowsExactly<ArgumentException>(() => Convert(anchor, overflow, back));
        Assert.AreEqual(100, overflow.X);
        Assert.AreEqual(-100, overflow.Y);
        Assert.AreEqual(-5, back.X);
        Assert.AreEqual(7, back.Y);
        Assert.IsTrue(overflow.RelativePosition);
        Assert.IsTrue(back.RelativePosition);
    }

    [TestMethod]
    public void ConversionResetsAtAbsoluteMovesAndRepeatedAttemptDoesNotChangeInput()
    {
        MouseEvent[] events = [
            new(10, 20, MouseActionTypeFlags.Move),
            new(1, 2, MouseActionTypeFlags.Move | MouseActionTypeFlags.LeftDown) { RelativePosition = true },
            new(-300, -400, MouseActionTypeFlags.Move),
            new(3, 4, MouseActionTypeFlags.Move) { RelativePosition = true }
        ];
        Convert(events);
        Assert.AreEqual(11, events[1].X);
        Assert.AreEqual(22, events[1].Y);
        Assert.AreEqual(-297, events[3].X);
        Assert.AreEqual(-396, events[3].Y);
        Assert.AreEqual(MouseActionTypeFlags.Move | MouseActionTypeFlags.LeftDown, events[1].ActionType);
        var before = events.Select(mouse => mouse.OriginalProtobufInputEvent.ToByteArray()).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => Convert(events));
        for (var index = 0; index < events.Length; ++index)
            CollectionAssert.AreEqual(before[index], events[index].OriginalProtobufInputEvent.ToByteArray());
    }
}
