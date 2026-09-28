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
        public int ScreenTransforms, SurfaceReads;
        public bool FailTransform;
        public Action? BeforeScreen;
        public Dictionary<nint, (nint Above, WaitSurface? Surface)> Windows = [];
        public nint WindowContext(nint window) => -1;
        public nint SetContext(nint context) { var previous = Context; Context = context; return previous; }
        public uint WindowDpi(nint window) => Dpi;
        public bool ClientBounds(nint window, out WaitPixelRect bounds) { bounds = new() { Right = 1000, Bottom = 1000 }; return true; }
        public bool ToScreen(nint window, ref WaitPixelPoint point) { ScreenTransforms++; BeforeScreen?.Invoke(); point.X -= 500; point.Y += 20; return true; }
        public bool ToPhysical(nint window, ref WaitPixelPoint point)
        { PhysicalTransforms++; point.X = -1000 + (int)Math.Round((point.X + 500) * BitmapScale); point.Y = (int)Math.Round(point.Y * BitmapScale); return !FailTransform; }
        public bool Above(nint window, out nint above) { above = Windows.GetValueOrDefault(window).Above; return true; }
        public WaitSurface? Surface(nint window) { SurfaceReads++; return Windows.GetValueOrDefault(window).Surface; }
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
    [DataRow(0, 0, true)]
    [DataRow(999, 999, true)]
    [DataRow(1000, 0, false)]
    [DataRow(0, 1000, false)]
    [DataRow(-1, 0, false)]
    [DataRow(0, -1, false)]
    public void ClientBoundsIncludeTopLeftAndExcludeBottomRightBeforeConversion(int x, int y, bool expected)
    {
        var api = new Geometry { Context = -2 };
        Assert.AreEqual(expected, WaitPixelGeometry.Resolve(new() { Coordinates = PixelCoordinates.ClientPhysical, X = x, Y = y }, 10, api, out var point));
        Assert.AreEqual(expected ? 1 : 0, api.ScreenTransforms);
        Assert.AreEqual(0, api.PhysicalTransforms);
        if (expected) { Assert.AreEqual(x - 500, point.X); Assert.AreEqual(y + 20, point.Y); }
        Assert.AreEqual((nint)(-2), api.Context);
    }

    [TestMethod]
    [DataRow(0u, 96u, 1)]
    [DataRow(96u, 0u, 1)]
    [DataRow(uint.MaxValue, 48u, int.MaxValue)]
    [DataRow(192u, 96u, 500)]
    public void UnavailableOrOutOfBoundsLogicalCoordinatesNeverReachScreenConversion(uint dpi, uint referenceDpi, int x)
    {
        var api = new Geometry { Dpi = dpi, Context = -2 };
        Assert.IsFalse(WaitPixelGeometry.Resolve(new() { Coordinates = PixelCoordinates.ClientLogical, ReferenceDpi = referenceDpi, X = x }, 10, api, out _));
        Assert.AreEqual(0, api.ScreenTransforms);
        Assert.AreEqual(0, api.PhysicalTransforms);
        Assert.AreEqual((nint)(-2), api.Context);
    }

    [TestMethod]
    public void ThrowingTransformStillRestoresTheCallersDpiContext()
    {
        var failure = new InvalidOperationException("fake transform failure");
        var api = new Geometry { Context = -2, BeforeScreen = () => throw failure };
        Assert.AreSame(failure, Assert.Throws<InvalidOperationException>(() => WaitPixelGeometry.Resolve(
            new() { Coordinates = PixelCoordinates.ClientLogical, ReferenceDpi = 96 }, 10, api, out _)));
        Assert.AreEqual(0, api.PhysicalTransforms);
        Assert.AreEqual((nint)(-2), api.Context);
    }

    [TestMethod]
    [DataRow(9, 15, true)]
    [DataRow(10, 10, false)]
    [DataRow(19, 19, false)]
    [DataRow(20, 15, true)]
    [DataRow(15, 20, true)]
    public void OnlyTheOverlayInteriorCoversAPixel(int x, int y, bool expected)
    {
        var api = new Geometry();
        api.Windows[1] = (2, null);
        api.Windows[2] = (0, new(true, false, false, new WaitPixelRect { Left = 10, Top = 10, Right = 20, Bottom = 20 }));
        Assert.AreEqual(expected, WaitPixelGeometry.Uncovered(1, new() { X = x, Y = y }, api));
    }

    [TestMethod]
    public void HiddenMinimizedAndCloakedSurfacesNeedNoBoundsButAnUnknownSurfaceFails()
    {
        var api = new Geometry();
        api.Windows[1] = (2, null);
        api.Windows[2] = (3, new(false, false, false, null));
        api.Windows[3] = (4, new(true, true, false, null));
        api.Windows[4] = (0, new(true, false, true, null));
        Assert.IsTrue(WaitPixelGeometry.Uncovered(1, new(), api));
        api.Windows[4] = (0, null);
        Assert.IsFalse(WaitPixelGeometry.Uncovered(1, new(), api));
    }

    [TestMethod]
    [DataRow(4096, true)]
    [DataRow(4097, false)]
    public void OcclusionWalkAcceptsItsLimitAndRejectsAnIncompleteTraversal(int count, bool expected)
    {
        var api = new Geometry();
        for (var i = 1; i <= count; i++) api.Windows[i] = (i == count ? 0 : i + 1, new(false, false, false, null));
        Assert.AreEqual(expected, WaitPixelGeometry.Uncovered(1, new(), api));
        Assert.AreEqual(4095, api.SurfaceReads, "Stop at the existing traversal bound without inspecting further windows.");
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
