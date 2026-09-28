using Google.Protobuf;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class EditorActionUxTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("  ")]
    public void EmptyTimesCommitZeroAndUndoPreservesUnknownFields(string text)
    {
        using var macro = new MacroViewModel("Empty time", new FakePlaybackEngine());
        macro.AddEvent(new MouseEvent(10, 20, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 100 });
        macro.AddEvent(new MouseEvent(30, 40, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 200 });
        foreach (var input in macro.Events)
        {
            input.OriginalProtobufInputEvent.MergeFrom(new byte[] { 0xA0, 0x06, 0x07 });
            input.OriginalProtobufInputEvent.MouseEvent.MergeFrom(new byte[] { 0xA0, 0x06, 0x09 });
        }
        var original = macro.SnapshotBytes();
        macro.Editor.Refresh();
        var edits = new ActionFieldEdits(macro);
        edits.Select(macro.Editor.Projection.Actions[0]);
        edits.Change(ActionField.Wait, text); edits.Change(ActionField.Duration, text);
        Assert.IsTrue(edits.TryCommitAll());
        CollectionAssert.AreEqual(new ulong[] { 0, 0 }, macro.Events.Select(e => e.TimeSinceLastEvent).ToArray());
        Assert.IsFalse(edits.HasDrafts);
        Assert.IsTrue(macro.Editor.Undo()); Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(original, macro.SnapshotBytes());
        Assert.IsFalse(macro.Editor.Undo());
    }

    [TestMethod]
    public void EditedDelayRemainsOwnedByItsInputAfterMovementGroupsMerge()
    {
        using var macro = new MacroViewModel("Gap owner", new FakePlaybackEngine());
        macro.AddEvent(new MouseEvent(10, 20, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 10 });
        macro.AddEvent(new MouseEvent(30, 40, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 500000 });
        macro.Editor.Refresh();
        var edits = new ActionFieldEdits(macro);
        edits.Select(macro.Editor.Projection.Actions[1]);
        edits.Change(ActionField.Wait, ""); Assert.IsTrue(edits.TryCommitAll());
        macro.Editor.Refresh(); Assert.AreEqual(1, macro.Editor.Projection.Actions.Count);
        edits.Select(macro.Editor.Projection.Actions[0]);
        edits.Change(ActionField.Wait, "0.000123"); Assert.IsTrue(edits.TryCommitAll());
        Assert.AreEqual(10UL, macro.Events[0].TimeSinceLastEvent);
        Assert.AreEqual(123UL, macro.Events[1].TimeSinceLastEvent);
    }

    [TestMethod]
    public void DelayPresentationDoesNotRewriteLegacyTiming()
    {
        using var macro = new MacroViewModel("Legacy gaps", new FakePlaybackEngine());
        macro.AddEvent(new MouseEvent(0, 0, MouseActionTypeFlags.LeftDown) { TimeSinceLastEvent = ulong.MaxValue });
        macro.AddEvent(new MouseEvent(0, 0, MouseActionTypeFlags.LeftUp) { TimeSinceLastEvent = 123456 });
        var original = macro.SnapshotBytes();
        macro.Editor.Refresh();
        var action = macro.Editor.Projection.Actions.Single();
        Assert.AreEqual(ulong.MaxValue, action.Wait);
        Assert.AreEqual("123ms", action.DisplayTime);
        Assert.AreEqual(Microsoft.UI.Xaml.Visibility.Visible, action.LeadingDelayVisibility);
        StringAssert.Contains(action.LeadingDelayLabel, TimeText.Seconds(ulong.MaxValue));
        CollectionAssert.AreEqual(original, macro.SnapshotBytes());
        Assert.IsFalse(macro.Editor.CanUndo);
    }
}
