using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class OcrRegionCaptureTests
{
    private sealed class Screen : IOcrScreenApi
    {
        public nint Context = -2;
        public uint Dpi = 96;
        public double Scale = 1;
        public bool Current = true, Monitor = true, FailCapture, Overlay;
        public int Captures;
        public Action? DuringCapture;
        public int Offset;
        public bool Revalidate(ObservedWindow window) => Current;
        public bool OnMonitor(WaitPixelPoint point) => Monitor;
        public nint AcquireScreen() => throw new AssertFailedException("No real DC required.");
        public uint ScreenPixel(nint dc, WaitPixelPoint point) => throw new AssertFailedException("No pixel fallback.");
        public void ReleaseScreen(nint dc) => throw new AssertFailedException();
        public nint WindowContext(nint window) => -1;
        public nint SetContext(nint value) { var previous = Context; Context = value; return previous; }
        public uint WindowDpi(nint window) => Dpi;
        public bool ClientBounds(nint window, out WaitPixelRect bounds) { bounds = new() { Right = 4096, Bottom = 4096 }; return true; }
        public bool ToScreen(nint window, ref WaitPixelPoint point) { point.X += Offset; return true; }
        public bool ToPhysical(nint window, ref WaitPixelPoint point) { point.X = (int)(point.X * Scale); point.Y = (int)(point.Y * Scale); return true; }
        public bool Above(nint window, out nint above) { above = Overlay && window == 1 ? 2 : 0; return true; }
        public WaitSurface? Surface(nint window) => new(true, false, false, window == 2
            ? new WaitPixelRect { Left = 40, Top = 40, Right = 60, Bottom = 60 }
            : new WaitPixelRect { Right = 4096, Bottom = 4096 });
        public bool RegionOnMonitor(WaitPixelRect region) => Monitor;
        public byte[]? CaptureRegion(WaitPixelRect region)
        {
            Captures++; DuringCapture?.Invoke();
            return FailCapture ? null : new byte[(region.Right - region.Left) * (region.Bottom - region.Top) * 4];
        }
    }
    private static ObservedWindow Window => new(1, 12, 13, "fake", "fake", "C:\\fake.exe", true, false);
    private static OcrRegion Region => new() { Coordinates = PixelCoordinates.ClientPhysical, Target = new() { Title = "fake" }, Width = 100, Height = 100 };

    [TestMethod]
    public void OcclusionAnywhereInsideRegionBlocksCapture()
    {
        var api = new Screen { Overlay = true }; var capture = new WindowsOcrCapture(api);
        Assert.AreEqual(ReadStatus.Unavailable, capture.Capture(Region, Window, default).Status);
        Assert.AreEqual(0, api.Captures); Assert.AreEqual((nint)(-2), api.Context);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    public void MovementIdentityOcclusionAndCaptureFailuresRejectEntireSample(int kind)
    {
        var api = new Screen(); var capture = new WindowsOcrCapture(api);
        api.DuringCapture = () => { if (kind == 0) api.Offset = 1; if (kind == 1) api.Current = false; if (kind == 2) api.Overlay = true; };
        api.FailCapture = kind == 3;
        Assert.AreEqual(ReadStatus.Unavailable, capture.Capture(Region, Window, default).Status);
        Assert.AreEqual(1, api.Captures); Assert.AreEqual((nint)(-2), api.Context);
    }

    [TestMethod]
    public void LogicalRegionUsesExclusiveBottomRightAndRechecksPhysicalArea()
    {
        var api = new Screen { Scale = 2 }; var region = Region;
        region.Coordinates = PixelCoordinates.ClientLogical; region.ReferenceDpi = 96;
        var capture = new WindowsOcrCapture(api);
        var result = capture.Capture(region, Window, default);
        Assert.AreEqual(ReadStatus.Success, result.Status);
        Assert.AreEqual(200, result.Frame!.Width); Assert.AreEqual(200, result.Frame.Height);
        region.Width = 1000; region.Height = 1000;
        Assert.AreEqual(ReadStatus.Unavailable, capture.Capture(region, Window, default).Status);
        Assert.AreEqual(1, api.Captures);
    }

    [TestMethod]
    public void NegativeDesktopRegionsDoNotUseClientTransformsAndCannotCrossMonitorGaps()
    {
        var api = new Screen(); var capture = new WindowsOcrCapture(api);
        var region = new OcrRegion { X = -100, Y = -100, Width = 10, Height = 10 };
        Assert.AreEqual(ReadStatus.Success, capture.Capture(region, null, default).Status);
        api.Monitor = false;
        Assert.AreEqual(ReadStatus.Unavailable, capture.Capture(region, null, default).Status);
        Assert.AreEqual(1, api.Captures);
    }
}
