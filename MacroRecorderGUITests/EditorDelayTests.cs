using System.Numerics;
using Google.Protobuf;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class EditorDelayTests
{
    private static MacroViewModel Macro(params InputEvent[] inputs)
    {
        var macro = new MacroViewModel("Delay editor", new FakePlaybackEngine());
        foreach (var input in inputs) macro.AddEvent(input);
        macro.Editor.Refresh();
        return macro;
    }

    [TestMethod]
    [DataRow(0UL)]
    [DataRow(1UL)]
    [DataRow(DelayEvent.MaximumDurationMicroseconds)]
    public void DelayOnlyIsAnEditableStepWithExactTotalAndNoInputState(ulong duration)
    {
        var delay = new DelayEvent(duration) { TimeSinceLastEvent = ulong.MaxValue };
        using var macro = Macro(delay);
        var bytes = macro.SnapshotBytes();
        var projection = macro.Editor.Projection;
        var action = projection.Actions.Single();
        Assert.AreEqual(ActionKind.Delay, action.Kind);
        Assert.IsTrue(action.Complete);
        Assert.AreEqual((BigInteger)ulong.MaxValue + duration, projection.TotalTime);
        Assert.AreEqual(projection.TotalTime, action.EndTime);
        Assert.AreEqual((BigInteger)duration, action.Duration);
        var preview = new VisualPreview(macro.Events, projection);
        var frame = preview.Seek(ulong.MaxValue);
        Assert.AreSame(action, frame.Current);
        Assert.IsEmpty(frame.HeldKeys); Assert.IsEmpty(frame.HeldButtons);
        Assert.IsNull(frame.Pointer?.Position);
        Assert.AreEqual(projection.TotalTime, preview.Segments().Last().End);
        var edits = new ActionFieldEdits(macro); edits.Select(action);
        edits.Change(ActionField.FixedDelay, ""); Assert.IsTrue(edits.TryCommitAll());
        Assert.AreEqual(0UL, delay.DurationMicroseconds);
        Assert.AreEqual(1, macro.Events.Count, "Explicit zero delays must remain selectable events.");
        if (duration > 0) Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(bytes, macro.SnapshotBytes());
    }

    [TestMethod]
    public void DelayDurationAndRawEditsPreserveUnknownFieldsAndUndoExactly()
    {
        var delay = new DelayEvent(123) { TimeSinceLastEvent = 17 };
        delay.OriginalProtobufInputEvent.MergeFrom(new byte[] { 0xa0, 6, 7 });
        delay.OriginalProtobufInputEvent.Delay.MergeFrom(new byte[] { 0xa0, 6, 9 });
        using var macro = Macro(delay);
        var original = macro.SnapshotBytes();
        var expected = delay.OriginalProtobufInputEvent.Clone();
        expected.Delay.DurationMicroseconds = 456;
        macro.Editor.SetFixedDelay(delay, 456);
        CollectionAssert.AreEqual(expected.ToByteArray(), delay.OriginalProtobufInputEvent.ToByteArray());
        var invalid = expected.Clone(); invalid.TimeSinceLastEvent = 999;
        invalid.Delay.DurationMicroseconds = DelayEvent.MaximumDurationMicroseconds + 1;
        Assert.ThrowsExactly<ArgumentException>(() => macro.Editor.EditRaw(delay, invalid));
        CollectionAssert.AreEqual(expected.ToByteArray(), delay.OriginalProtobufInputEvent.ToByteArray(), "Invalid duration cannot partially commit the raw pre-delay.");
        expected.TimeSinceLastEvent = 31; expected.Delay.DurationMicroseconds = 0;
        macro.Editor.EditRaw(delay, expected);
        Assert.IsTrue(macro.Editor.Undo()); Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(original, macro.SnapshotBytes());
        Assert.IsFalse(macro.Editor.CanUndo);
    }

    [TestMethod]
    public void InsertAfterLegacyDelayRunsAfterThatGapAndBeforeItsOriginalAction()
    {
        var owner = new KeyboardEvent(VirtualKey.A, false) { TimeSinceLastEvent = ulong.MaxValue };
        owner.OriginalProtobufInputEvent.MergeFrom(new byte[] { 0xa0, 6, 7 });
        using var macro = Macro(owner, new KeyboardEvent(VirtualKey.A, true) { TimeSinceLastEvent = 15 });
        var original = macro.SnapshotBytes();
        var inserted = macro.Editor.InsertDelay(macro.Editor.Projection.Actions.Single(), 20, afterDelay: owner);
        CollectionAssert.AreEqual(new InputEvent[] { inserted, owner, macro.Events[2] }, macro.Events.ToArray());
        Assert.AreEqual(ulong.MaxValue, inserted.TimeSinceLastEvent);
        Assert.AreEqual(0UL, owner.TimeSinceLastEvent);
        CollectionAssert.AreEqual(new[] { inserted }, macro.SelectedEvents.ToArray());
        macro.Editor.Refresh();
        Assert.AreEqual((BigInteger)ulong.MaxValue + 35, macro.Editor.Projection.TotalTime);
        Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(original, macro.SnapshotBytes());
    }

    [TestMethod]
    public void InsertingClickAfterAGapKeepsBalancedInputsAndOneUndo()
    {
        using var macro = Macro(new MouseEvent(10, 20, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 55 });
        var original = macro.SnapshotBytes();
        var owner = macro.Events[0];
        var inputs = macro.Editor.InsertClick(macro.Editor.Projection.Actions[0], new ClickDefinition(MouseButton.Right, HoldMicroseconds: 10), owner);
        Assert.AreEqual(55UL, inputs[0].TimeSinceLastEvent);
        Assert.AreEqual(10UL, inputs[1].TimeSinceLastEvent);
        Assert.AreSame(owner, macro.Events[2]); Assert.AreEqual(0UL, owner.TimeSinceLastEvent);
        Assert.IsTrue(macro.Editor.Undo()); Assert.IsFalse(macro.Editor.CanUndo);
        CollectionAssert.AreEqual(original, macro.SnapshotBytes());
    }

    [TestMethod]
    public void PreviewKeepsHeldInputsAndPointerAcrossFixedDelay()
    {
        var delay = new DelayEvent(100) { TimeSinceLastEvent = 5 };
        using var macro = Macro(new MouseEvent(10, 20, MouseActionTypeFlags.Move),
            new KeyboardEvent(VirtualKey.A, false), delay,
            new KeyboardEvent(VirtualKey.A, true) { TimeSinceLastEvent = 1 });
        var preview = new VisualPreview(macro.Events, macro.Editor.Projection);
        var frame = preview.Seek(50);
        Assert.AreEqual(ActionKind.Delay, frame.Current!.Kind);
        CollectionAssert.AreEqual(new uint[] { 65 }, frame.HeldKeys.ToArray());
        Assert.AreEqual(new PathPosition(10, 20, CoordinateSpace.AbsoluteDesktop), frame.Pointer!.Value.Position);
        Assert.IsNull(preview.Checkpoint(50));
        Assert.IsEmpty(preview.Seek(106).HeldKeys);
        Assert.AreEqual((BigInteger)106, macro.Editor.Projection.TotalTime);
        Assert.IsTrue(preview.Segments().Any(segment => segment.Start == 5 && segment.End == 105 && segment.Waiting));
    }

    [TestMethod]
    public void ReplacingDelayReplacesOnlyTheSelectedTimingStep()
    {
        foreach (var replaceGap in new[] { false, true })
        {
            var delay = new DelayEvent(123) { TimeSinceLastEvent = 17 };
            using var macro = Macro(delay);
            var original = macro.SnapshotBytes();
            var wait = macro.Editor.ReplaceDelayWithWait(macro.Editor.Projection.Actions[0], ConditionalWaitTests.Condition(), replaceGap ? delay : null);
            Assert.AreEqual(replaceGap ? 0UL : 17UL, wait.TimeSinceLastEvent);
            Assert.AreEqual(replaceGap ? 2 : 1, macro.Events.Count);
            if (replaceGap) { Assert.AreSame(delay, macro.Events[1]); Assert.AreEqual(123UL, delay.DurationMicroseconds); }
            Assert.IsTrue(macro.Editor.Undo());
            CollectionAssert.AreEqual(original, macro.SnapshotBytes());
        }
    }
}
