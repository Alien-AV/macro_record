using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class ActionFieldEditsTests
{
    private static MacroViewModel Macro()
    {
        var macro = new MacroViewModel("field edits", new FakePlaybackEngine());
        macro.AddEvent(new MouseEvent(10, 20, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 100 });
        macro.AddEvent(new MouseEvent(30, 40, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 200 });
        macro.AddEvent(new MouseEvent(50, 60, MouseActionTypeFlags.Move) { TimeSinceLastEvent = ActionProjection.MovementPause });
        macro.Editor.Refresh();
        return macro;
    }

    private static ActionFieldEdits Edits(MacroViewModel macro)
    {
        var edits = new ActionFieldEdits(macro);
        edits.Select(macro.Editor.Projection.Actions[0]);
        return edits;
    }

    [TestMethod]
    public void TimingAndDestinationAreCommittedBeforeSerializingWithoutLosingSiblingDrafts()
    {
        using var macro = Macro(); var edits = Edits(macro);
        edits.Change(ActionField.Wait, "0.012345");
        edits.Change(ActionField.Duration, "0.000500");
        edits.Change(ActionField.DestinationX, "90");
        edits.Change(ActionField.DestinationY, "100");
        Assert.IsTrue(edits.TryCommit(ActionField.Wait));
        Assert.AreEqual("0.000500", edits.Text(ActionField.Duration, "model"));
        Assert.AreEqual("90", edits.Text(ActionField.DestinationX, "model"));
        Assert.IsTrue(edits.TryCommitAll());
        var snapshot = macro.Events.Select(input => input.OriginalProtobufInputEvent.Clone()).ToArray();
        Assert.AreEqual(12345UL, snapshot[0].TimeSinceLastEvent);
        Assert.AreEqual(500UL, snapshot[1].TimeSinceLastEvent);
        Assert.AreEqual(90, snapshot[1].MouseEvent.X); Assert.AreEqual(100, snapshot[1].MouseEvent.Y);
        Assert.IsFalse(edits.HasDrafts); Assert.IsNull(edits.Error);
    }

    [TestMethod]
    public void CommitUsesInspectorOwnerEvenIfListOrRawSelectionHasAlreadyChanged()
    {
        using var macro = Macro(); var edits = Edits(macro);
        edits.Change(ActionField.Wait, "0.025");
        macro.Editor.SelectActions([macro.Editor.Projection.Actions[1]]);
        Assert.IsTrue(edits.TryCommitAll());
        Assert.AreEqual(25000UL, macro.Events[0].TimeSinceLastEvent);
        Assert.AreEqual(ActionProjection.MovementPause, macro.Events[2].TimeSinceLastEvent);
        CollectionAssert.AreEqual(new[] { macro.Events[2] }, macro.SelectedEvents.ToArray());
    }

    [TestMethod]
    public void InvalidDraftSurvivesOtherCommitRefreshAndRepeatedCommandAttempts()
    {
        using var macro = Macro(); var edits = Edits(macro);
        edits.Change(ActionField.Duration, "unfinished");
        Assert.IsFalse(edits.TryCommit(ActionField.Duration)); var error = edits.Error;
        edits.Change(ActionField.Wait, "0.05"); Assert.IsTrue(edits.TryCommit(ActionField.Wait));
        macro.Editor.Refresh(); edits.Select(macro.Editor.Projection.Actions[0]);
        Assert.AreEqual(error, edits.Error);
        Assert.AreEqual("unfinished", edits.Text(ActionField.Duration, "0.0002"));
        for (var i = 0; i < 3; i++) Assert.IsFalse(edits.TryCommitAll());
        Assert.AreEqual(200UL, macro.Events[1].TimeSinceLastEvent);
        edits.Change(ActionField.Duration, "0.000750");
        Assert.IsTrue(edits.TryCommitAll()); Assert.IsNull(edits.Error);
        Assert.AreEqual(750UL, macro.Events[1].TimeSinceLastEvent);
    }

    [TestMethod]
    [DataRow("-")]
    [DataRow("2147483648")]
    [DataRow("")]
    public void DestinationPairIsAtomicAndCanBeCorrected(string invalidY)
    {
        using var macro = Macro(); var edits = Edits(macro);
        edits.Change(ActionField.DestinationX, "123"); edits.Change(ActionField.DestinationY, invalidY);
        Assert.IsFalse(edits.TryCommit(ActionField.DestinationX));
        Assert.AreEqual(30, ((MouseEvent)macro.Events[1]).X);
        Assert.AreEqual("123", edits.Text(ActionField.DestinationX, "30"));
        Assert.AreEqual(invalidY, edits.Text(ActionField.DestinationY, "40"));
        edits.Change(ActionField.DestinationY, "456");
        Assert.IsTrue(edits.TryCommit(ActionField.DestinationY));
        Assert.AreEqual(123, ((MouseEvent)macro.Events[1]).X);
        Assert.AreEqual(456, ((MouseEvent)macro.Events[1]).Y);
    }

    [TestMethod]
    public void XThenYUsesCurrentCompanionCoordinateAndRepeatedCommitAddsNoUndo()
    {
        using var macro = Macro(); var edits = Edits(macro);
        edits.Change(ActionField.DestinationX, "80"); Assert.IsTrue(edits.TryCommitAll());
        edits.Change(ActionField.DestinationY, "90"); Assert.IsTrue(edits.TryCommitAll());
        Assert.AreEqual(80, ((MouseEvent)macro.Events[1]).X); Assert.AreEqual(90, ((MouseEvent)macro.Events[1]).Y);
        Assert.IsTrue(edits.TryCommitAll()); Assert.IsTrue(edits.TryCommitAll());
        Assert.IsTrue(macro.Editor.Undo()); Assert.AreEqual(40, ((MouseEvent)macro.Events[1]).Y);
        Assert.IsTrue(macro.Editor.Undo()); Assert.AreEqual(30, ((MouseEvent)macro.Events[1]).X);
        Assert.IsFalse(macro.Editor.Undo());
    }

    [TestMethod]
    public void WaitMergingMovementGroupsDoesNotDiscardRemainingDrafts()
    {
        using var macro = Macro(); var edits = new ActionFieldEdits(macro);
        edits.Select(macro.Editor.Projection.Actions[1]);
        edits.Change(ActionField.DestinationY, "bad");
        edits.Change(ActionField.Wait, "0.000010");
        Assert.IsTrue(edits.TryCommit(ActionField.Wait));
        macro.Editor.Refresh(); edits.Select(macro.Editor.Projection.Actions[0]);
        Assert.AreEqual("bad", edits.Text(ActionField.DestinationY, "60"));
        Assert.IsFalse(edits.TryCommitAll());
    }

    [TestMethod]
    public void ExternalInsertionRefreshesProjectionWithoutRetargetingTheDraft()
    {
        using var macro = Macro(); var edits = Edits(macro); var owner = macro.Events[0];
        edits.Change(ActionField.Wait, "0.000888");
        macro.Events.Insert(0, new KeyboardEvent(Windows.System.VirtualKey.A, true));
        Assert.IsTrue(edits.TryCommitAll());
        Assert.AreEqual(888UL, owner.TimeSinceLastEvent);
        Assert.AreEqual(0UL, macro.Events[0].TimeSinceLastEvent);
    }

    [TestMethod]
    public void CancelledDragRemovesOnlyDestinationDrafts()
    {
        using var macro = Macro(); var edits = Edits(macro);
        edits.Change(ActionField.Duration, "bad");
        edits.Change(ActionField.DestinationX, "90"); edits.Change(ActionField.DestinationY, "100");
        edits.CancelDestination();
        Assert.AreEqual("30", edits.Text(ActionField.DestinationX, "30"));
        Assert.AreEqual("bad", edits.Text(ActionField.Duration, "model"));
        Assert.AreEqual(30, ((MouseEvent)macro.Events[1]).X);
    }

    [TestMethod]
    public void DeletedOwnerReportsErrorInsteadOfEditingNewSelection()
    {
        using var macro = Macro(); var edits = Edits(macro);
        edits.Change(ActionField.Wait, "1"); macro.Events.RemoveAt(0);
        Assert.IsFalse(edits.TryCommitAll()); Assert.IsNotNull(edits.Error);
        Assert.AreEqual(200UL, macro.Events[0].TimeSinceLastEvent);
        Assert.AreEqual("1", edits.Text(ActionField.Wait, "model"));
    }
}
