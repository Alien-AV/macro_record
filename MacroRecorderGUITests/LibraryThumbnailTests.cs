using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.Views;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class LibraryThumbnailTests
{
    private static LibraryThumbnail Project(params InputEvent[] events)
    {
        var projection = new ActionProjection();
        foreach (var input in events) projection.Append(input);
        return LibraryThumbnail.Create(projection.Samples, events);
    }
    private static MouseEvent Move(int x, int y = 0, bool relative = false) => new(x, y, MouseActionTypeFlags.Move) { RelativePosition = relative };
    private static PathSample Sample(int index, double x, int segment, CoordinateSpace space = CoordinateSpace.RelativeCounts, bool start = false)
        => new(index, index, new(x, 0, space), start, segment);

    [TestMethod]
    public void InitialDesktopAnchorDoesNotHideRelativeMovementOrModifyWireInput()
    {
        InputEvent[] events = [Move(1200, 700), Move(2, 3, true), Move(-5, 4, true), Move(8, -7, true)];
        var wire = SerializeEvents.SerializeEventsToByteArray(events);
        var result = Project(events);
        Assert.IsTrue(result.HasTrace);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, result.Samples.Select(s => s.Index).ToArray());
        Assert.IsTrue(result.Samples.All(s => s.Position!.Value.Space == CoordinateSpace.RelativeCounts));
        Assert.AreEqual(new PathPosition(2, 3, CoordinateSpace.RelativeCounts), result.Samples[0].Position);
        Assert.AreEqual(new PathPosition(5, 0, CoordinateSpace.RelativeCounts), result.Samples[^1].Position);
        StringAssert.Contains(result.Label, "starting position unknown");
        Assert.IsFalse(result.Label.Contains("pixels"));
        CollectionAssert.AreEqual(wire, SerializeEvents.SerializeEventsToByteArray(events));
    }

    [TestMethod]
    public void SegmentSelectionDoesNotCompareCountsWithPixelDistances()
    {
        var result = LibraryThumbnail.Create([
            Sample(0, 0, 1, CoordinateSpace.AbsoluteDesktop, true), Sample(1, 100000, 1, CoordinateSpace.AbsoluteDesktop),
            Sample(2, 1, 2, start: true), Sample(3, 2, 2), Sample(4, 3, 2)
        ], []);
        CollectionAssert.AreEqual(new[] { 2, 3, 4 }, result.Samples.Select(s => s.Index).ToArray());
    }

    [TestMethod]
    public void SeparateRelativeSegmentsNeverJoinThroughAnAbsoluteFrame()
    {
        var result = LibraryThumbnail.Create([
            Sample(0, 1, 1, start: true), Sample(1, 2, 1),
            Sample(2, 400, 2, CoordinateSpace.AbsoluteDesktop, true),
            Sample(3, 10, 3, start: true), Sample(4, 20, 3), Sample(5, 30, 3)
        ], []);
        CollectionAssert.AreEqual(new[] { 3, 4, 5 }, result.Samples.Select(s => s.Index).ToArray());
        Assert.IsTrue(result.Samples.All(s => s.Segment == 3));
    }

    [TestMethod]
    public void MissingPositionsAndExplicitBoundariesDoNotInventMovement()
    {
        var result = LibraryThumbnail.Create([
            Sample(0, 1, 1, start: true), new(1, 1, null, false, 1), Sample(2, 20, 1),
            Sample(3, 50, 1, start: true), Sample(4, 100, 2),
            Sample(5, double.NaN, 2), Sample(6, 800, 2)
        ], []);
        Assert.IsFalse(result.HasTrace);
        Assert.AreEqual(0, result.Samples.Count);
    }

    [TestMethod]
    public void StationaryAndKeyboardSamplesCannotOutvoteBriefMotion()
    {
        var samples = Enumerable.Range(0, 2000).Select(i => Sample(i, 500, 1, CoordinateSpace.AbsoluteDesktop, i == 0)).ToList();
        samples.AddRange([Sample(2000, 1, 2, start: true), Sample(2001, 2, 2), Sample(2002, 4, 2)]);
        var result = LibraryThumbnail.Create(samples, []);
        CollectionAssert.AreEqual(new[] { 2000, 2001, 2002 }, result.Samples.Select(s => s.Index).ToArray());
    }

    [TestMethod]
    public void BoundedTraceKeepsBriefExtremaAndClosedLoopEndpoints()
    {
        var samples = Enumerable.Range(0, 4000).Select(i => Sample(i, i % 2, 1, start: i == 0)).ToArray();
        samples[1999] = Sample(1999, 200, 1);
        samples[^1] = Sample(3999, 0, 1);
        var result = LibraryThumbnail.Create(samples, []);
        Assert.IsTrue(result.Samples.Count <= 160);
        Assert.IsTrue(result.Samples.Any(s => s.Index == 1999));
        Assert.AreEqual(samples[0], result.Samples[0]);
        Assert.AreEqual(samples[^1].Position, result.Samples[^1].Position);
        Assert.IsTrue(result.Samples.Select(s => s.Position).Distinct().Count() > 1);
    }

    [TestMethod]
    public void AbsoluteTraceRetainsItsScreenFrameLabel()
    {
        var first = Move(-500, 20); first.MappedToVirtualDesktop = false;
        var second = Move(-100, 90); second.MappedToVirtualDesktop = false;
        var result = Project(first, second);
        StringAssert.Contains(result.Label, "primary screen pixels");
        Assert.IsTrue(result.Samples.All(s => s.Position!.Value.Space == CoordinateSpace.AbsolutePrimary));
    }

    [TestMethod]
    public void KeyboardFallbackCountsObservedEventsWithoutInferringTypedText()
    {
        var result = Project(new KeyboardEvent(VirtualKey.A, false), new KeyboardEvent(VirtualKey.A, true));
        Assert.IsFalse(result.HasTrace);
        Assert.AreEqual("2 keyboard events", result.InputSummary);
        Assert.AreEqual("No pointer path to show", result.Label);
        var card = new LibraryCard(Guid.NewGuid(), "Keys", "1 action", "Saved", result);
        Assert.IsTrue(card.ArtHeight < new LibraryCard(Guid.NewGuid(), "Move", "", "", Project(Move(0), Move(1))).ArtHeight);
        StringAssert.Contains(card.AccessibleName, "2 keyboard events");
    }

    [TestMethod]
    public void StationaryMixedInputAndEmptyRecordingsHaveHonestFallbacks()
    {
        var result = Project(Move(500, 700), new KeyboardEvent(VirtualKey.B, false), new MouseEvent(0, 0, MouseActionTypeFlags.LeftDown));
        Assert.IsFalse(result.HasTrace);
        Assert.AreEqual("1 keyboard event · 2 mouse events", result.InputSummary);
        Assert.AreEqual("No input recorded", Project().InputSummary);
        Assert.IsFalse(Project().HasTrace);
    }
}
