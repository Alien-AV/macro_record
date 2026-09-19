using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUITests;

[TestClass]
public class EditorPresentationTests
{
    private static MouseEvent Move(int x, int y = 0) => new(x, y, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 1 };
    private static MacroViewModel Macro(params InputEvent[] inputs)
    {
        var macro = new MacroViewModel("presentation", new FakePlaybackEngine());
        foreach (var input in inputs) macro.AddEvent(input);
        return macro;
    }

    [TestMethod]
    [DataRow("clear")]
    [DataRow("add")]
    [DataRow("remove")]
    [DataRow("delay")]
    public void ToolbarMutationThenPassiveRedrawDefersWithoutReplacingProjection(string mutation)
    {
        using var macro = Macro(Move(0), Move(1), Move(2));
        var editor = macro.Editor; var presentation = new EditorPresentation(editor);
        var refresh = presentation.Refresh(null, []); var selected = refresh.Selection[0];
        Assert.IsTrue(presentation.TryGetFrame(selected, out _));
        switch (mutation)
        {
            case "clear": editor.Execute("Clear", macro.Clear); break;
            case "add": macro.CreateKeyboardEventManually(); break;
            case "remove": macro.RemoveSelectedEvents(); break;
            case "delay": macro.ChangeDelaysOnSelected(5000); break;
        }
        for (var redraw = 0; redraw < 3; redraw++) // Resize, theme and sample visibility before the 100 ms tick.
        {
            Assert.IsFalse(presentation.TryGetFrame(selected, out _));
            StringAssert.Contains(editor.GeometryBlockReason(selected)!, "recording changed");
            Assert.IsTrue(editor.IsDirty);
            Assert.AreSame(selected, editor.Projection.Actions[0]);
        }
        refresh = presentation.Refresh(selected, refresh.Selection);
        Assert.IsTrue(presentation.TryGetFrame(refresh.Selection.FirstOrDefault(), out _));
        Assert.IsFalse(presentation.TryGetFrame(selected, out _), "An old reference also stays harmless after rebuilding.");
    }

    [TestMethod]
    public void AppendThenRedrawWaitsForRefreshAndPreservesSelectionIdentity()
    {
        using var macro = Macro(Move(0)); var presentation = new EditorPresentation(macro.Editor);
        var refresh = presentation.Refresh(null, []); var selected = refresh.Selection[0];
        macro.AddEvent(Move(1));
        Assert.IsFalse(presentation.TryGetFrame(selected, out _));
        refresh = presentation.Refresh(selected, refresh.Selection);
        Assert.AreSame(selected, refresh.Selection[0]);
        Assert.AreEqual(1, refresh.ProjectedEvents); Assert.AreEqual(1, refresh.SelectionEventsVisited);
        Assert.IsTrue(presentation.TryGetFrame(selected, out _));
    }

    [TestMethod]
    [DataRow(100000)]
    [DataRow(1000000)]
    public void FullPresentationRefreshOnlyVisitsAppendsAndUsesBoundedDisplayWork(int initialCount)
    {
        using var macro = Macro();
        for (var i = 0; i < initialCount; i++) macro.AddEvent(Move(i));
        var presentation = new EditorPresentation(macro.Editor);
        var refresh = presentation.Refresh(null, []); var selected = refresh.Selection[0];
        Assert.AreEqual(initialCount, refresh.ProjectedEvents); Assert.AreEqual(initialCount, refresh.SelectionEventsVisited);
        for (var batch = 0; batch < 100; batch++)
        {
            const int batchSize = 128;
            for (var i = 0; i < batchSize; i++) macro.AddEvent(Move(initialCount + batch * batchSize + i));
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            refresh = presentation.Refresh(selected, refresh.Selection);
            Assert.IsTrue(presentation.TryGetFrame(selected, out var frame));
            Assert.IsNull(macro.Editor.GeometryBlockReason(selected)); // Inspector's independent eligibility read.
            var viewport = PathViewport.Fit(frame.Bounds!.Value, 400, 300);
            var end = viewport.Map(frame.Destination!.Value);
            Assert.IsTrue(end.X <= 382.000001 && end.Y <= 282.000001);
            Assert.IsTrue(GC.GetAllocatedBytesForCurrentThread() - allocated < 2_000_000,
                "A presentation refresh must not rematerialize the entire event selection or mouse stream.");
            Assert.AreSame(selected, refresh.Selection[0]);
            Assert.AreEqual(batchSize, refresh.ProjectedEvents); Assert.AreEqual(batchSize, refresh.SelectionEventsVisited);
            Assert.AreEqual(initialCount + (batch + 1) * batchSize, macro.SelectedEvents.Count);
            Assert.IsTrue(frame.Overview.Count <= 1200 && frame.SelectedSamples.Count <= 512);
            Assert.IsTrue(presentation.TryGetFrame(selected, out var redraw)); Assert.AreSame(frame, redraw);
        }
    }

