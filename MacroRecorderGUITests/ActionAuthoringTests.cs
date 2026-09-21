using System.Numerics;
using System.Text.Json;
using Google.Protobuf;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class ActionAuthoringTests
{
    private static MacroViewModel Macro(params InputEvent[] inputs)
    {
        var macro = new MacroViewModel("authored actions", new FakePlaybackEngine());
        foreach (var input in inputs) macro.AddEvent(input);
        macro.Editor.Refresh();
        return macro;
    }

    private static MouseEvent Move(int x = 10) => new(x, -20, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 123 };
    private static KeyboardEvent Key(bool up = false) => new(VirtualKey.A, up) { TimeSinceLastEvent = 789 };
    private static RecordedAction Last(MacroViewModel macro)
    {
        macro.Editor.Refresh();
        return macro.Editor.Projection.Actions[^1];
    }

    [TestMethod]
    [DataRow(MouseButton.Left, MouseActionTypeFlags.LeftDown, MouseActionTypeFlags.LeftUp, 0u)]
    [DataRow(MouseButton.Right, MouseActionTypeFlags.RightDown, MouseActionTypeFlags.RightUp, 0u)]
    [DataRow(MouseButton.Middle, MouseActionTypeFlags.MiddleDown, MouseActionTypeFlags.MiddleUp, 0u)]
    [DataRow(MouseButton.X1, MouseActionTypeFlags.XDown, MouseActionTypeFlags.XUp, 1u)]
    [DataRow(MouseButton.X2, MouseActionTypeFlags.XDown, MouseActionTypeFlags.XUp, 2u)]
    public void ClickCreatesOneBalancedGestureWithoutMovingPointer(MouseButton button, MouseActionTypeFlags down, MouseActionTypeFlags up, uint data)
    {
        using var macro = Macro(Move());
        var inserted = macro.Editor.InsertClick(Last(macro), new(button, 123_456, 78_901));
        var events = inserted.Cast<MouseEvent>().ToArray();
        CollectionAssert.AreEqual(new[] { down, up }, events.Select(e => e.ActionType).ToArray());
        Assert.IsTrue(events.All(e => e.MouseData == data && e.RelativePosition && !e.MappedToVirtualDesktop && e.X == 0 && e.Y == 0));
        CollectionAssert.AreEqual(new ulong[] { 123_456, 78_901 }, inserted.Select(e => e.TimeSinceLastEvent).ToArray());
        var action = Last(macro);
        Assert.AreEqual(ActionKind.Click, action.Kind);
        Assert.IsTrue(action.Complete);
        Assert.AreEqual(new BigInteger(78_901), action.Duration);
        Assert.AreEqual(10d, macro.Editor.Projection.Samples[^1].Position!.Value.X);
        CollectionAssert.AreEqual(inserted.ToArray(), macro.SelectedEvents.ToArray());
        Assert.IsFalse(macro.Editor.RawSelection);
        WaitValidation.ValidateSchedule(macro.Events);
    }

    [TestMethod]
    public void EveryModifierCombinationHasExactBalancedOrderAndOneUndo()
    {
        for (var flags = 0; flags < 16; flags++)
        {
            using var macro = Macro();
            var inserted = macro.Editor.InsertShortcut(null, new((uint)VirtualKey.S, (ShortcutModifiers)flags, 111, 222));
            uint[] modifiers = [0x11, 0x10, 0x12, 0x5B];
            var expected = modifiers.Where((_, i) => (flags & (1 << i)) != 0).Append((uint)VirtualKey.S).ToArray();
            var keys = inserted.Cast<KeyboardEvent>().ToArray();
            CollectionAssert.AreEqual(expected.Concat(expected.Reverse()).ToArray(), keys.Select(e => e.VirtualKeyCode).ToArray());
            Assert.IsTrue(keys.Take(expected.Length).All(e => !e.KeyUp));
            Assert.IsTrue(keys.Skip(expected.Length).All(e => e.KeyUp));
            Assert.AreEqual(111ul, keys[0].TimeSinceLastEvent);
            Assert.AreEqual(222ul, keys[expected.Length].TimeSinceLastEvent);
            Assert.IsTrue(keys.Where((_, i) => i != 0 && i != expected.Length).All(e => e.TimeSinceLastEvent == 0));
            Assert.AreEqual(ActionKind.Keys, Last(macro).Kind);
            Assert.IsTrue(Last(macro).Complete);
            WaitValidation.ValidateSchedule(macro.Events);
            Assert.IsTrue(macro.Editor.Undo());
            Assert.IsEmpty(macro.Events);
            Assert.IsFalse(macro.Editor.Undo());
        }
    }

    [TestMethod]
    [DataRow(CoordinateSpace.AbsoluteDesktop, false, true)]
    [DataRow(CoordinateSpace.AbsolutePrimary, false, false)]
    [DataRow(CoordinateSpace.RelativeCounts, true, false)]
    public void PointerReportPreservesExplicitCoordinateFrameAndFullWireRanges(CoordinateSpace space, bool relative, bool desktop)
    {
        using var macro = Macro();
        var inserted = macro.Editor.InsertPointerMovement(null, new(int.MinValue, int.MaxValue, space, ulong.MaxValue));
        Assert.HasCount(1, inserted);
        var movement = (MouseEvent)inserted[0];
        Assert.AreEqual((int.MinValue, int.MaxValue), (movement.X, movement.Y));
        Assert.AreEqual(relative, movement.RelativePosition);
        Assert.AreEqual(desktop, movement.MappedToVirtualDesktop);
        Assert.AreEqual(MouseActionTypeFlags.Move, movement.ActionType);
        Assert.AreEqual(0u, movement.MouseData);
        Assert.AreEqual(ulong.MaxValue, movement.TimeSinceLastEvent);
        var action = Last(macro);
        Assert.AreEqual(space, macro.Editor.Projection.Samples[0].Position!.Value.Space);
        Assert.AreEqual(BigInteger.Zero, action.Duration);
        Assert.IsTrue(macro.Editor.Undo());
        Assert.IsEmpty(macro.Events);
    }

    [TestMethod]
    public void ZeroAndMaximumDelaysRemainExactWithoutOverflowOrScaling()
    {
        using var macro = Macro();
        macro.Editor.InsertClick(null, new(MouseButton.Left, ulong.MaxValue, ulong.MaxValue));
        Assert.AreEqual((BigInteger)ulong.MaxValue * 2, Last(macro).EndTime);
        macro.Editor.InsertShortcut(Last(macro), new((uint)VirtualKey.F12, ShortcutModifiers.Alt, ulong.MaxValue, ulong.MaxValue));
        Assert.AreEqual((BigInteger)ulong.MaxValue * 4, Last(macro).EndTime);
        macro.Editor.InsertClick(Last(macro), new(MouseButton.Right));
        Assert.AreEqual(0ul, Last(macro).Wait);
        Assert.AreEqual(BigInteger.Zero, Last(macro).Duration);
    }

    [TestMethod]
    [DataRow(0u)] [DataRow(1u)] [DataRow(7u)] [DataRow(0x10u)] [DataRow(0x11u)] [DataRow(0x12u)]
    [DataRow(0x1Au)] [DataRow(0x3Au)] [DataRow(0x5Bu)] [DataRow(0x5Cu)] [DataRow(0xA0u)] [DataRow(0xA5u)]
    [DataRow(0xC3u)] [DataRow(0xE7u)] [DataRow(255u)] [DataRow(256u)] [DataRow(uint.MaxValue)]
    public void UnsupportedShortcutKeysFailBeforeMutation(uint code)
    {
        using var macro = Macro(Move());
        var before = macro.SnapshotBytes();
        Assert.IsFalse(ActionAuthoring.IsSupportedShortcutKey(code));
        Assert.Throws<ArgumentException>(() => macro.Editor.InsertShortcut(Last(macro), new(code)));
        CollectionAssert.AreEqual(before, macro.SnapshotBytes());
        Assert.IsFalse(macro.Editor.CanUndo);
    }

    [TestMethod]
    [DataRow(0x08u)] [DataRow(0x0Du)] [DataRow(0x1Bu)] [DataRow(0x30u)] [DataRow(0x41u)] [DataRow(0x5Du)]
    [DataRow(0x60u)] [DataRow(0x70u)] [DataRow(0x87u)] [DataRow(0x90u)] [DataRow(0xA6u)] [DataRow(0xBAu)]
    [DataRow(0xDEu)] [DataRow(0xE2u)]
    public void SupportedVirtualKeysIncludeNavigationFunctionNumpadMediaAndOem(uint code)
    {
        using var macro = Macro();
        Assert.IsTrue(ActionAuthoring.IsSupportedShortcutKey(code));
        var inputs = macro.Editor.InsertShortcut(null, new(code)).Cast<KeyboardEvent>().ToArray();
        CollectionAssert.AreEqual(new[] { code, code }, inputs.Select(e => e.VirtualKeyCode).ToArray());
        Assert.IsTrue(Last(macro).Complete);
    }

    [TestMethod]
    public void InvalidDefinitionsDoNotMutateSelectionOrConsumeExistingUndo()
    {
        using var macro = Macro();
        var inserted = macro.Editor.InsertClick(null, new(MouseButton.Left));
        var before = macro.SnapshotBytes();
        Assert.Throws<ArgumentException>(() => macro.Editor.InsertClick(null, new((MouseButton)99)));
        Assert.Throws<ArgumentException>(() => macro.Editor.InsertShortcut(null, new(0x41, (ShortcutModifiers)16)));
        Assert.Throws<ArgumentException>(() => macro.Editor.InsertShortcut(null, new(0x41, (ShortcutModifiers)(-1))));
        Assert.Throws<ArgumentException>(() => macro.Editor.InsertPointerMovement(null, new(0, 0, CoordinateSpace.Unknown)));
        Assert.Throws<ArgumentException>(() => macro.Editor.InsertPointerMovement(null, new(0, 0, (CoordinateSpace)99)));
        Assert.Throws<ArgumentNullException>(() => macro.Editor.InsertClick(null, null!));
        Assert.Throws<ArgumentNullException>(() => macro.Editor.InsertShortcut(null, null!));
        Assert.Throws<ArgumentNullException>(() => macro.Editor.InsertPointerMovement(null, null!));
        CollectionAssert.AreEqual(before, macro.SnapshotBytes());
        CollectionAssert.AreEqual(inserted.ToArray(), macro.SelectedEvents.ToArray());
        Assert.IsTrue(macro.Editor.Undo());
        Assert.IsEmpty(macro.Events);
        Assert.IsFalse(macro.Editor.Undo());
    }

    [TestMethod]
    public void StaleForeignAndFabricatedAnchorsFailWithoutInserting()
    {
        using var macro = Macro(Move());
        using var other = Macro(Move());
        var stale = Last(macro);
        macro.Events[0].TimeSinceLastEvent++;
        var fake = new RecordedAction(0, macro.Events[0], 0);
        var before = macro.SnapshotBytes();
        foreach (var anchor in new[] { stale, Last(other), fake })
        {
            Assert.Throws<ArgumentException>(() => macro.Editor.InsertClick(anchor, new(MouseButton.Left)));
            Assert.Throws<ArgumentException>(() => macro.Editor.InsertShortcut(anchor, new(0x41)));
            Assert.Throws<ArgumentException>(() => macro.Editor.InsertPointerMovement(anchor, new(1, 2, CoordinateSpace.AbsoluteDesktop)));
        }
        CollectionAssert.AreEqual(before, macro.SnapshotBytes());
        Assert.IsFalse(macro.Editor.CanUndo);
    }

    [TestMethod]
    public void NullAnchorInsertsAtBeginningRegardlessOfExistingRawSelection()
    {
        using var macro = Macro(Key(), Key(true));
        var originals = macro.Events.ToArray();
        macro.Editor.SelectRawEvents([originals[1]]);
        var inserted = macro.Editor.InsertClick(null, new(MouseButton.Middle));
        CollectionAssert.AreEqual(inserted.Concat(originals).ToArray(), macro.Events.ToArray());
        Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(originals, macro.Events.ToArray());
        CollectionAssert.AreEqual(new[] { originals[1] }, macro.SelectedEvents.ToArray());
        Assert.IsTrue(macro.Editor.RawSelection);
    }

    [TestMethod]
    public void InsertionUsesEndOfWholeSelectedActionAndUndoRestoresActionSelection()
    {
        using var macro = Macro(Key(), Key(true), Move());
        var first = macro.Editor.Projection.Actions[0];
        var originals = macro.Events.ToArray();
        macro.Editor.SelectActions([first]);
        var inserted = macro.Editor.InsertShortcut(first, new(0x53, ShortcutModifiers.Control));
        CollectionAssert.AreEqual(originals.Take(2).Concat(inserted).Concat(originals.Skip(2)).ToArray(), macro.Events.ToArray());
        Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(originals, macro.Events.ToArray());
        CollectionAssert.AreEqual(originals.Take(2).ToArray(), macro.SelectedEvents.ToArray());
        Assert.IsFalse(macro.Editor.RawSelection);
        Assert.IsFalse(macro.Editor.Undo());
    }

    [TestMethod]
    public void HeldKeysAndEveryMouseButtonRejectAllSemanticInsertionsEvenAcrossOriginBoundary()
    {
        InputEvent[] downs = [Key(),
            new MouseEvent(0, 0, MouseActionTypeFlags.LeftDown), new MouseEvent(0, 0, MouseActionTypeFlags.RightDown),
            new MouseEvent(0, 0, MouseActionTypeFlags.MiddleDown),
            new MouseEvent(0, 0, MouseActionTypeFlags.XDown) { MouseData = 1 },
            new MouseEvent(0, 0, MouseActionTypeFlags.XDown) { MouseData = 2 },
            new MouseEvent(0, 0, MouseActionTypeFlags.XDown) { MouseData = 0 }];
        foreach (var down in downs)
        {
            using var macro = Macro(down);
            macro.AddCaptureOrigin(new(100, 200));
            macro.AddEvent(Move());
            var anchor = Last(macro);
            var before = macro.SnapshotBytes();
            Assert.Throws<ArgumentException>(() => macro.Editor.InsertClick(anchor, new(MouseButton.Left)));
            Assert.Throws<ArgumentException>(() => macro.Editor.InsertShortcut(anchor, new(0x41)));
            Assert.Throws<ArgumentException>(() => macro.Editor.InsertPointerMovement(anchor, new(1, 2, CoordinateSpace.RelativeCounts)));
            CollectionAssert.AreEqual(before, macro.SnapshotBytes());
            Assert.IsFalse(macro.Editor.CanUndo);
        }
    }

    [TestMethod]
    public void InsertionDoesNotRewriteUnknownFieldsOrWaitsAndReindexesOnlyLaterOrigins()
    {
        byte[] unknown = [0xA0, 0x06, 0x7B];
        var move = Move();
        move.OriginalProtobufInputEvent.MouseEvent.MergeFrom(unknown);
        move.OriginalProtobufInputEvent.MergeFrom(unknown);
        var condition = WaitValidation.NewWindow();
        condition.Window.Target.Title = "Never observed";
        condition.MergeFrom(unknown);
        var wait = new WaitConditionEvent(condition) { TimeSinceLastEvent = 87 };
        wait.OriginalProtobufInputEvent.MergeFrom(unknown);
        using var macro = Macro(move, Key(), Key(true), wait, Move(99));
        macro.RestoreOriginState(macro.OriginState with
        {
            IsExtended = true,
            Origins = [new(0, new(-20, 40), 17), new(1, new(200, 300), 18), new(4, new(-90, 70), 19)],
            Extensions = new() { ["future"] = JsonSerializer.SerializeToElement(new { Value = 123 }) },
            BeforeOriginAdoption = [.. new ProtobufInputEventList().ToByteArray()]
        });
        macro.Editor.Refresh();
        var originals = macro.Events.ToArray();
        var bytes = originals.Select(e => e.OriginalProtobufInputEvent.ToByteArray()).ToArray();
        var before = macro.SnapshotBytes();
        var originState = macro.OriginState;
        var inserted = macro.Editor.InsertShortcut(macro.Editor.Projection.Actions[0], new(0x53, ShortcutModifiers.Control));
        CollectionAssert.AreEqual(new[] { 0, 1, 8 }, macro.PointerOrigins.Select(o => o.EventIndex).ToArray());
        Assert.AreSame(originState.Extensions, macro.OriginState.Extensions);
        Assert.AreSame(originState.BeforeOriginAdoption, macro.OriginState.BeforeOriginAdoption);
        CollectionAssert.AreEqual(originals.Take(1).Concat(inserted).Concat(originals.Skip(1)).ToArray(), macro.Events.ToArray());
        for (var i = 0; i < originals.Length; i++) CollectionAssert.AreEqual(bytes[i], originals[i].OriginalProtobufInputEvent.ToByteArray());
        WaitValidation.ValidateSchedule(macro.Events, loop: true);
        Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(before, macro.SnapshotBytes());
    }

    [TestMethod]
    public void InsertionAfterWaitPreservesItsConditionAndFixedDelay()
    {
        using var macro = Macro();
        var condition = WaitValidation.NewWindow();
        condition.Window.Target.Title = "Never observed";
        var wait = macro.Editor.InsertWait(null, condition);
        wait.TimeSinceLastEvent = 500;
        var before = wait.OriginalProtobufInputEvent.ToByteArray();
        macro.Editor.InsertClick(Last(macro), new(MouseButton.X2));
        CollectionAssert.AreEqual(before, wait.OriginalProtobufInputEvent.ToByteArray());
        Assert.AreSame(wait, macro.Events[0]);
        WaitValidation.ValidateSchedule(macro.Events, loop: true);
        Assert.IsTrue(macro.Editor.Undo());
        Assert.HasCount(1, macro.Events);
        Assert.IsFalse(macro.Editor.CanUndo);
    }

    [TestMethod]
    public void UndoRetainsSubsequentlyAppendedInputAndCaptureOrigin()
    {
        using var macro = Macro(Move());
        var original = macro.Events[0];
        macro.Editor.InsertClick(Last(macro), new(MouseButton.Right));
        macro.AddCaptureOrigin(new(-500, 300));
        var appended = Move(500);
        macro.AddEvent(appended);
        Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(new[] { original, appended }, macro.Events.ToArray());
        Assert.AreEqual(1, macro.PointerOrigins.Single().EventIndex);
        Assert.AreEqual(-500, macro.PointerOrigins.Single().Position!.X);
    }

    [TestMethod]
    public void DisposedEditorCannotAuthor()
    {
        using var macro = Macro();
        macro.Editor.Dispose();
        Assert.Throws<ObjectDisposedException>(() => macro.Editor.InsertClick(null, new(MouseButton.Left)));
        Assert.Throws<ObjectDisposedException>(() => macro.Editor.InsertShortcut(null, new(0x41)));
        Assert.Throws<ObjectDisposedException>(() => macro.Editor.InsertPointerMovement(null, new(0, 0, CoordinateSpace.AbsoluteDesktop)));
        Assert.IsEmpty(macro.Events);
    }
}
