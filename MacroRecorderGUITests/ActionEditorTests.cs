using System.Numerics;
using Google.Protobuf;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;
using Windows.System;
using static MacroRecorderGUITests.HotkeyChordTests;

namespace MacroRecorderGUITests;

[TestClass]
public class ActionEditorTests
{
    private static MouseEvent Move(int x, int y = 0, ulong delay = 10, bool relative = false, bool desktop = true)
        => new(x, y, MouseActionTypeFlags.Move) { TimeSinceLastEvent = delay, RelativePosition = relative, MappedToVirtualDesktop = desktop };
    private static MouseEvent Button(MouseActionTypeFlags flags, ulong delay = 10, uint data = 0)
        => new(999, -999, flags) { TimeSinceLastEvent = delay, MouseData = data, RelativePosition = true };
    private static KeyboardEvent K(VirtualKey code, bool up = false, ulong delay = 10)
        => new(code, up) { TimeSinceLastEvent = delay };
    private static MacroViewModel Macro(params InputEvent[] events)
    {
        var macro = new MacroViewModel("editor test", new FakePlaybackEngine());
        foreach (var input in events) macro.AddEvent(input);
        macro.Editor.Refresh();
        return macro;
    }
    private static RecordedAction A(MacroViewModel macro, int index = 0) { macro.Editor.Refresh(); return macro.Editor.Projection.Actions[index]; }
    private static byte[][] Bytes(MacroViewModel macro) => macro.Events.Select(e => e.OriginalProtobufInputEvent.ToByteArray()).ToArray();
    private static void SameBytes(byte[][] expected, MacroViewModel actual)
    {
        Assert.AreEqual(expected.Length, actual.Events.Count);
        for (var i = 0; i < expected.Length; i++) CollectionAssert.AreEqual(expected[i], actual.Events[i].OriginalProtobufInputEvent.ToByteArray());
    }
    private static BigInteger Sum(IEnumerable<InputEvent> events) => events.Aggregate(BigInteger.Zero, (sum, e) => sum + e.TimeSinceLastEvent);

    [TestMethod]
    public void ProjectionPartitionsEveryRawEventAndPreservesWireBytes()
    {
        using var macro = Macro(Move(-20, 10, 123), Move(30), Button(MouseActionTypeFlags.LeftDown), Button(MouseActionTypeFlags.LeftUp),
            Move(40), Button(MouseActionTypeFlags.LeftDown), Move(60), Button(MouseActionTypeFlags.LeftUp),
            Button(MouseActionTypeFlags.Wheel, data: unchecked((uint)-120)), Button(MouseActionTypeFlags.Wheel, data: unchecked((uint)-120)),
            K(VirtualKey.Control), K(VirtualKey.S), K(VirtualKey.S, true), K(VirtualKey.Control, true));
        var bytes = Bytes(macro); var originals = macro.Events.ToArray();
        var actions = macro.Editor.Projection.Actions;
        CollectionAssert.AreEqual(new[] { ActionKind.Move, ActionKind.Click, ActionKind.Move, ActionKind.Drag, ActionKind.Scroll, ActionKind.Keys }, actions.Select(a => a.Kind).ToArray());
        CollectionAssert.AreEqual(originals, actions.SelectMany(macro.Editor.EventsFor).ToArray());
        Assert.AreEqual(Sum(macro.Events), actions.Aggregate(BigInteger.Zero, (sum, a) => sum + a.Wait + a.Duration));
        Assert.AreEqual(Sum(macro.Events), macro.Editor.Projection.TotalTime);
        StringAssert.Contains(actions[^2].Detail, "-240 total");
        StringAssert.Contains(actions[^1].Detail, "Ctrl + S");
        macro.Editor.SelectActions(actions); macro.Editor.Projection.SampleAt(300);
        PathDisplay.Decimate(macro.Editor.Projection.Samples, 0, macro.Events.Count);
        SameBytes(bytes, macro);
    }