    [TestMethod]
    public void PresentationRefreshDoesNotBroadenRawSubsetDuringCapture()
    {
        using var macro = Macro(Move(0), Move(1)); var presentation = new EditorPresentation(macro.Editor);
        var refresh = presentation.Refresh(null, []); var selected = refresh.Selection[0];
        var raw = macro.Events[0]; macro.Editor.SelectRawEvents([raw]);
        for (var batch = 0; batch < 10; batch++)
        {
            macro.AddEvent(Move(batch + 2)); refresh = presentation.Refresh(selected, refresh.Selection);
            Assert.AreEqual(0, refresh.SelectionEventsVisited);
            CollectionAssert.AreEqual(new[] { raw }, macro.SelectedEvents.ToArray());
        }
    }

    [TestMethod]
    public void PendingRawCollapseRestoresCurrentMultiSelectionBeforeChangingScope()
    {
        using var macro = Macro(Move(0), Move(1),
            new KeyboardEvent(Windows.System.VirtualKey.Control, false),
            new KeyboardEvent(Windows.System.VirtualKey.Control, true), Move(2), Move(3));
        var editor = macro.Editor; var presentation = new EditorPresentation(editor);
        presentation.Refresh(null, []);
        RecordedAction[] actions = [editor.Projection.Actions[0], editor.Projection.Actions[2]];
        editor.SelectActions(actions);
        InputEvent[] subset = [macro.Events[0], macro.Events[4]];
        editor.SelectRawEvents(subset);
        macro.Events[0].TimeSinceLastEvent = 2;
        var refresh = presentation.Refresh(actions[0], actions);
        Assert.IsTrue(editor.RawSelection);
        CollectionAssert.AreEqual(subset, macro.SelectedEvents.ToArray());
        Assert.AreEqual(2, refresh.Selection.Count);
        Assert.IsTrue(refresh.Selection.All(editor.Projection.IsCurrent));
        editor.SelectActions(refresh.Selection); // Advanced collapse now switches scope on current actions.
        Assert.IsFalse(editor.RawSelection);
        CollectionAssert.AreEqual(new[] { macro.Events[0], macro.Events[1], macro.Events[4], macro.Events[5] }, macro.SelectedEvents.ToArray());
        Assert.IsTrue(presentation.TryGetFrame(refresh.Selection[0], out _));
    }

    [TestMethod]
    public void UnsampledExtremeStillFitsThePreviewViewport()
    {
        using var macro = Macro(Enumerable.Range(0, 1201).Select(i => (InputEvent)Move(i == 1199 ? 100000 : 10, i == 1199 ? -100000 : 10)).ToArray());
        var presentation = new EditorPresentation(macro.Editor); var refresh = presentation.Refresh(null, []);
        Assert.IsTrue(presentation.TryGetFrame(refresh.Selection[0], out var frame));
        Assert.IsFalse(frame.Overview.Any(p => p.Index == 1199));
        Assert.IsFalse(frame.SelectedSamples.Any(p => p.Index == 1199));
        Assert.AreEqual(new PathBounds(10, -100000, 100000, 10), frame.Bounds);
        var cursor = macro.Editor.Projection.SampleAt(1200)!.Value.Position!.Value;
        var mapped = PathViewport.Fit(frame.Bounds!.Value, 400, 300).Map(cursor);
        Assert.IsTrue(mapped.X >= 18 && mapped.X <= 382 && mapped.Y >= 18 && mapped.Y <= 282);
        ((MouseEvent)macro.Events[1199]).X = 10; ((MouseEvent)macro.Events[1199]).Y = 10;
        refresh = presentation.Refresh(refresh.Selection[0], refresh.Selection);
        Assert.IsTrue(presentation.TryGetFrame(refresh.Selection[0], out frame));
        Assert.AreEqual(new PathBounds(10, 10, 10, 10), frame.Bounds);
    }

