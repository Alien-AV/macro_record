using System.Globalization;
using System.Numerics;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Utils;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public class EditorPolishTests
{
    [TestMethod]
    [DataRow(0L, "0ms")]
    [DataRow(1L, "1µs")]
    [DataRow(999L, "999µs")]
    [DataRow(1000L, "1ms")]
    [DataRow(278859L, "279ms")]
    [DataRow(999499L, "999ms")]
    [DataRow(999500L, "1s")]
    [DataRow(1000000L, "1s")]
    [DataRow(2438336L, "2.44s")]
    [DataRow(3024275L, "3.02s")]
    public void DisplayTimeIsCompactWhileEditingRemainsExact(long time, string expected)
    {
        Assert.AreEqual(expected, TimeText.Human(time));
        Assert.AreEqual(new BigInteger(time), TimeText.ParseSeconds(TimeText.Seconds(time)));
    }

    [TestMethod]
    public void DisplayAndExactTimeHandleHugeTotalsAndNonEnglishCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var huge = BigInteger.Pow(10, 40) + 1234567;
            Assert.AreEqual("10000000000000000000000000000000001.23s", TimeText.Human(huge));
            Assert.AreEqual(huge, TimeText.ParseSeconds(TimeText.Seconds(huge)));
            Assert.AreEqual("2.44s", TimeText.Human(2438336));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    public void GroupNumbersDoNotExposeRawIndicesAndTechnicalDetailsRemainAvailable()
    {
        var projection = new ActionProjection();
        for (var i = 0; i < 1374; i++)
            projection.Append(new MouseEvent(i, 0, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 10 });
        projection.Append(new KeyboardEvent(VirtualKey.Control, false));
        projection.Append(new KeyboardEvent(VirtualKey.S, false));
        projection.Append(new KeyboardEvent(VirtualKey.S, true));
        projection.Append(new KeyboardEvent(VirtualKey.Control, true));
        projection.FlushNotifications();
        var move = projection.Actions[0]; var keys = projection.Actions[1];
        Assert.AreEqual(3, projection.StepCount);
        Assert.AreEqual("01", move.LeadingDelayNumber);
        Assert.AreEqual("2. Move pointer", move.Title);
        Assert.AreEqual("3. Press Ctrl + S", keys.Title);
        Assert.AreEqual(1374, keys.Start);
        StringAssert.Contains(move.TechnicalSummary, "absolute pixels");
        StringAssert.Contains(keys.TechnicalSummary, "4 raw events");
        Assert.IsFalse(move.Summary.Contains("events"));
        Assert.AreNotEqual(move.Glyph, keys.Glyph);
    }

    [TestMethod]
    public void IncompleteWarningClearsWhenCaptureCompletesAndUnknownKeyStaysExplicit()
    {
        var projection = new ActionProjection();
        projection.Append(new KeyboardEvent((VirtualKey)0xFEDC, false));
        var action = projection.Actions[0];
        Assert.AreEqual("Incomplete", action.Warning);
        StringAssert.Contains(action.WarningExplanation, "Missing or unmatched");
        var notifications = new List<string?>();
        action.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        projection.Append(new KeyboardEvent((VirtualKey)0xFEDC, true));
        projection.FlushNotifications();
        Assert.AreEqual("", action.Warning);
        StringAssert.Contains(action.Name, "VK 0xFEDC");
        CollectionAssert.Contains(notifications, nameof(RecordedAction.WarningVisibility));
        CollectionAssert.Contains(notifications, nameof(RecordedAction.Name));
    }

    [TestMethod]
    [DataRow(false, 120, "Scroll up")]
    [DataRow(false, -120, "Scroll down")]
    [DataRow(true, 120, "Scroll right")]
    [DataRow(true, -120, "Scroll left")]
    [DataRow(true, 0, "Scroll")]
    public void ScrollIntentKeepsAxisAndDirection(bool horizontal, int amount, string expected)
    {
        var projection = new ActionProjection();
        projection.Append(new MouseEvent(0, 0, horizontal ? MouseActionTypeFlags.HorizontalWheel : MouseActionTypeFlags.Wheel)
            { MouseData = unchecked((uint)amount) });
        Assert.AreEqual(expected, projection.Actions[0].Name);
        StringAssert.Contains(projection.Actions[0].Detail, "total wheel units");
    }

    [TestMethod]
    public void ViewportCentersFlatPathsAndRoundTripsNegativeDesktopCoordinates()
    {
        var viewport = PathViewport.Fit(new(-1920, -1080, 0, -1080), 400, 300);
        var start = viewport.Map(new(-1920, -1080, CoordinateSpace.AbsoluteDesktop));
        var end = viewport.Map(new(0, -1080, CoordinateSpace.AbsoluteDesktop));
        Assert.AreEqual(24d, start.X, 0.000001);
        Assert.AreEqual(376d, end.X, 0.000001);
        Assert.AreEqual(150d, start.Y, 0.000001);
        var restored = viewport.Unmap(start.X, start.Y);
        Assert.AreEqual(-1920d, restored.X, 0.000001); Assert.AreEqual(-1080d, restored.Y, 0.000001);
        var point = PathViewport.Fit(new(80, 90, 80, 90), 400, 300).Map(new(80, 90, CoordinateSpace.AbsolutePrimary));
        Assert.AreEqual((200d, 150d), point);
    }

    [TestMethod]
    public void DirectionCuesAreBoundedAndNeverCrossUnknownOrSeparateFrames()
    {
        var samples = Enumerable.Range(0, 512).Select(i =>
            new PathSample(i, i, new PathPosition(i * 100, 0, CoordinateSpace.RelativeCounts), i == 0, 1)).ToArray();
        Assert.AreEqual(8, PathDisplay.Directions(samples, CoordinateSpace.RelativeCounts, 1).Count);
        PathSample[] gaps =
        [
            new(0, 0, new(0, 0, CoordinateSpace.RelativeCounts), true, 1),
            new(1, 1, new(100, 0, CoordinateSpace.RelativeCounts), true, 2),
            new(2, 2, null, false, 2),
            new(3, 3, new(200, 0, CoordinateSpace.RelativeCounts), false, 2),
            new(4, 4, new(300, 0, CoordinateSpace.AbsoluteDesktop), true, 3)
        ];
        Assert.AreEqual(0, PathDisplay.Directions(gaps, CoordinateSpace.RelativeCounts, 1).Count);
        Assert.AreEqual(0, PathDisplay.Directions(samples, CoordinateSpace.AbsoluteDesktop, 1).Count);
    }

    [TestMethod]
    [DataRow(1180d, 600d, false)]
    [DataRow(1180d, 220d, false)]
    [DataRow(900d, 400d, false)]
    [DataRow(600d, 800d, true)]
    [DataRow(600d, 180d, true)]
    public void EditorPanesStayWithinTheViewportAtEveryWidth(
        double width, double height, bool singlePane)
    {
        var layout = EditorLayout.Fit(width, height);
        Assert.AreEqual(singlePane, layout.SinglePane);
        Assert.AreEqual(height, layout.ViewportHeight);
    }

    [TestMethod]
    public void NativeCaptionColorsResetTogetherForHighContrastAndRestoreTheCurrentTheme()
    {
        foreach (var dark in new[] { false, true })
        {
            var normal = TitleBarPalette.ForTheme(dark, false, true);
            Assert.IsNotNull(normal.Background); Assert.IsNotNull(normal.Foreground);
            Assert.IsNotNull(normal.InactiveForeground); Assert.IsNotNull(normal.HoverBackground); Assert.IsNotNull(normal.PressedBackground);
            Assert.AreNotEqual(normal.Foreground, normal.Background);
            Assert.AreNotEqual(normal.InactiveForeground, normal.Background);
            var contrast = TitleBarPalette.ForTheme(dark, true, true);
            Assert.AreEqual(new TitleBarPalette(null, null, null, null, null), contrast);
            Assert.AreEqual(normal, TitleBarPalette.ForTheme(dark, false, true));
            Assert.AreEqual(contrast, TitleBarPalette.ForTheme(dark, false, false), "Windows 10 uses system caption colors.");
        }
        Assert.AreNotEqual(TitleBarPalette.ForTheme(false, false, true), TitleBarPalette.ForTheme(true, false, true));
    }
}
