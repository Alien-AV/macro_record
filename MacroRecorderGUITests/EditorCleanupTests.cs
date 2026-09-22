using Google.Protobuf;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUITests;

[TestClass]
public class EditorCleanupTests
{
    [TestMethod]
    [DataRow(0u, 1u)]
    [DataRow(1u, 0u)]
    [DataRow(0u, 0u)]
    public void LegacyX1TransitionsCompleteBeforeAConditionalWait(uint downData, uint upData)
    {
        var condition = WaitValidation.NewWindow();
        condition.Window.Target.Title = "simulated target";
        InputEvent[] events =
        [
            ExtraButton(MouseActionTypeFlags.XDown, downData),
            ExtraButton(MouseActionTypeFlags.XUp, upData),
            new WaitConditionEvent(condition)
        ];
        var originals = events.Select(input => input.OriginalProtobufInputEvent.ToByteArray()).ToArray();
        WaitValidation.ValidateSchedule(events);
        var projection = Project(events);
        var preview = new VisualPreview(events, projection);

        Assert.IsTrue(projection.Actions[0].Complete, "Legacy zero means X1, as in playback; a matching release must complete the action.");
        Assert.AreEqual(ActionKind.Click, projection.Actions[0].Kind);
        Assert.AreEqual("X1 click", projection.Actions[0].Name);
        Assert.IsTrue(projection.Actions[1].Complete);
        Assert.AreEqual(0, projection.IncompleteActionCount);
        CollectionAssert.AreEqual(new[] { "Mouse X1" }, preview.Seek(10).HeldButtons.ToArray());
        Assert.HasCount(0, preview.Seek(20).HeldButtons);
        Assert.AreEqual(ActionKind.Wait, preview.Checkpoint(20)!.Kind);
        CollectionAssert.AreEqual(new[] { "Mouse X1" }, preview.Seek(10).HeldButtons.ToArray());
        AssertAuthoringBoundary(events, held: false);
        for (var i = 0; i < originals.Length; i++)
            CollectionAssert.AreEqual(originals[i], events[i].OriginalProtobufInputEvent.ToByteArray());
    }

    [TestMethod]
    [DataRow(2u)]
    [DataRow(3u)]
    public void LegacyX1ReleaseDoesNotHideHeldX2(uint downData)
    {
        InputEvent[] events = [ExtraButton(MouseActionTypeFlags.XDown, downData), ExtraButton(MouseActionTypeFlags.XUp, 0)];
        var projection = Project(events);
        var preview = new VisualPreview(events, projection);

        CollectionAssert.AreEqual(new[] { "Mouse X2" }, preview.Seek(20).HeldButtons.ToArray());
        Assert.IsFalse(projection.Actions[0].Complete);
        Assert.AreEqual(1, projection.IncompleteActionCount);
        AssertAuthoringBoundary(events, held: true);
    }

    private static void AssertAuthoringBoundary(InputEvent[] events, bool held)
    {
        var condition = WaitValidation.NewWindow();
        condition.Window.Target.Title = "simulated target";
        var schedule = events.Append(new WaitConditionEvent(condition));
        using var macro = new MacroViewModel("legacy buttons", new FakePlaybackEngine());
        foreach (var input in events) macro.AddEvent(input);
        macro.Editor.Refresh();
        var original = macro.SnapshotBytes();
        var anchor = macro.Editor.Projection.Actions[^1];
        if (held)
        {
            Assert.Throws<ArgumentException>(() => WaitValidation.ValidateSchedule(schedule));
            Assert.Throws<ArgumentException>(() => macro.Editor.InsertClick(anchor, new(MouseButton.Left)));
            Assert.IsFalse(macro.Editor.CanUndo);
        }
        else
        {
            WaitValidation.ValidateSchedule(schedule);
            macro.Editor.InsertClick(anchor, new(MouseButton.Left));
            Assert.IsTrue(macro.Editor.Undo());
        }
        CollectionAssert.AreEqual(original, macro.SnapshotBytes());
    }

    private static MouseEvent ExtraButton(MouseActionTypeFlags flags, uint data)
    {
        var input = new MouseEvent(0, 0, flags) { MouseData = data, TimeSinceLastEvent = 10 };
        input.OriginalProtobufInputEvent.MergeFrom(new byte[] { 0xA0, 0x06, 0x07 });
        input.OriginalProtobufInputEvent.MouseEvent.MergeFrom(new byte[] { 0xA0, 0x06, 0x09 });
        return input;
    }

    private static ActionProjection Project(IEnumerable<InputEvent> events)
    {
        var projection = new ActionProjection();
        foreach (var input in events) projection.Append(input);
        projection.FlushNotifications();
        return projection;
    }
}
