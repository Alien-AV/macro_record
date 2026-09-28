using MacroRecorderGUI.Models;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitPixelSamplerTests
{
    private static readonly ObservedWindow Target = new(10, 20, 30, "Class", "Export", @"C:\app.exe", true, false);
    private static PixelCondition ClientPixel() => new() { Coordinates = PixelCoordinates.ClientPhysical, X = 4, Y = 5 };

    private sealed class PixelApi : IWaitPixelApi
    {
        public nint Context = -2;
        public bool FailContext, FailBounds, FailScreen, FailPhysical, FailAbove;
        public bool Current = true, Connected = true;
        public nint Dc = 99;
        public uint Color = 0x00332211;
        public int OriginX = -500;
        public WaitSurface? TargetSurface = new(true, false, false, null);
        public WaitSurface? Overlay;
        public Action? DuringRead;
        public List<string> Calls = [];
        public List<nint> Contexts = [];
        public List<WaitPixelPoint> Points = [];

        public bool Revalidate(ObservedWindow window)
        {
            Assert.AreEqual(Target, window);
            Calls.Add("identity");
            return Current;
        }
        public bool OnMonitor(WaitPixelPoint point) { Calls.Add("monitor"); Points.Add(point); return Connected; }
        public nint AcquireScreen() { Calls.Add("acquire"); return Dc; }
        public uint ScreenPixel(nint dc, WaitPixelPoint point)
        {
            Assert.AreEqual(Dc, dc);
            Assert.AreEqual((nint)(-4), Context, "Read pixels under physical DPI awareness.");
            Calls.Add("read"); Points.Add(point);
            DuringRead?.Invoke();
            return Color;
        }
        public void ReleaseScreen(nint dc)
        {
            Assert.AreEqual(Dc, dc);
            Assert.AreEqual((nint)(-4), Context, "Release the DC before restoring the caller's DPI context.");
            Calls.Add("release");
        }
        public nint WindowContext(nint window) => -1;
        public nint SetContext(nint context)
        {
            Contexts.Add(context);
            if (FailContext) return 0;
            var previous = Context; Context = context; return previous;
        }
        public uint WindowDpi(nint window) => 96;
        public bool ClientBounds(nint window, out WaitPixelRect bounds)
        { bounds = new() { Right = 100, Bottom = 100 }; return !FailBounds; }
        public bool ToScreen(nint window, ref WaitPixelPoint point) { point.X += OriginX; return !FailScreen; }
        public bool ToPhysical(nint window, ref WaitPixelPoint point) => !FailPhysical;
        public bool Above(nint window, out nint above)
        { above = window == Target.Handle && Overlay is not null ? 11 : 0; return !FailAbove; }
        public WaitSurface? Surface(nint window) => window == Target.Handle ? TargetSurface : Overlay;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CloakedTargetCannotSupplyAPixelBeforeOrDuringSampling(bool duringRead)
    {
        var api = new PixelApi();
        void Cloak() => api.TargetSurface = new(true, false, true, null);
        if (duringRead) api.DuringRead = Cloak; else Cloak();

        var result = WaitPixelSampler.Read(ClientPixel(), Target, api);

        Assert.IsNotNull(result.Unavailable, "A cloaked window must not report the desktop behind it as its own color.");
        Assert.AreEqual("", result.Identity);
        Assert.AreEqual(duringRead ? 1 : 0, api.Calls.Count(call => call == "read"));
        Assert.AreEqual(duringRead ? 1 : 0, api.Calls.Count(call => call == "release"));
        Assert.AreEqual((nint)(-2), api.Context);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SuccessfulSampleConvertsColorRetainsIdentityAndReleasesScreenBeforeRestoringDpi(bool client)
    {
        var api = new PixelApi();
        var condition = client ? ClientPixel() : new PixelCondition { X = -20, Y = -30 };
        var result = WaitPixelSampler.Read(condition, client ? Target : null, api);

        Assert.IsNull(result.Unavailable);
        Assert.AreEqual(0x112233u, result.Rgb);
        Assert.AreEqual(client ? Target.Identity : "desktop", result.Identity);
        CollectionAssert.AreEqual(client
            ? new[] { "identity", "monitor", "acquire", "read", "release", "identity" }
            : new[] { "monitor", "acquire", "read", "release" }, api.Calls);
        Assert.AreEqual(client ? -496 : -20, api.Points[1].X);
        Assert.AreEqual(client ? 5 : -30, api.Points[1].Y);
        Assert.AreEqual(api.Points[0], api.Points[1]);
        CollectionAssert.AreEqual(client ? new nint[] { -4, -4, -4, -4, -4, -2 } : new nint[] { -4, -2 }, api.Contexts);
        Assert.AreEqual((nint)(-2), api.Context);
    }

    [TestMethod]
    [DataRow("identity")]
    [DataRow("hidden")]
    [DataRow("minimized")]
    [DataRow("visibility unavailable")]
    [DataRow("bounds")]
    [DataRow("screen transform")]
    [DataRow("occlusion unavailable")]
    [DataRow("covered")]
    [DataRow("monitor")]
    public void UnavailableTargetNeverAcquiresScreenAndRestoresDpi(string failure)
    {
        var api = new PixelApi();
        Fail(api, failure);
        var result = WaitPixelSampler.Read(ClientPixel(), Target, api);
        Assert.IsNotNull(result.Unavailable, failure);
        Assert.AreEqual("", result.Identity);
        Assert.IsFalse(api.Calls.Contains("acquire"), failure);
        Assert.AreEqual((nint)(-2), api.Context);
    }

    [TestMethod]
    [DataRow("identity")]
    [DataRow("hidden")]
    [DataRow("minimized")]
    [DataRow("visibility unavailable")]
    [DataRow("bounds")]
    [DataRow("screen transform")]
    [DataRow("moved")]
    [DataRow("occlusion unavailable")]
    [DataRow("covered")]
    public void TargetChangesDuringReadDiscardColorAndReleaseResources(string failure)
    {
        var api = new PixelApi();
        api.DuringRead = () => Fail(api, failure);
        var result = WaitPixelSampler.Read(ClientPixel(), Target, api);
        Assert.IsNotNull(result.Unavailable, failure);
        Assert.AreEqual("", result.Identity);
        Assert.AreEqual(0u, result.Rgb);
        Assert.AreEqual(1, api.Calls.Count(call => call == "read"));
        Assert.AreEqual(1, api.Calls.Count(call => call == "release"));
        Assert.IsTrue(api.Calls.LastIndexOf("identity") > api.Calls.IndexOf("release"));
        Assert.AreEqual((nint)(-2), api.Context);
    }

    private static void Fail(PixelApi api, string failure)
    {
        switch (failure)
        {
            case "identity": api.Current = false; break;
            case "hidden": api.TargetSurface = new(false, false, false, null); break;
            case "minimized": api.TargetSurface = new(true, true, false, null); break;
            case "visibility unavailable": api.TargetSurface = null; break;
            case "bounds": api.FailBounds = true; break;
            case "screen transform": api.FailScreen = true; break;
            case "moved": api.OriginX++; break;
            case "occlusion unavailable": api.FailAbove = true; break;
            case "covered": api.Overlay = new(true, false, false, new WaitPixelRect { Left = -500, Right = 0, Bottom = 100 }); break;
            case "monitor": api.Connected = false; break;
            default: Assert.Fail(failure); break;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LogicalTransformFailureBeforeOrAfterReadRestoresBothDpiContexts(bool duringRead)
    {
        var api = new PixelApi();
        var condition = ClientPixel(); condition.Coordinates = PixelCoordinates.ClientLogical; condition.ReferenceDpi = 96;
        if (duringRead) api.DuringRead = () => api.FailPhysical = true; else api.FailPhysical = true;
        Assert.IsNotNull(WaitPixelSampler.Read(condition, Target, api).Unavailable);
        CollectionAssert.AreEqual(duringRead ? new nint[] { -4, -1, -4, -1, -4, -2 } : new nint[] { -4, -1, -4, -2 }, api.Contexts);
        Assert.AreEqual(duringRead ? 1 : 0, api.Calls.Count(call => call == "release"));
        Assert.AreEqual((nint)(-2), api.Context);
    }

    [TestMethod]
    public void FailedDpiSwitchNeverReadsOrAttemptsToRestoreAnInvalidContext()
    {
        var api = new PixelApi { FailContext = true };
        Assert.IsNotNull(WaitPixelSampler.Read(new(), null, api).Unavailable);
        Assert.AreEqual(0, api.Calls.Count);
        CollectionAssert.AreEqual(new nint[] { -4 }, api.Contexts);
        Assert.AreEqual((nint)(-2), api.Context);
    }

    [TestMethod]
    public void FailedAcquisitionDoesNotReadOrReleaseAnInvalidDc()
    {
        var api = new PixelApi { Dc = 0 };
        Assert.IsNotNull(WaitPixelSampler.Read(new(), null, api).Unavailable);
        CollectionAssert.AreEqual(new[] { "monitor", "acquire" }, api.Calls);
        Assert.AreEqual((nint)(-2), api.Context);
    }

    [TestMethod]
    public void InvalidPixelReleasesDcAndRestoresContextWithoutReturningAColor()
    {
        var api = new PixelApi { Color = uint.MaxValue };
        var result = WaitPixelSampler.Read(new(), null, api);
        Assert.IsNotNull(result.Unavailable);
        Assert.AreEqual(0u, result.Rgb);
        CollectionAssert.AreEqual(new[] { "monitor", "acquire", "read", "release" }, api.Calls);
        Assert.AreEqual((nint)(-2), api.Context);
    }

    [TestMethod]
    public void ThrownReadReleasesDcAndRestoresContextWhilePreservingTheFailure()
    {
        var failure = new InvalidOperationException("fake read failure");
        var api = new PixelApi { DuringRead = () => throw failure };
        Assert.AreSame(failure, Assert.Throws<InvalidOperationException>(() => WaitPixelSampler.Read(new(), null, api)));
        CollectionAssert.AreEqual(new[] { "monitor", "acquire", "read", "release" }, api.Calls);
        Assert.AreEqual((nint)(-2), api.Context);
    }
}
