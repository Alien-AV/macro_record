using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Google.Protobuf;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public class VisualPreviewTests
{
    private static KeyboardEvent Key(VirtualKey key, bool up, ulong delay) => new(key, up) { TimeSinceLastEvent = delay };
    private static MouseEvent Mouse(MouseActionTypeFlags flags, ulong delay, int x = 999, int y = -999, bool relative = false)
        => new(x, y, flags) { TimeSinceLastEvent = delay, RelativePosition = relative, MappedToVirtualDesktop = false };
    private static (ActionProjection Projection, VisualPreview Preview) Project(params InputEvent[] events)
    {
        var projection = new ActionProjection();
        foreach (var input in events) projection.Append(input);
        projection.FlushNotifications();
        return (projection, new VisualPreview(events, projection));
    }

    [TestMethod]
    public void WaitsAndHeldKeysFollowExactTransitionsIncludingBackwardsScrubs()
    {
        var (projection, preview) = Project(Key(VirtualKey.Control, false, 10), Key(VirtualKey.L, false, 20),
            Key(VirtualKey.L, true, 10), Key(VirtualKey.Control, true, 10), Key(VirtualKey.Enter, false, 100), Key(VirtualKey.Enter, true, 10));
        var before = preview.Seek(9);
        Assert.IsTrue(before.Waiting); Assert.HasCount(0, before.HeldKeys); Assert.IsNull(before.Pointer);
        CollectionAssert.AreEqual(new uint[] { (uint)VirtualKey.Control }, preview.Seek(10).HeldKeys.ToArray());
        CollectionAssert.AreEqual(new uint[] { (uint)VirtualKey.Control, (uint)VirtualKey.L }, preview.Seek(39).HeldKeys.ToArray());
        CollectionAssert.AreEqual(new uint[] { (uint)VirtualKey.Control }, preview.Seek(40).HeldKeys.ToArray());
        var wait = preview.Seek(50);
        Assert.IsTrue(wait.Waiting); Assert.AreSame(projection.Actions[1], wait.Current); Assert.HasCount(0, wait.HeldKeys);
        CollectionAssert.AreEqual(new uint[] { (uint)VirtualKey.Enter }, preview.Seek(150).HeldKeys.ToArray());
        Assert.HasCount(0, preview.Seek(160).HeldKeys);
        CollectionAssert.AreEqual(new uint[] { (uint)VirtualKey.Control }, preview.Seek(10).HeldKeys.ToArray());
        Assert.AreEqual(new BigInteger(10), preview.NextActionTime(0));
        Assert.AreEqual(new BigInteger(50), preview.NextActionTime(10));
        Assert.AreEqual(new BigInteger(150), preview.NextActionTime(50));
    }

    [TestMethod]
    public void ZeroTimeTransitionsAreAppliedInOrderAndIncompleteHoldsRemainVisibleAtEnd()
    {
        var (_, preview) = Project(Key(VirtualKey.A, false, 0), Key(VirtualKey.A, true, 0),
            Key((VirtualKey)0xFEDC, false, 0), Mouse(MouseActionTypeFlags.LeftDown, 0));
        var end = preview.Seek(0);
        CollectionAssert.AreEqual(new uint[] { 0xFEDC }, end.HeldKeys.ToArray());
        CollectionAssert.AreEqual(new[] { "Left mouse" }, end.HeldButtons.ToArray());
        Assert.IsFalse(end.Current!.Complete);
        Assert.IsNull(end.Pointer!.Value.Position, "Button payload coordinates do not create a pointer position.");
    }

    [TestMethod]
    public void PointerHoldsAtSamplesAndNeverInterpolatesOrConvertsRelativeCounts()
    {
        var (_, preview) = Project(Mouse(MouseActionTypeFlags.LeftUp, 10),
            Mouse(MouseActionTypeFlags.Move, 20, -25, 40), Mouse(MouseActionTypeFlags.Move, 100, 200, 300),
            Mouse(MouseActionTypeFlags.Move, 10, 2, -3, relative: true));
        Assert.IsNull(preview.Seek(29).Pointer!.Value.Position);
        Assert.AreEqual(new PathPosition(-25, 40, CoordinateSpace.AbsolutePrimary), preview.Seek(129).Pointer!.Value.Position);
        Assert.AreEqual(new PathPosition(200, 300, CoordinateSpace.AbsolutePrimary), preview.Seek(130).Pointer!.Value.Position);
        Assert.AreEqual(new PathPosition(2, -3, CoordinateSpace.RelativeCounts), preview.Seek(140).Pointer!.Value.Position);
    }

    [TestMethod]
    public void PreviewFrameCanShowCurrentCoordinateFrameInsideMixedAction()
    {
        using var macro = new MacroViewModel("mixed", new FakePlaybackEngine());
        foreach (var e in new InputEvent[] { Key(VirtualKey.Control, false, 10), Mouse(MouseActionTypeFlags.Move, 10, 2, 3, true), Mouse(MouseActionTypeFlags.Move, 10, 200, 300), Key(VirtualKey.Control, true, 10) }) macro.AddEvent(e);
        macro.Editor.Refresh();
        var presentation = new EditorPresentation(macro.Editor);
        var selected = macro.Editor.Projection.Actions[0];
        Assert.IsTrue(presentation.TryGetFrame(selected, out var relative, CoordinateSpace.RelativeCounts));
        Assert.AreEqual(CoordinateSpace.RelativeCounts, relative.Space);
        Assert.AreEqual(2d, relative.Bounds!.Value.MinX);
        Assert.IsTrue(presentation.TryGetFrame(selected, out var absolute, CoordinateSpace.AbsolutePrimary));
        Assert.AreEqual(CoordinateSpace.AbsolutePrimary, absolute.Space);
        Assert.AreEqual(200d, absolute.Bounds!.Value.MinX);
    }

    [TestMethod]
    public void TimelinePartitionsAllOriginalTimingIncludingLargeDelaysAndGroupedRecordings()
    {
        var inputs = Enumerable.Range(0, 1200).SelectMany(_ => new InputEvent[]
            { Key(VirtualKey.A, false, ulong.MaxValue), Key(VirtualKey.A, true, 17) }).ToArray();
        var (projection, preview) = Project(inputs);
        var segments = preview.Segments();
        Assert.IsTrue(segments.Count <= 256);
        Assert.AreEqual(BigInteger.Zero, segments[0].Start);
        Assert.AreEqual(projection.TotalTime, segments[^1].End);
        Assert.AreEqual(projection.TotalTime, segments.Aggregate(BigInteger.Zero, (sum, s) => sum + s.End - s.Start));
        for (var i = 1; i < segments.Count; i++) Assert.AreEqual(segments[i - 1].End, segments[i].Start);
        var small = Project(Key(VirtualKey.A, false, ulong.MaxValue), Key(VirtualKey.A, true, 17)).Preview.Segments();
        Assert.IsTrue(small[0].Waiting); Assert.IsFalse(small[1].Waiting);
        Assert.AreEqual(new BigInteger(ulong.MaxValue), small[0].End);
    }

    [TestMethod]
    public void EmptyAndZeroDurationRecordingsAreTruthfulAndSeekable()
    {
        var empty = Project().Preview;
        Assert.IsNull(empty.Seek(100).Current); Assert.IsNull(empty.Seek(100).Next);
        Assert.HasCount(0, empty.Segments()); Assert.AreEqual(BigInteger.Zero, empty.NextActionTime(100));
        var instant = Project(Key(VirtualKey.A, false, 0), Key(VirtualKey.A, true, 0)).Preview;
        Assert.IsNotNull(instant.Seek(0).Current); Assert.HasCount(0, instant.Seek(0).HeldKeys);
        Assert.HasCount(1, instant.Segments());
    }

    [TestMethod]
    public void PreviewNeverMutatesWireBytesOrInvokesPlayback()
    {
        var engine = new FakePlaybackEngine();
        using var macro = new MacroViewModel("preview", engine);
        macro.AddEvent(Mouse(MouseActionTypeFlags.Move, 1, -400, 50));
        macro.AddEvent(Key(VirtualKey.A, false, ulong.MaxValue));
        macro.AddEvent(Key(VirtualKey.A, true, 1));
        macro.Editor.Refresh();
        var original = macro.Events.Select(e => e.OriginalProtobufInputEvent.ToByteArray()).ToArray();
        var preview = new VisualPreview(macro.Events, macro.Editor.Projection);
        foreach (var time in new[] { BigInteger.Zero, BigInteger.One, new BigInteger(ulong.MaxValue), macro.Editor.Projection.TotalTime, BigInteger.Zero }) preview.Seek(time);
        preview.Segments();
        for (var i = 0; i < original.Length; i++) CollectionAssert.AreEqual(original[i], macro.Events[i].OriginalProtobufInputEvent.ToByteArray());
        Assert.AreEqual(0, engine.Starts); Assert.AreEqual(0, engine.Aborts); Assert.IsFalse(macro.Editor.CanUndo);
    }

    [TestMethod]
    public void DesktopEditorMustNotSubscribeToUnsafeAccessibilityEvent()
    {
        var gui = Path.Combine(SourceDirectory(), "..", "MacroRecorderGUI");
        var sources = Directory.EnumerateFiles(Path.Combine(gui, "Editor"), "*.cs")
            .Append(Path.Combine(gui, "Views", "MacroTabContent.xaml.cs"));
        foreach (var source in sources)
            Assert.IsFalse(Regex.IsMatch(File.ReadAllText(source), @"\.HighContrastChanged\s*[+-]="),
                $"{source}: the unpackaged desktop event can throw 0x80070490. Use the host's DesktopThemeMonitor and RefreshTheme().");
    }
    private static string SourceDirectory([CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;
}
