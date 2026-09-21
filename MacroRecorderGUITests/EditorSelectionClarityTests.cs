using System.Numerics;
using Google.Protobuf;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public class EditorSelectionClarityTests
{
    private static MouseEvent Mouse(int x, MouseActionTypeFlags flags = MouseActionTypeFlags.Move,
        ulong delay = 10, bool relative = false) => new(x, 0, flags)
        { TimeSinceLastEvent = delay, RelativePosition = relative };

    private static MacroViewModel Macro(params InputEvent[] events)
    {
        var macro = new MacroViewModel("editor clarity", new FakePlaybackEngine());
        foreach (var input in events) macro.AddEvent(input);
        macro.Editor.Refresh();
        return macro;
    }

    private static PathFrame Frame(MacroViewModel macro, RecordedAction action)
    {
        Assert.IsTrue(new EditorPresentation(macro.Editor).TryGetFrame(action, out var frame));
        return frame;
    }

    private static byte[][] Bytes(MacroViewModel macro) => macro.Events.Select(e => e.OriginalProtobufInputEvent.ToByteArray()).ToArray();
    private static void AssertBytes(byte[][] expected, MacroViewModel macro)
    {
        Assert.AreEqual(expected.Length, macro.Events.Count);
        for (var i = 0; i < expected.Length; i++) CollectionAssert.AreEqual(expected[i], macro.Events[i].OriginalProtobufInputEvent.ToByteArray());
    }

    [TestMethod]
    public void ScreenshotRawReleaseKeepsTheRelativeTraceAsContextWithoutASelectedPath()
    {
        using var macro = Macro(Mouse(5, relative: true), Mouse(10, relative: true),
            Mouse(999, MouseActionTypeFlags.LeftUp, 1_890_123, relative: true));
        var before = Bytes(macro);
        var action = macro.Editor.Projection.Actions[^1];
        Assert.AreEqual(ActionKind.Raw, action.Kind);
        Assert.IsFalse(action.Complete);
        var frame = Frame(macro, action);
        Assert.AreEqual(CoordinateSpace.RelativeCounts, frame.Space);
        Assert.AreEqual(new PathPosition(15, 0, CoordinateSpace.RelativeCounts), frame.Destination);
        Assert.IsNotNull(frame.Bounds);
        Assert.IsTrue(frame.Overview.Count > 0);
        Assert.AreEqual(0, frame.SelectedSamples.Count, "A held position is context, not movement by the raw release.");
        Assert.IsFalse(frame.HasSelectedPosition);
        Assert.IsFalse(frame.HasSelectedPath);
        Assert.AreEqual("No pointer movement in this action", frame.SelectionLabel);
        Assert.AreEqual("1 event", action.EventCountLabel);
        Assert.AreEqual("1.89s", action.DisplayTime);
        Assert.AreEqual("1.890123", TimeText.Seconds(action.Wait));
        Assert.AreEqual(BigInteger.Zero, action.Duration);
        Assert.IsFalse(action.CanEditDuration);
        AssertBytes(before, macro);
        macro.Editor.SetWait(action, checked((ulong)TimeText.ParseSeconds("2.345678")));
        macro.Editor.Refresh();
        action = macro.Editor.Projection.Actions[^1];
        Assert.AreEqual(2_345_678UL, action.Wait);
        Assert.AreEqual(BigInteger.Zero, action.Duration);
        Assert.AreEqual(1, action.Count);
        CollectionAssert.AreEqual(before[0], macro.Events[0].OriginalProtobufInputEvent.ToByteArray());
        CollectionAssert.AreEqual(before[1], macro.Events[1].OriginalProtobufInputEvent.ToByteArray());
        Assert.IsTrue(macro.Editor.Undo());
        AssertBytes(before, macro);
    }

    [TestMethod]
    [DataRow("click")]
    [DataRow("keys")]
    [DataRow("scroll")]
    [DataRow("incomplete key")]
    public void StationaryActionsNeverPromoteAnInheritedPositionToSelectedMovement(string kind)
    {
        InputEvent[] selected = kind switch
        {
            "click" => [Mouse(999, MouseActionTypeFlags.LeftDown), Mouse(-999, MouseActionTypeFlags.LeftUp)],
            "keys" => [new KeyboardEvent(VirtualKey.A, false), new KeyboardEvent(VirtualKey.A, true)],
            "scroll" => [Mouse(999, MouseActionTypeFlags.Wheel)],
            _ => [new KeyboardEvent(VirtualKey.A, false)]
        };
        using var macro = Macro([Mouse(-20), Mouse(40), .. selected]);
        var action = macro.Editor.Projection.Actions[^1];
        var frame = Frame(macro, action);
        Assert.AreEqual(0, action.MovementCount);
        Assert.AreEqual(new PathPosition(40, 0, CoordinateSpace.AbsoluteDesktop), frame.Destination);
        Assert.AreEqual(0, frame.SelectedSamples.Count);
        Assert.AreEqual(0, frame.Landmarks.Count, "Stationary click coordinates must not become path markers.");
        Assert.IsFalse(frame.HasSelectedPath);
        Assert.AreEqual("No pointer movement in this action", frame.SelectionLabel);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DragAndMoveHaveDistinctLabelsWithoutAnInheritedClickMarker(bool relative)
    {
        using var macro = Macro(Mouse(10, relative: relative), Mouse(20, relative: relative),
            Mouse(999, MouseActionTypeFlags.LeftDown), Mouse(999, MouseActionTypeFlags.LeftUp),
            Mouse(999, MouseActionTypeFlags.LeftDown), Mouse(30, relative: relative), Mouse(999, MouseActionTypeFlags.LeftUp));
        var actions = macro.Editor.Projection.Actions;
        var move = Frame(macro, actions[0]);
        var click = Frame(macro, actions[1]);
        var drag = Frame(macro, actions[2]);
        Assert.AreEqual("Selected movement", move.SelectionLabel);
        Assert.AreEqual("No pointer movement in this action", click.SelectionLabel);
        Assert.AreEqual("Selected drag", drag.SelectionLabel);
        Assert.IsTrue(drag.HasSelectedPath);
        foreach (var frame in new[] { move, click, drag })
        {
            Assert.AreEqual(1, frame.Landmarks.Count);
            Assert.AreSame(actions[2], frame.Landmarks[0]);
            Assert.IsFalse(frame.Landmarks.Contains(actions[1]));
        }
    }

    [TestMethod]
    public void ButtonPayloadCoordinatesDoNotCreatePositionsOrPaths()
    {
        using var macro = Macro(Mouse(999, MouseActionTypeFlags.LeftDown), Mouse(-999, MouseActionTypeFlags.LeftUp));
        var frame = Frame(macro, macro.Editor.Projection.Actions[0]);
        Assert.IsNull(frame.Destination);
        Assert.IsNull(frame.Bounds);
        Assert.AreEqual(CoordinateSpace.Unknown, frame.Space);
        Assert.AreEqual(0, frame.SelectedSamples.Count);
    }

    [TestMethod]
    public void IncompleteRawMovementHighlightsOnlyItsRecordedSegment()
    {
        using var macro = Macro(Mouse(0), Mouse(10), Mouse(20, MouseActionTypeFlags.Move | MouseActionTypeFlags.LeftUp, 300_000));
        var before = Bytes(macro);
        var action = macro.Editor.Projection.Actions[^1];
        Assert.AreEqual(ActionKind.Raw, action.Kind);
        Assert.IsFalse(action.Complete);
        Assert.AreEqual(1, action.MovementCount);
        var frame = Frame(macro, action);
        Assert.IsTrue(frame.HasSelectedPath);
        Assert.AreEqual("Selected movement", frame.SelectionLabel);
        CollectionAssert.AreEqual(new[] { 1, 2 }, frame.SelectedSamples.Select(sample => sample.Index).ToArray());
        Assert.IsNotNull(frame.GeometryBlockReason, "Showing observed movement must not enable unsafe geometry editing.");
        AssertBytes(before, macro);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IsolatedMovementShowsOnlyItsObservedPositionWithoutAnInventedOrigin(bool relative)
    {
        using var macro = Macro(Mouse(25, relative: relative));
        var frame = Frame(macro, macro.Editor.Projection.Actions[0]);
        Assert.IsTrue(frame.HasSelectedPosition);
        Assert.IsFalse(frame.HasSelectedPath);
        Assert.AreEqual(1, frame.SelectedSamples.Count);
        Assert.AreEqual(25d, frame.SelectedSamples[0].Position!.Value.X);
        Assert.AreEqual("Selected position · no movement path", frame.SelectionLabel);
    }

    [TestMethod]
    public void SelectedMovementDoesNotJoinAnEarlierCoordinateFrame()
    {
        using var macro = Macro(Mouse(100), Mouse(10, relative: true));
        var frame = Frame(macro, macro.Editor.Projection.Actions[^1]);
        Assert.AreEqual(CoordinateSpace.RelativeCounts, frame.Space);
        CollectionAssert.AreEqual(new[] { 1 }, frame.SelectedSamples.Select(sample => sample.Index).ToArray());
        Assert.IsFalse(frame.HasSelectedPath);
        Assert.IsTrue(frame.HasSelectedPosition);
        Assert.AreEqual(0, PathDisplay.Directions(frame.SelectedSamples, frame.Space, 1).Count);
    }

    [TestMethod]
    public void AppendAndRawEditRefreshMovementMetadataWithoutChangingSelection()
    {
        using var macro = Macro(Mouse(10), new KeyboardEvent(VirtualKey.Control, false));
        var editor = macro.Editor;
        var presentation = new EditorPresentation(editor);
        var action = editor.Projection.Actions[^1];
        var refresh = presentation.Refresh(action, [action]);
        Assert.IsTrue(presentation.TryGetFrame(action, out var stationary));
        Assert.AreEqual(0, stationary.SelectedSamples.Count);
        macro.AddEvent(Mouse(40));
        Assert.IsFalse(presentation.TryGetFrame(action, out _));
        refresh = presentation.Refresh(action, refresh.Selection);
        Assert.AreSame(action, refresh.Selection[0]);
        Assert.AreEqual(1, action.MovementCount);
        Assert.IsTrue(presentation.TryGetFrame(action, out var moving));
        Assert.IsTrue(moving.HasSelectedPath);
        var move = macro.Events[^1];
        editor.SelectRawEvents([move]);
        var value = move.OriginalProtobufInputEvent.Clone();
        value.MouseEvent.ActionType = (uint)MouseActionTypeFlags.Wheel;
        editor.EditRaw(move, value);
        refresh = presentation.Refresh(action, refresh.Selection);
        CollectionAssert.AreEqual(new[] { move }, macro.SelectedEvents.ToArray());
        Assert.IsTrue(presentation.TryGetFrame(refresh.Selection[0], out var edited));
        Assert.AreEqual(0, edited.SelectedSamples.Count);
        Assert.AreEqual(0, refresh.Selection[0].MovementCount);
        Assert.IsTrue(editor.Undo());
        refresh = presentation.Refresh(refresh.Selection[0], refresh.Selection);
        Assert.IsTrue(presentation.TryGetFrame(refresh.Selection[0], out var restored));
        Assert.IsTrue(restored.HasSelectedPath);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void WaitAndExecutionEditsKeepMicrosecondsSelectionAndUndoExact(bool rawSelection)
    {
        using var macro = Macro(Mouse(10, delay: 1_890_123), Mouse(20, delay: 101), Mouse(30, delay: 202),
            new KeyboardEvent(VirtualKey.A, false), new KeyboardEvent(VirtualKey.A, true));
        var original = Bytes(macro);
        var editor = macro.Editor;
        var action = editor.Projection.Actions[0];
        editor.SelectActions(editor.Projection.Actions);
        var raw = macro.Events[1];
        if (rawSelection) editor.SelectRawEvents([raw]);
        var selected = macro.SelectedEvents.ToArray();
        Assert.IsTrue(action.CanEditDuration);
        Assert.AreEqual("1.89s pause before + 303µs execution time", action.Summary);
        editor.SetWait(action, checked((ulong)TimeText.ParseSeconds("2.123456")));
        editor.Refresh(); action = editor.Projection.Actions[0];
        Assert.AreEqual(2_123_456UL, action.Wait);
        Assert.AreEqual(new BigInteger(303), action.Duration);
        for (var i = 1; i < original.Length; i++) CollectionAssert.AreEqual(original[i], macro.Events[i].OriginalProtobufInputEvent.ToByteArray());
        var afterWait = Bytes(macro);
        editor.SetDuration(action, TimeText.ParseSeconds("0.000999"));
        editor.Refresh(); action = editor.Projection.Actions[0];
        Assert.AreEqual(new BigInteger(999), action.Duration);
        Assert.AreEqual(2_123_456UL, action.Wait);
        Assert.AreEqual(333UL, macro.Events[1].TimeSinceLastEvent);
        Assert.AreEqual(666UL, macro.Events[2].TimeSinceLastEvent);
        CollectionAssert.AreEqual(selected, macro.SelectedEvents.ToArray());
        Assert.AreEqual(rawSelection, editor.RawSelection);
        Assert.IsTrue(editor.Undo()); AssertBytes(afterWait, macro);
        Assert.IsTrue(editor.Undo()); AssertBytes(original, macro);
        CollectionAssert.AreEqual(selected, macro.SelectedEvents.ToArray());
    }

    [TestMethod]
    [DataRow(0, "0 events")]
    [DataRow(1, "1 event")]
    [DataRow(2, "2 events")]
    public void EventCountsUseTheCorrectSingularAndPlural(int count, string label)
    {
        Assert.AreEqual(label, EditorText.Count(count, "event"));
        Assert.AreEqual(label.Replace("event", "raw event"), EditorText.Count(count, "raw event"));
        using var macro = Macro(Enumerable.Range(0, count).Select(i => (InputEvent)Mouse(i)).ToArray());
        if (count == 0) return;
        var action = macro.Editor.Projection.Actions[0];
        Assert.AreEqual(label, action.EventCountLabel);
        StringAssert.Contains(action.TechnicalSummary, label.Replace("event", "raw event"));
        Assert.AreEqual(count > 1, action.CanEditDuration);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void SparseMotionSurvivesDisplaySamplingForMovesAndDrags(bool drag, bool relative)
    {
        var events = new List<InputEvent> { Mouse(0, relative: relative) };
        if (drag) events.Add(Mouse(999, MouseActionTypeFlags.LeftDown));
        for (var i = 0; i < 1201; i++)
            events.Add(Mouse(i == 1199 ? 100 : relative && i == 1200 ? -100 : 0, relative: relative));
        if (drag) events.Add(Mouse(999, MouseActionTypeFlags.LeftUp));
        using var macro = Macro(events.ToArray());
        var before = Bytes(macro);
        var action = macro.Editor.Projection.Actions[^1];
        var frame = Frame(macro, action);
        var edge = action.MovementEdgeFor(frame.Space);
        Assert.IsNotNull(edge);
        Assert.IsTrue(frame.HasSelectedPath);
        Assert.AreEqual(drag ? "Selected drag" : "Selected movement", frame.SelectionLabel);
        Assert.IsTrue(frame.SelectedSamples.Count <= 512);
        var from = frame.SelectedSamples.Single(sample => sample.Index == edge - 1);
        var to = frame.SelectedSamples.Single(sample => sample.Index == edge);
        Assert.AreEqual(from.Segment, to.Segment);
        Assert.AreNotEqual(from.Position, to.Position);
        Assert.IsFalse(to.StartsSegment);
        Assert.AreEqual(macro.Editor.Projection.Samples[edge!.Value - 1].Position, from.Position);
        Assert.AreEqual(macro.Editor.Projection.Samples[edge.Value].Position, to.Position);
        AssertBytes(before, macro);
    }

    [TestMethod]
    public void MixedActionRetainsIndependentMotionEdgesPerCoordinateFrame()
    {
        var events = new List<InputEvent> { new KeyboardEvent(VirtualKey.Control, false), Mouse(0), Mouse(100) };
        events.AddRange(Enumerable.Range(0, 600).Select(_ => Mouse(100)));
        events.AddRange([Mouse(0, relative: true), Mouse(7, relative: true), Mouse(-7, relative: true)]);
        events.AddRange(Enumerable.Range(0, 600).Select(_ => Mouse(0, relative: true)));
        events.Add(new KeyboardEvent(VirtualKey.Control, true));
        using var macro = Macro(events.ToArray());
        var action = macro.Editor.Projection.Actions.Single();
        var presentation = new EditorPresentation(macro.Editor);
        foreach (var space in new[] { CoordinateSpace.AbsoluteDesktop, CoordinateSpace.RelativeCounts })
        {
            Assert.IsTrue(presentation.TryGetFrame(action, out var frame, space));
            Assert.IsTrue(frame.HasSelectedPath);
            var edge = action.MovementEdgeFor(space)!.Value;
            Assert.IsTrue(frame.SelectedSamples.Any(sample => sample.Index == edge - 1));
            Assert.IsTrue(frame.SelectedSamples.Any(sample => sample.Index == edge));
            Assert.IsTrue(frame.SelectedSamples.Count <= 512);
            foreach (var pair in frame.SelectedSamples.Zip(frame.SelectedSamples.Skip(1)))
                if (pair.First.Segment != pair.Second.Segment) Assert.IsTrue(pair.Second.StartsSegment);
        }
        Assert.IsNull(action.MovementEdgeFor(CoordinateSpace.AbsolutePrimary));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompleteMovementShowsGlobalIncompleteInputWarningUntilTheOtherActionCompletes(bool drag)
    {
        using var macro = drag ? Macro(Mouse(0), Mouse(999, MouseActionTypeFlags.LeftDown), Mouse(100), Mouse(999, MouseActionTypeFlags.LeftUp))
            : Macro(Mouse(0), Mouse(100));
        var editor = macro.Editor;
        var presentation = new EditorPresentation(editor);
        var action = editor.Projection.Actions[^1];
        var refresh = presentation.Refresh(action, [action]);
        Assert.IsTrue(action.Complete);
        Assert.AreEqual("", presentation.InspectorWarning(action));
        macro.AddEvent(new KeyboardEvent(VirtualKey.Control, false));
        refresh = presentation.Refresh(action, refresh.Selection);
        Assert.AreSame(action, refresh.Selection[0]);
        Assert.IsTrue(action.Complete, "The warning concerns another action, not the selected movement.");
        var warning = presentation.InspectorWarning(action);
        Assert.AreEqual(editor.GeometryBlockReason(action), warning);
        StringAssert.Contains(warning, "Incomplete");
        StringAssert.Contains(warning, "Inspect the raw input");
        Assert.AreEqual("Incomplete sequence · see exact input", presentation.InspectorWarning(editor.Projection.Actions[^1]));
        macro.AddEvent(new KeyboardEvent(VirtualKey.Control, true));
        presentation.Refresh(action, refresh.Selection);
        Assert.AreEqual("", presentation.InspectorWarning(action));
        Assert.IsNull(editor.GeometryBlockReason(action));
        Assert.AreEqual("", presentation.InspectorWarning(editor.Projection.Actions[^1]), "Generic key guidance stays in help.");
    }

    [TestMethod]
    public void MovementSafetyWarningsRemainVisibleButStationaryGuidanceStaysInHelp()
    {
        using var macro = Macro(Mouse(10, relative: true), Mouse(20, relative: true),
            Mouse(999, MouseActionTypeFlags.LeftDown), Mouse(999, MouseActionTypeFlags.LeftUp));
        var presentation = new EditorPresentation(macro.Editor);
        var actions = macro.Editor.Projection.Actions;
        Assert.AreEqual(macro.Editor.GeometryBlockReason(actions[0]), presentation.InspectorWarning(actions[0]));
        StringAssert.Contains(presentation.InspectorWarning(actions[0]), "device counts");
        Assert.AreEqual("", presentation.InspectorWarning(actions[1]));
        Assert.AreEqual("", presentation.InspectorWarning(null));
    }

    [TestMethod]
    [DataRow(719d, true)]
    [DataRow(899d, true)]
    [DataRow(900d, false)]
    public void NarrowEditorUsesPaneNavigationBeforeItsActionRowsBecomeCramped(double width, bool singlePane)
    {
        var layout = EditorLayout.Fit(width, 500);
        Assert.AreEqual(singlePane, layout.SinglePane);
        Assert.AreEqual(500d, layout.ViewportHeight);
    }
}
