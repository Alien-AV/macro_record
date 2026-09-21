using MacroRecorderGUI.Models;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitGeometryTests
{
    private sealed class Geometry : IWaitGeometryApi
    {
        public uint Dpi = 96;
        public double BitmapScale = 1;
        public nint Context = -4;
        public int PhysicalTransforms;
        public bool FailTransform;
        public Dictionary<nint, (nint Above, WaitSurface? Surface)> Windows = [];
        public nint WindowContext(nint window) => -1;
        public nint SetContext(nint context) { var previous = Context; Context = context; return previous; }
        public uint WindowDpi(nint window) => Dpi;
        public bool ClientBounds(nint window, out WaitPixelRect bounds) { bounds = new() { Right = 1000, Bottom = 1000 }; return true; }
        public bool ToScreen(nint window, ref WaitPixelPoint point) { point.X -= 500; point.Y += 20; return true; }
        public bool ToPhysical(nint window, ref WaitPixelPoint point)
        { PhysicalTransforms++; point.X = -1000 + (int)Math.Round((point.X + 500) * BitmapScale); point.Y = (int)Math.Round(point.Y * BitmapScale); return !FailTransform; }
        public bool Above(nint window, out nint above) { above = Windows.GetValueOrDefault(window).Above; return true; }
        public WaitSurface? Surface(nint window) => Windows.GetValueOrDefault(window).Surface;
    }
    [TestMethod]
    [DataRow(96u, 2d)]
    [DataRow(120u, 1.6d)]
    [DataRow(192u, 1d)]
    public void LogicalOffsetsUseTargetRenderingTransformForAllDpiAwarenessModes(uint targetDpi, double bitmapScale)
    {
        var api = new Geometry { Dpi = targetDpi, BitmapScale = bitmapScale };
        var condition = new PixelCondition { Coordinates = PixelCoordinates.ClientLogical, ReferenceDpi = 96, X = 100, Y = 100 };
        Assert.IsTrue(WaitPixelGeometry.Resolve(condition, 10, api, out var point));
        Assert.AreEqual(-800, point.X); Assert.AreEqual(1, api.PhysicalTransforms); Assert.AreEqual((nint)(-4), api.Context);
        api.FailTransform = true;
        Assert.IsFalse(WaitPixelGeometry.Resolve(condition, 10, api, out _)); Assert.AreEqual((nint)(-4), api.Context);
    }
    [TestMethod]
    public void ClientPhysicalOffsetsRemainPhysicalAndInvalidBoundsFailWithoutConversion()
    {
        var api = new Geometry { Dpi = 192, BitmapScale = 2 };
        var condition = new PixelCondition { Coordinates = PixelCoordinates.ClientPhysical, X = 100, Y = 100 };
        Assert.IsTrue(WaitPixelGeometry.Resolve(condition, 10, api, out var point));
        Assert.AreEqual(-400, point.X); Assert.AreEqual(0, api.PhysicalTransforms);
        condition.X = 1000; Assert.IsFalse(WaitPixelGeometry.Resolve(condition, 10, api, out _));
    }
    [TestMethod]
    public void OcclusionUsesVisibleGeometryEvenForDisabledWindowsAndRejectsCyclesOrUnknownBounds()
    {
        var api = new Geometry(); var point = new WaitPixelPoint { X = 10, Y = 10 };
        // Disabled target is accepted: input eligibility is not a condition.
        Assert.IsTrue(WaitPixelGeometry.Uncovered(1, point, api));
        api.Windows[1] = (2, null);
        // Disabled opaque overlay still covers the sample.
        api.Windows[2] = (0, new(true, false, false, new WaitPixelRect { Right = 20, Bottom = 20 }));
        Assert.IsFalse(WaitPixelGeometry.Uncovered(1, point, api));
        api.Windows[2] = (0, new(true, false, true, null)); Assert.IsTrue(WaitPixelGeometry.Uncovered(1, point, api));
        api.Windows[2] = (0, new(true, false, false, null)); Assert.IsFalse(WaitPixelGeometry.Uncovered(1, point, api));
        api.Windows[2] = (1, new(false, false, false, null)); Assert.IsFalse(WaitPixelGeometry.Uncovered(1, point, api));
    }
    [TestMethod]
    public void TriggerDescriptionsAndValidationReflectActualComparisonBasis()
    {
        var condition = ConditionalWaitTests.Condition(WaitTrigger.Changes);
        condition.Window.Test = WindowTest.Exists;
        Assert.Throws<ArgumentException>(() => WaitValidation.Validate(condition));
        condition.Window.Test = WindowTest.Visible; WaitValidation.Validate(condition);
        StringAssert.Contains(WaitValidation.Describe(condition), "runtime baseline");
        condition.Trigger = WaitTrigger.NewWindow; StringAssert.Contains(WaitValidation.Describe(condition), "initial matching set");
        condition.Pixel = new() { Rgb = 0x112233, Tolerance = 5 }; condition.Trigger = WaitTrigger.Changes;
        StringAssert.Contains(WaitValidation.Describe(condition), "first valid runtime sample");
        Assert.IsFalse(WaitValidation.Describe(condition).Contains("112233"));
        condition.Trigger = WaitTrigger.BecomesTrue; StringAssert.Contains(WaitValidation.Describe(condition), "after observing false");
    }
}