    [TestMethod]
    public void PauseFrameAndWheelDirectionAreSemanticBoundaries()
    {
        using var macro = Macro(Move(1), Move(2), Move(3, delay: ActionProjection.MovementPause), Move(4, relative: true), Move(5, desktop: false),
            Button(MouseActionTypeFlags.Wheel, data: 120), Button(MouseActionTypeFlags.Wheel, data: unchecked((uint)-120)),
            Button(MouseActionTypeFlags.HorizontalWheel, data: unchecked((uint)-120)));
        CollectionAssert.AreEqual(new[] { 2, 1, 1, 1, 1, 1, 1 }, macro.Editor.Projection.Actions.Select(a => a.Count).ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ModifierMouseSequenceIsCompactTruthfulAndLossless(bool drag)
    {
        var inputs = new List<InputEvent> { K(VirtualKey.Shift) };
        if (drag) inputs.Add(Button(MouseActionTypeFlags.LeftDown));
        inputs.AddRange(Enumerable.Range(0, 500).Select(i => Move(i, delay: 1)));
        if (drag) inputs.Add(Button(MouseActionTypeFlags.LeftUp));
        inputs.Add(K(VirtualKey.Shift, true)); inputs.Add(Move(800));
        using var macro = Macro(inputs.ToArray()); var bytes = Bytes(macro);
        Assert.HasCount(2, macro.Editor.Projection.Actions);
        Assert.AreEqual(ActionKind.Sequence, A(macro).Kind);
        Assert.IsTrue(A(macro).Complete);
        Assert.AreEqual(inputs.Count - 1, A(macro).Count);
        Assert.IsNotNull(macro.Editor.GeometryBlockReason(A(macro)));
        SameBytes(bytes, macro);
    }

    [TestMethod]
    public void MissingModifierReleaseKeepsOneIncompleteSequence()
    {
        using var macro = Macro(K(VirtualKey.Control));
        for (var i = 0; i < 500; i++) macro.AddEvent(Move(i, relative: true));
        macro.Editor.Refresh();
        Assert.HasCount(1, macro.Editor.Projection.Actions);
        Assert.AreEqual(ActionKind.Sequence, A(macro).Kind); Assert.IsFalse(A(macro).Complete);
    }

    [TestMethod]
    public void ChordsRepeatsAndOrphanReleasesDoNotInventTextOrClicks()
    {
        using var macro = Macro(K(VirtualKey.Control), K(VirtualKey.S), K(VirtualKey.S), K(VirtualKey.S, true), K(VirtualKey.Control, true),
            K(VirtualKey.A, true), Button(MouseActionTypeFlags.LeftUp), K((VirtualKey)0xFEDC), K((VirtualKey)0xFEDC, true));
        Assert.HasCount(4, macro.Editor.Projection.Actions);
        Assert.AreEqual(ActionKind.Keys, A(macro).Kind); Assert.AreEqual(5, A(macro).Count);
        Assert.AreEqual(ActionKind.Raw, A(macro, 1).Kind); Assert.AreEqual(ActionKind.Raw, A(macro, 2).Kind);
        StringAssert.Contains(A(macro, 3).Detail, "VK 0xFEDC");
    }

    [TestMethod]
    public void OverlappingButtonsAreOneMixedSequenceRatherThanFalseClicks()
    {
        using var macro = Macro(Button(MouseActionTypeFlags.LeftDown), Button(MouseActionTypeFlags.RightDown), Move(10),
            Button(MouseActionTypeFlags.LeftUp), Button(MouseActionTypeFlags.RightUp), Move(20));
        Assert.AreEqual(ActionKind.Sequence, A(macro).Kind); Assert.AreEqual(5, A(macro).Count);
        Assert.AreEqual(ActionKind.Move, A(macro, 1).Kind);
    }

    [TestMethod]
    public void DifferentXButtonsCannotCloseEachOthersPress()
    {
        using var macro = Macro(Button(MouseActionTypeFlags.XDown, data: 1), Button(MouseActionTypeFlags.XUp, data: 2), Move(10));
        Assert.AreEqual(ActionKind.Sequence, A(macro).Kind); Assert.IsFalse(A(macro).Complete);
        macro.AddEvent(Button(MouseActionTypeFlags.XUp, data: 1));
        Assert.IsFalse(A(macro).Complete, "Releasing the tracked button cannot erase an earlier unmatched release.");
    }

    [TestMethod]
    public void CompoundMouseFlagsAreRetainedWithoutInventingSimpleGestures()
    {
        using var macro = Macro(Button(MouseActionTypeFlags.Move | MouseActionTypeFlags.LeftDown), Move(20), Button(MouseActionTypeFlags.LeftUp));
        Assert.AreEqual(ActionKind.Sequence, A(macro).Kind);
        Assert.AreEqual(3, A(macro).Count);
    }

    [TestMethod]
    public void RelativeCountsNeverUseAnAbsoluteAnchorAsTheirDisplayOrigin()
    {
        using var macro = Macro(Move(1000, 2000), Move(5, -7, relative: true), Move(3, 2, relative: true));
        var samples = macro.Editor.Projection.Samples;
        Assert.AreEqual(new PathPosition(5, -7, CoordinateSpace.RelativeCounts), samples[1].Position);
        Assert.AreEqual(new PathPosition(8, -5, CoordinateSpace.RelativeCounts), samples[2].Position);
        Assert.IsTrue(samples[1].StartsSegment);
        StringAssert.Contains(macro.Editor.GeometryBlockReason(A(macro))!, "device counts");
        Assert.IsTrue(((MouseEvent)macro.Events[1]).RelativePosition);
    }

    [TestMethod]
    public void NonMovementPayloadCoordinatesNeverCreateOrChangeAnAnchor()
    {
        using var macro = Macro(Button(MouseActionTypeFlags.LeftDown), Button(MouseActionTypeFlags.LeftUp), Move(-80, 60),
            Button(MouseActionTypeFlags.Wheel, data: 120));
        Assert.IsNull(macro.Editor.Projection.Samples[0].Position);
        Assert.IsNull(macro.Editor.Projection.Samples[1].Position);
        Assert.AreEqual(new PathPosition(-80, 60, CoordinateSpace.AbsoluteDesktop), macro.Editor.Projection.Samples[3].Position);
    }

    [TestMethod]
    public void MixedFramesAndIncompleteInputBlockGeometry()
    {
        using var frames = Macro(Move(10), Move(20, desktop: false));
        StringAssert.Contains(frames.Editor.GeometryBlockReason(A(frames))!, "coordinate frames");
        using var incomplete = Macro(Move(10), K(VirtualKey.Shift));
        StringAssert.Contains(incomplete.Editor.GeometryBlockReason(A(incomplete))!, "Incomplete");
    }

    [TestMethod]
    public void WaitEditNeverTouchesInternalTiming()
    {
        using var macro = Macro(Move(1, delay: 20), Move(2, delay: 7), Move(3, delay: 13));
        var action = A(macro);
        macro.Editor.SetWait(action, 987654321);
        CollectionAssert.AreEqual(new ulong[] { 987654321, 7, 13 }, macro.Events.Select(e => e.TimeSinceLastEvent).ToArray());
        Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(new ulong[] { 20, 7, 13 }, macro.Events.Select(e => e.TimeSinceLastEvent).ToArray());
    }

    [TestMethod]
    public void DurationScalingPreservesExactIntegerTotalWaitOrderAndCount()
    {
        using var macro = Macro(Move(1, delay: 50), Move(2, delay: 1), Move(3, delay: 2), Move(4, delay: 3));
        var events = macro.Events.ToArray();
        macro.Editor.SetDuration(A(macro), 11);
        Assert.AreEqual(50ul, macro.Events[0].TimeSinceLastEvent);
        Assert.AreEqual(new BigInteger(11), Sum(macro.Events.Skip(1)));
        CollectionAssert.AreEqual(new ulong[] { 50, 1, 4, 6 }, macro.Events.Select(e => e.TimeSinceLastEvent).ToArray());
        CollectionAssert.AreEqual(events, macro.Events.ToArray());
    }

    [TestMethod]
    public void ZeroDurationCanExpandOrCollapseWithoutLosingSamples()
    {
        using var macro = Macro(Move(1, delay: 123), Move(2, delay: 0), Move(3, delay: 0), Move(4, delay: 0));
        macro.Editor.SetDuration(A(macro), 2);
        CollectionAssert.AreEqual(new ulong[] { 123, 0, 1, 1 }, macro.Events.Select(e => e.TimeSinceLastEvent).ToArray());
        macro.Editor.SetDuration(A(macro), 0);
        CollectionAssert.AreEqual(new ulong[] { 123, 0, 0, 0 }, macro.Events.Select(e => e.TimeSinceLastEvent).ToArray());
    }

    [TestMethod]
    public void SingleEventCannotAcquireAnInternalDuration()
    {
        using var macro = Macro(Move(1));
        Assert.ThrowsExactly<ArgumentException>(() => macro.Editor.SetDuration(A(macro), 1));
        macro.Editor.SetDuration(A(macro), 0);
        Assert.IsFalse(macro.Editor.CanUndo);
    }

    [TestMethod]
    public void HugeChordDurationsAndTotalsDoNotOverflow()
    {
        using var macro = Macro(K(VirtualKey.Control, delay: ulong.MaxValue), K(VirtualKey.S, delay: ulong.MaxValue), K(VirtualKey.S, true, ulong.MaxValue), K(VirtualKey.Control, true, ulong.MaxValue));
        Assert.AreEqual((BigInteger)ulong.MaxValue * 4, macro.Editor.Projection.TotalTime);
        macro.Editor.SetDuration(A(macro), (BigInteger)ulong.MaxValue * 3 - 1);
        Assert.AreEqual((BigInteger)ulong.MaxValue * 3 - 1, Sum(macro.Events.Skip(1)));
        Assert.AreEqual(ulong.MaxValue, macro.Events[0].TimeSinceLastEvent);
    }

    [TestMethod]
    public void OverflowingScaleIsRejectedAtomically()
    {
        using var macro = Macro(K(VirtualKey.Control), K(VirtualKey.S, delay: 1), K(VirtualKey.S, true, 0), K(VirtualKey.Control, true, 0));
        var before = Bytes(macro);
        Assert.ThrowsExactly<ArgumentException>(() => macro.Editor.SetDuration(A(macro), (BigInteger)ulong.MaxValue + 1));
        SameBytes(before, macro); Assert.IsFalse(macro.Editor.CanUndo);
    }

    [TestMethod]
    public void DestinationEditKeepsSubsequentTargetAndInheritedClickConnected()
    {
        using var macro = Macro(Move(0, 0), Move(50, 50), Move(100, 100), Button(MouseActionTypeFlags.LeftDown), Button(MouseActionTypeFlags.LeftUp),
            Move(120, 110), Move(150, 150), Move(200, 200));
        var before = Bytes(macro); var times = macro.Events.Select(e => e.TimeSinceLastEvent).ToArray();
        macro.Editor.SetDestination(A(macro), 200, 0); macro.Editor.Refresh();
        Assert.AreEqual((200, 0), (((MouseEvent)macro.Events[2]).X, ((MouseEvent)macro.Events[2]).Y));
        Assert.AreEqual(new PathPosition(200, 0, CoordinateSpace.AbsoluteDesktop), macro.Editor.Projection.Samples[3].Position);
        Assert.AreEqual((200, 200), (((MouseEvent)macro.Events[^1]).X, ((MouseEvent)macro.Events[^1]).Y));
        CollectionAssert.AreEqual(times, macro.Events.Select(e => e.TimeSinceLastEvent).ToArray());
        Assert.IsTrue(macro.Editor.Undo()); SameBytes(before, macro);
    }

    [TestMethod]
    public void DragDestinationEditsPreserveDownUpAndEverySample()
    {
        using var macro = Macro(Move(0), Button(MouseActionTypeFlags.LeftDown), Move(10), Move(20), Button(MouseActionTypeFlags.LeftUp));
        var order = macro.Events.ToArray();
        macro.Editor.SetDestination(A(macro, 1), 40, -10);
        Assert.AreEqual(40, ((MouseEvent)macro.Events[3]).X);
        Assert.AreEqual(MouseActionTypeFlags.LeftDown, ((MouseEvent)macro.Events[1]).ActionType);
        Assert.AreEqual(MouseActionTypeFlags.LeftUp, ((MouseEvent)macro.Events[^1]).ActionType);
        CollectionAssert.AreEqual(order, macro.Events.ToArray());
    }

    [TestMethod]
    public void GeometryOverflowDoesNotPartiallyChangeThePath()
    {
        using var macro = Macro(Move(0), Move(int.MaxValue), Move(0)); var before = Bytes(macro);
        Assert.ThrowsExactly<ArgumentException>(() => macro.Editor.SetDestination(A(macro), int.MaxValue, 0));
        SameBytes(before, macro);
    }

    [TestMethod]
    public void ExplicitConversionSkipsUnanchoredCountsAndIsUndoable()
    {
        using var macro = Macro(Move(7, relative: true), Move(100, 200), Move(5, -7, relative: true)); var before = Bytes(macro);
        macro.Editor.ConvertAnchoredEstimate();
        Assert.IsTrue(((MouseEvent)macro.Events[0]).RelativePosition);
        Assert.IsFalse(((MouseEvent)macro.Events[2]).RelativePosition);
        Assert.AreEqual((105, 193), (((MouseEvent)macro.Events[2]).X, ((MouseEvent)macro.Events[2]).Y));
        Assert.IsNotNull(macro.Editor.GeometryBlockReason(A(macro, 1)));
        Assert.IsTrue(macro.Editor.Undo()); SameBytes(before, macro);
    }

    [TestMethod]
    public void UnanchoredOrOverflowingConversionIsRejected()
    {
        using var unanchored = Macro(Move(10, relative: true));
        Assert.ThrowsExactly<ArgumentException>(unanchored.Editor.ConvertAnchoredEstimate);
        using var overflowing = Macro(Move(int.MaxValue), Move(1, relative: true)); var before = Bytes(overflowing);
        Assert.ThrowsExactly<ArgumentException>(overflowing.Editor.ConvertAnchoredEstimate); SameBytes(before, overflowing);
    }

    [TestMethod]
    public void UndoAfterCaptureAppendKeepsTheLaterInputAndIdentity()
    {
        using var macro = Macro(Move(1), Move(2)); var original = macro.Events.ToArray();
        macro.Editor.SetWait(A(macro), 123);
        var captured = K(VirtualKey.Escape); macro.AddEvent(captured);
        Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(original.Concat([captured]).ToArray(), macro.Events.ToArray());
        Assert.AreEqual(10ul, macro.Events[0].TimeSinceLastEvent);
    }

    [TestMethod]
    public void UndoDeleteRestoresSelectionAndRetainsCaptureTail()
    {
        using var macro = Macro(Move(1), Move(2), K(VirtualKey.A), K(VirtualKey.A, true));
        var deleted = macro.Events.Take(2).ToArray();
        macro.Editor.SelectActions([A(macro)]); macro.RemoveSelectedEvents();
        var capture = Move(800); macro.AddEvent(capture);
        Assert.IsTrue(macro.Editor.Undo());
        Assert.HasCount(5, macro.Events); Assert.AreSame(capture, macro.Events[^1]);
        CollectionAssert.AreEqual(deleted, macro.SelectedEvents.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExternalClearOrLoadInvalidatesUndoAndNeverResurrectsStaleContents(bool load)
    {
        using var macro = Macro(Move(1), Move(2)); macro.Editor.SetWait(A(macro), 99);
        if (load) macro.PopulateEventCollectionWithNewEvents([Move(900)]); else macro.Clear();
        Assert.IsFalse(macro.Editor.CanUndo); Assert.IsFalse(macro.Editor.Undo());
        Assert.AreEqual(load ? 1 : 0, macro.Events.Count);
    }

    [TestMethod]
    public void ExternalDelayChangeInvalidatesUndoInsteadOfOverwritingAutoDelay()
    {
        using var macro = Macro(Move(1), Move(2)); macro.Editor.SetWait(A(macro), 99);
        macro.Events[0].TimeSinceLastEvent = 5000;
        Assert.IsFalse(macro.Editor.Undo()); Assert.AreEqual(5000ul, macro.Events[0].TimeSinceLastEvent);
    }

    [TestMethod]
    public void DirectWireMutationFailsUndoPreconditions()
    {
        using var macro = Macro(Move(1)); macro.Editor.SetWait(A(macro), 99);
        macro.Events[0].OriginalProtobufInputEvent.TimeSinceLastEvent = 500;
        Assert.IsFalse(macro.Editor.Undo()); Assert.AreEqual(500ul, macro.Events[0].TimeSinceLastEvent);
    }

    [TestMethod]
    public void MultipleUndoCommandsComposeAcrossCaptureAppends()
    {
        using var macro = Macro(Move(1)); macro.Editor.SetWait(A(macro), 99);
        var tail = K(VirtualKey.A); macro.AddEvent(tail);
        macro.Editor.SetWait(A(macro), 123);
        Assert.IsTrue(macro.Editor.Undo()); Assert.AreEqual(99ul, macro.Events[0].TimeSinceLastEvent);
        Assert.IsTrue(macro.Editor.Undo()); Assert.AreEqual(10ul, macro.Events[0].TimeSinceLastEvent);
        Assert.AreSame(tail, macro.Events[^1]);
    }

    [TestMethod]
    public void RawEditSupportsAllWireFieldsAndUndo()
    {
        using var macro = Macro(Move(1)); var before = Bytes(macro); var raw = macro.Events[0].OriginalProtobufInputEvent.Clone();
        raw.TimeSinceLastEvent = ulong.MaxValue; raw.MouseEvent.X = int.MinValue; raw.MouseEvent.Y = int.MaxValue;
        raw.MouseEvent.ActionType = 0x12345678; raw.MouseEvent.WheelRotation = uint.MaxValue;
        raw.MouseEvent.RelativePosition = true; raw.MouseEvent.MappedToVirtualDesktop = false;
        macro.Editor.EditRaw(macro.Events[0], raw);
        Assert.AreEqual(raw, macro.Events[0].OriginalProtobufInputEvent);
        Assert.IsTrue(macro.Editor.Undo()); SameBytes(before, macro);
    }

    [TestMethod]
    public void MultiSelectionOrdersRawEventsAndManualInsertionFollowsLastSelection()
    {
        using var macro = Macro(Move(1), K(VirtualKey.A), K(VirtualKey.A, true), Move(2));
        macro.Editor.SelectActions([A(macro, 2), A(macro)]);
        CollectionAssert.AreEqual(new InputEvent[] { macro.Events[0], macro.Events[3] }, macro.SelectedEvents.ToArray());
        macro.CreateKeyboardEventManually();
        Assert.IsInstanceOfType<KeyboardEvent>(macro.Events[4]);
        Assert.IsTrue(macro.Editor.Undo()); Assert.HasCount(4, macro.Events);
    }

    [TestMethod]
    public void SelectionFollowsEventIdentityAfterRegrouping()
    {
        using var macro = Macro(Move(1), Move(2), Move(3)); macro.Editor.SelectActions([A(macro)]);
        macro.Events[1].TimeSinceLastEvent = 999999;
        Assert.HasCount(2, macro.Editor.SelectedActions());
    }

    [TestMethod]
    public void ClearAsEditorCommandCanBeUndoneButLaterReplacementCannot()
    {
        using var macro = Macro(Move(1)); var revision = macro.ContentRevision;
        macro.Editor.Execute("Clear", macro.Clear);
        Assert.AreEqual(revision + 1, macro.ContentRevision); Assert.IsTrue(macro.Editor.Undo()); Assert.HasCount(1, macro.Events);
        macro.Editor.Execute("Clear", macro.Clear); macro.PopulateEventCollectionWithNewEvents([Move(9)]);
        Assert.IsFalse(macro.Editor.Undo()); Assert.AreEqual(9, ((MouseEvent)macro.Events[0]).X);
    }

    [TestMethod]
    public void CaptureAndAutoDelayKeepTheirSessionBindingWithAnOpenEditor()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredVm(transport);
        var macro = vm.ActiveMacro!; macro.AddEvent(Move(10)); macro.Editor.Refresh();
        macro.Editor.SetWait(A(macro), 99);
        vm.StartRecording(); var session = transport.Starts.Single(); transport.Begin(session);
        transport.Push(session, Key(0x41, false, 15)); vm.Deliver();
        Assert.IsTrue(macro.Editor.Undo()); Assert.HasCount(2, macro.Events);
        macro.Editor.SetWait(A(macro), 123); vm.StopRecording(777);
        transport.Push(session, Key(0x41, true, 20)); transport.End(session);
        vm.AddNewTab(); vm.Deliver();
        Assert.HasCount(3, macro.Events); Assert.IsTrue(macro.Events.All(e => e.TimeSinceLastEvent == 777));
        Assert.IsFalse(macro.Editor.Undo()); Assert.IsEmpty(vm.ActiveMacro!.Events);
    }

    [TestMethod]
    public void DenseCaptureOnlyProcessesNewSamplesAndCoalescesInvalidations()
    {
        using var macro = Macro(Move(0)); var editor = macro.Editor; var action = A(macro);
        var invalidations = 0; editor.Invalidated += (_, _) => invalidations++;
        for (var i = 1; i < 100000; i++) macro.AddEvent(Move(i, delay: 1));
        Assert.AreEqual(1, invalidations);
        Assert.AreEqual(1, editor.Projection.ProcessedCount, "Capture should not rebuild projection per sample.");
        editor.Refresh();
        Assert.AreSame(action, A(macro)); Assert.AreEqual(100000, A(macro).Count);
        var display = PathDisplay.Decimate(editor.Projection.Samples, 0, macro.Events.Count);
        Assert.IsTrue(display.Count <= 1200); Assert.AreEqual(0, display[0].Index); Assert.AreEqual(99999, display[^1].Index);
        Assert.AreEqual(100000, macro.Events.Count);
    }

    [TestMethod]
    public void DecimationDoesNotConnectAcrossHiddenCoordinateSegments()
    {
        using var macro = Macro(Move(0), Move(1), Move(5, relative: true), Move(10), Move(20));
        var display = PathDisplay.Decimate(macro.Editor.Projection.Samples, 0, 5, 2);
        Assert.IsTrue(display[^1].StartsSegment);
    }

    [TestMethod]
    public void PreviewHoldsDuringWaitsAndHandlesZeroTimeEventsAndHugeTimes()
    {
        using var macro = Macro(Move(10, delay: 10), Move(20, delay: 1000), Move(30, delay: 0), Move(40, delay: ulong.MaxValue));
        var p = macro.Editor.Projection;
        Assert.IsNull(p.SampleAt(9)); Assert.AreEqual(10d, p.SampleAt(100)!.Value.Position!.Value.X);
        Assert.AreEqual(30d, p.SampleAt(1010)!.Value.Position!.Value.X);
        Assert.AreEqual(40d, p.SampleAt(p.TotalTime)!.Value.Position!.Value.X);
    }

    [TestMethod]
    public void DisposedEditorDetachesFromCaptureAndInputMutations()
    {
        var macro = Macro(Move(1)); var editor = macro.Editor; var called = false;
        editor.Invalidated += (_, _) => called = true;
        macro.Dispose(); macro.AddEvent(Move(2)); macro.Events[0].TimeSinceLastEvent = 123;
        Assert.IsFalse(called); Assert.ThrowsExactly<ObjectDisposedException>(() => editor.Refresh());
    }

    [TestMethod]
    public void HumanTimesRetainExactMicrosecondsWithoutLargeRawCounts()
    {
        Assert.AreEqual("125 ms", TimeText.Human(125000)); Assert.AreEqual("1.234567 s", TimeText.Human(1234567));
        Assert.AreEqual(new BigInteger(1234567), TimeText.ParseSeconds("1.234567"));
        Assert.AreEqual((BigInteger)ulong.MaxValue * 20, TimeText.ParseSeconds(TimeText.Seconds((BigInteger)ulong.MaxValue * 20)));
        Assert.ThrowsExactly<ArgumentException>(() => TimeText.ParseSeconds("1.0000001"));
        Assert.ThrowsExactly<ArgumentException>(() => TimeText.ParseSeconds("-1"));
    }

    [TestMethod]
    public void RawSubsetNeverBroadensDuringCaptureRefreshOrRegrouping()
    {
        using var macro = Macro(Move(1), Move(2), Move(3)); var editor = macro.Editor;
        editor.SelectActions([A(macro)]);
        var raw = macro.Events[1]; editor.SelectRawEvents([raw]);
        macro.AddEvent(Move(4)); editor.Refresh(); editor.RefreshSelection(editor.Projection.Actions);
        CollectionAssert.AreEqual(new[] { raw }, macro.SelectedEvents.ToArray()); Assert.IsTrue(editor.RawSelection);
        editor.SetWait(A(macro), 99); editor.Refresh(); editor.RefreshSelection(editor.SelectedActions());
        CollectionAssert.AreEqual(new[] { raw }, macro.SelectedEvents.ToArray());
        macro.RemoveSelectedEvents();
        CollectionAssert.AreEqual(new[] { 1, 3, 4 }, macro.Events.OfType<MouseEvent>().Select(m => m.X).ToArray());
        Assert.IsTrue(editor.Undo()); Assert.IsTrue(editor.RawSelection);
        CollectionAssert.AreEqual(new[] { raw }, macro.SelectedEvents.ToArray());
    }

    [TestMethod]
    public void RawExpandAndCollapseUseExplicitSelectionScopesIncludingMultipleActions()
    {
        using var macro = Macro(Move(1), Move(2), K(VirtualKey.A), K(VirtualKey.A, true)); var editor = macro.Editor;
        var actions = editor.Projection.Actions.ToArray(); editor.SelectActions(actions);
        var firstRow = macro.Events[0]; editor.SelectRawEvents([firstRow]);
        Assert.HasCount(1, macro.SelectedEvents);
        editor.RefreshSelection(actions); Assert.HasCount(1, macro.SelectedEvents);
        editor.SelectRawEvents([macro.Events[1], macro.Events[3]]);
        editor.RefreshSelection(actions); Assert.HasCount(2, macro.SelectedEvents);
        editor.SelectActions(actions); Assert.HasCount(4, macro.SelectedEvents); Assert.IsFalse(editor.RawSelection);
    }

    [TestMethod]
    public void RawMultiSelectionRemainsAccessibleAfterTimingRegroupsAnAction()
    {
        using var macro = Macro(Move(1), Move(2), Move(3)); var editor = macro.Editor;
        editor.SelectRawEvents([macro.Events[0], macro.Events[2]]);
        var raw = macro.Events[2].OriginalProtobufInputEvent.Clone(); raw.TimeSinceLastEvent = 500000;
        editor.EditRaw(macro.Events[2], raw); editor.Refresh();
        var rows = new RawEventRows(); rows.Refresh(macro.Events, editor.SelectedActions());
        Assert.HasCount(3, rows);
        Assert.IsTrue(macro.SelectedEvents.All(input => rows.Any(row => ReferenceEquals(row.Input, input))));
        editor.RefreshSelection(editor.SelectedActions()); Assert.HasCount(2, macro.SelectedEvents);
    }

    [TestMethod]
    public void RawRowsAreReusedDuringCaptureAndReleasedOnCollapse()
    {
        using var macro = Macro(Move(1)); var rows = new RawEventRows(); rows.Refresh(macro.Events, A(macro)); var first = rows[0];
        for (var i = 2; i < 500; i++) macro.AddEvent(Move(i));
        rows.Refresh(macro.Events, A(macro)); Assert.AreSame(first, rows[0]); Assert.HasCount(499, rows);
        rows.Close(); macro.AddEvent(Move(500)); macro.Editor.Refresh(); Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void RawRowsIncludeDiscontiguousActionsInStreamOrder()
    {
        using var macro = Macro(Move(1), K(VirtualKey.A), K(VirtualKey.A, true), Move(2));
        var rows = new RawEventRows(); rows.Refresh(macro.Events, [A(macro), A(macro, 2)]);
        CollectionAssert.AreEqual(new[] { 0, 3 }, rows.Select(row => row.Index).ToArray());
        macro.AddEvent(Move(3)); rows.Refresh(macro.Events, [A(macro), A(macro, 2)]);
        CollectionAssert.AreEqual(new[] { 0, 3, 4 }, rows.Select(row => row.Index).ToArray());
    }

    [TestMethod]
    public void UnanchoredDragDoesNotOfferGeometryEvenIfLaterMovesAreAbsolute()
    {
        using var macro = Macro(Button(MouseActionTypeFlags.LeftDown), Move(20), Button(MouseActionTypeFlags.LeftUp));
        Assert.AreEqual(ActionKind.Drag, A(macro).Kind);
        StringAssert.Contains(macro.Editor.GeometryBlockReason(A(macro))!, "unknown position");
    }

    [TestMethod]
    public void LandmarkProjectionDistinguishesClickAndDragWithoutMutatingEvents()
    {
        using var macro = Macro(Move(0), Button(MouseActionTypeFlags.LeftDown), Button(MouseActionTypeFlags.LeftUp),
            Button(MouseActionTypeFlags.LeftDown), Move(30), Button(MouseActionTypeFlags.LeftUp));
        var bytes = Bytes(macro); var markers = macro.Editor.Projection.MouseLandmarks;
        CollectionAssert.AreEqual(new[] { ActionKind.Click, ActionKind.Drag }, markers.Select(a => a.Kind).ToArray());
        Assert.AreEqual(0d, macro.Editor.Projection.Samples[markers[1].Start].Position!.Value.X);
        SameBytes(bytes, macro);
    }

    [TestMethod]
    public void RandomInputProjectionConservesBytesRangesAndExactTime()
    {
        var random = new Random(46); var events = new List<InputEvent>();
        for (var i = 0; i < 2000; i++)
        {
            InputEvent input = random.Next(5) switch
            {
                0 => K((VirtualKey)random.Next(256), random.Next(2) == 0),
                1 => Button((MouseActionTypeFlags)(1 << random.Next(14)), data: (uint)random.Next(4)),
                _ => Move(random.Next(-500, 500), random.Next(-500, 500), relative: random.Next(2) == 0)
            };
            input.TimeSinceLastEvent = (ulong)random.Next(300000); events.Add(input);
        }
        using var macro = Macro(events.ToArray()); var before = Bytes(macro);
        CollectionAssert.AreEqual(events, macro.Editor.Projection.Actions.SelectMany(macro.Editor.EventsFor).ToArray());
        Assert.AreEqual(Sum(events), macro.Editor.Projection.Actions.Aggregate(BigInteger.Zero, (sum, a) => sum + a.Wait + a.Duration));
        SameBytes(before, macro);
    }

    [TestMethod]
    public void DenseActionRemovalAndUndoPreserveAllSamplesAndSelection()
    {
        using var macro = Macro(Enumerable.Range(0, 50000).Select(i => (InputEvent)Move(i)).ToArray());
        var originals = macro.Events.ToArray(); macro.Editor.SelectActions([A(macro)]);
        macro.RemoveSelectedEvents(); Assert.IsEmpty(macro.Events);
        var captured = K(VirtualKey.A); macro.AddEvent(captured);
        Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(originals.Concat([captured]).ToArray(), macro.Events.ToArray());
        CollectionAssert.AreEqual(originals, macro.SelectedEvents.ToArray());
    }

    [TestMethod]
    public void NoOpAutoDelayInvalidatesOnlyItsOriginalMacroAfterDeferredCompletion()
    {
        var transport = new FakeRecordingTransport(); using var vm = new DeferredVm(transport);
        var owner = vm.ActiveMacro!; owner.AddEvent(Move(0, delay: 10)); owner.Editor.SetWait(A(owner), 777);
        vm.StartRecording(); var session = transport.Starts.Single(); transport.Begin(session);
        vm.StopRecording(777);
        var other = vm.AddNewTab(); other.AddEvent(Move(1, delay: 10)); other.Editor.SetWait(A(other), 888);
        transport.End(session);
        Assert.IsTrue(owner.Editor.CanUndo, "Native completion must not bypass the UI delivery queue.");
        vm.Deliver();
        Assert.IsFalse(owner.Editor.CanUndo); Assert.IsFalse(owner.Editor.Undo());
        Assert.AreEqual(777ul, owner.Events[0].TimeSinceLastEvent);
        Assert.IsTrue(other.Editor.Undo()); Assert.AreEqual(10ul, other.Events[0].TimeSinceLastEvent);
    }

    [TestMethod]
    public void StaleRevisionAutoDelayCannotInvalidateNewContentHistory()
    {
        var transport = new FakeRecordingTransport(); using var vm = new DeferredVm(transport);
        var macro = vm.ActiveMacro!; macro.AddEvent(Move(0, delay: 10)); macro.Editor.SetWait(A(macro), 777);
        vm.StartRecording(); var session = transport.Starts.Single(); transport.Begin(session); vm.StopRecording(777);
        macro.Clear(); macro.AddEvent(Move(5, delay: 10)); macro.Editor.SetWait(A(macro), 888);
        transport.End(session); vm.Deliver();
        Assert.IsTrue(macro.Editor.Undo()); Assert.AreEqual(10ul, macro.Events[0].TimeSinceLastEvent);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnmatchedReleaseCannotBeForgottenWhenTrackedStateReturnsToNeutral(bool keyboard)
    {
        InputEvent[] sequence = keyboard
            ? [Move(100), K(VirtualKey.Control), K(VirtualKey.S, true), K(VirtualKey.Control, true), Move(200)]
            : [Move(100), Button(MouseActionTypeFlags.LeftDown), Button(MouseActionTypeFlags.RightUp), Button(MouseActionTypeFlags.LeftUp), Move(200)];
        using var macro = Macro(sequence); var bytes = Bytes(macro);
        Assert.AreEqual(ActionKind.Sequence, A(macro, 1).Kind); Assert.IsFalse(A(macro, 1).Complete);
        Assert.AreEqual(1, macro.Editor.Projection.IncompleteActionCount);
        StringAssert.Contains(macro.Editor.GeometryBlockReason(A(macro))!, "anomalous");
        SameBytes(bytes, macro);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DownstreamDragAnchorIsPreservedRegardlessOfModifierDisplayGrouping(bool modifier)
    {
        var first = Move(100); var approach1 = Move(150, delay: modifier ? 10ul : ActionProjection.MovementPause); var approachEnd = Move(200);
        var drag1 = Move(250); var dragEnd = Move(300); var down = Button(MouseActionTypeFlags.LeftDown);
        var inputs = new List<InputEvent> { first };
        if (modifier) inputs.Add(K(VirtualKey.Control));
        inputs.AddRange([approach1, approachEnd, down, drag1, dragEnd, Button(MouseActionTypeFlags.LeftUp)]);
        if (modifier) inputs.Add(K(VirtualKey.Control, true));
        using var macro = Macro(inputs.ToArray()); var before = Bytes(macro); var order = macro.Events.ToArray();
        if (modifier) Assert.AreEqual(ActionKind.Sequence, A(macro, 1).Kind);
        macro.Editor.SetDestination(A(macro), 200, 0); macro.Editor.Refresh();
        Assert.AreEqual(200, first.X); Assert.AreEqual(200, approach1.X); Assert.AreEqual(200, approachEnd.X);
        Assert.AreEqual(200d, macro.Editor.Projection.Samples[macro.Events.IndexOf(down)].Position!.Value.X);
        Assert.AreEqual(250, drag1.X); Assert.AreEqual(300, dragEnd.X);
        CollectionAssert.AreEqual(order, macro.Events.ToArray());
        Assert.IsTrue(macro.Editor.Undo()); SameBytes(before, macro);
    }

    [TestMethod]
    public void EmptyAutoDelayWithNoApplicableEventsDoesNotInvalidateLaterEdits()
    {
        var transport = new FakeRecordingTransport(); using var vm = new DeferredVm(transport);
        var macro = vm.ActiveMacro!;
        vm.StartRecording(); var session = transport.Starts.Single(); transport.Begin(session); vm.StopRecording(777);
        macro.AddEvent(Move(5, delay: 10)); macro.Editor.SetWait(A(macro), 888);
        transport.End(session); vm.Deliver();
        Assert.IsTrue(macro.Editor.Undo()); Assert.AreEqual(10ul, macro.Events[0].TimeSinceLastEvent);
    }

    private sealed class DeferredVm(FakeRecordingTransport transport) : MainWindowViewModel(new RecordEngine(transport), new FakePlaybackEngine())
    {
        private readonly Queue<Action> _pending = [];
        protected override void InvokeDispatcher(Action action) => _pending.Enqueue(action);
        public void Deliver() { while (_pending.TryDequeue(out var action)) action(); }
    }
}