    [TestMethod]
    public void FullBoundsStaySeparateForNegativeDesktopPrimaryAndDeviceCountFrames()
    {
        using var macro = Macro(Move(-1920, -1080), Move(300, 200),
            new MouseEvent(10, 20, MouseActionTypeFlags.Move) { MappedToVirtualDesktop = false },
            new MouseEvent(-7, 8, MouseActionTypeFlags.Move) { RelativePosition = true },
            new MouseEvent(2, -3, MouseActionTypeFlags.Move) { RelativePosition = true });
        macro.Editor.Refresh(); var p = macro.Editor.Projection;
        Assert.AreEqual(new PathBounds(-1920, -1080, 300, 200), p.BoundsFor(CoordinateSpace.AbsoluteDesktop));
        Assert.AreEqual(new PathBounds(10, 20, 10, 20), p.BoundsFor(CoordinateSpace.AbsolutePrimary));
        Assert.AreEqual(new PathBounds(-7, 5, -5, 8), p.BoundsFor(CoordinateSpace.RelativeCounts));
        Assert.IsNull(p.BoundsFor(CoordinateSpace.Unknown));
        var hugeCounts = new PathBounds(-1e18, -1e18, 1e18, 1e18);
        var end = PathViewport.Fit(hugeCounts, 400, 300).Map(new(1e18, 1e18, CoordinateSpace.RelativeCounts));
        Assert.IsTrue(end.X <= 382 && end.Y <= 282, "A minimum scale must not expand very large count traces beyond the viewport.");
    }

    [TestMethod]
    public void GeometryMetadataTracksAppendCompletionAndExternalCoordinateEdits()
    {
        using var macro = Macro(Move(0)); var presentation = new EditorPresentation(macro.Editor);
        var refresh = presentation.Refresh(null, []);
        string? Reason()
        {
            refresh = presentation.Refresh(refresh.Selection[0], refresh.Selection);
            Assert.IsTrue(presentation.TryGetFrame(refresh.Selection[0], out var frame));
            return frame.GeometryBlockReason;
        }
        Assert.IsNull(Reason());
        macro.AddEvent(new KeyboardEvent(Windows.System.VirtualKey.Control, false));
        StringAssert.Contains(Reason()!, "Incomplete");
        macro.AddEvent(new KeyboardEvent(Windows.System.VirtualKey.Control, true));
        Assert.IsNull(Reason());
        var relative = new MouseEvent(5, 5, MouseActionTypeFlags.Move) { RelativePosition = true };
        macro.AddEvent(relative); StringAssert.Contains(Reason()!, "device counts");
        relative.RelativePosition = false; Assert.IsNull(Reason());
        relative.MappedToVirtualDesktop = false; StringAssert.Contains(Reason()!, "coordinate frames");
    }

    [TestMethod]
    public void SynchronousModelPopulationDoesNotBecomeADraftButUserChangesDo()
    {
        var drafts = new InspectorDrafts<string>(); var text = "";
        void Populate(string value) => drafts.Populate("duration", () => { text = value; drafts.Changing("duration"); });
        Populate("0.1"); Populate("0.2"); Assert.AreEqual("0.2", text);
        text = "0.333333"; drafts.Changing("duration");
        Populate("0.4"); Assert.AreEqual("0.333333", text);
        drafts.Remove("duration"); Populate("0.5"); Assert.AreEqual("0.5", text);
        drafts.Changing("duration"); drafts.Clear(); Populate("0.6"); Assert.AreEqual("0.6", text);
    }

    [TestMethod]
    public void FailedProgrammaticAssignmentDoesNotSuppressTheNextUserDraft()
    {
        var drafts = new InspectorDrafts<string>();
        Assert.ThrowsExactly<InvalidOperationException>(() => drafts.Populate("x", () => throw new InvalidOperationException()));
        drafts.Changing("x");
        Assert.IsFalse(drafts.Populate("x", () => Assert.Fail("A true draft must survive the next population.")));
    }
}
