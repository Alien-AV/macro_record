using MacroRecorderGUI.Models;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitTargetPickerTests
{
    internal sealed class FakeCaptureApi : IWaitTargetCaptureApi
    {
        public readonly List<nint> Contexts = [];
        public WaitPixelPoint Position = new() { X = -1920, Y = -237 };
        public nint Foreground = 11, Hovered = 22, Root = 33, Selected, Context = 96, Dc = 4;
        public bool PointerAvailable = true, Monitor = true, Valid = true, ThrowPixel, Denied;
        public uint Color = 0x00563412;
        public int PointerCalls, Released, Reads;
        public CapturedWindowIdentity Window = new(987654, 123, @"C:\Apps\Example.exe", "DocumentWindow", "Quarterly report");
        public nint SetDpiContext(nint context) { Contexts.Add(context); return Context; }
        public nint ForegroundWindow() => Foreground;
        public bool Pointer(out WaitPixelPoint point) { PointerCalls++; point = Position; return PointerAvailable; }
        public nint WindowAt(WaitPixelPoint point) { Assert.AreEqual(Position.X, point.X); Assert.AreEqual(Position.Y, point.Y); return Hovered; }
        public nint RootWindow(nint window) { Selected = window; return window == 0 ? 0 : Root; }
        public CapturedWindowIdentity Identity(nint window)
        { Assert.AreEqual(Root, window); if (Denied) throw new UnauthorizedAccessException("Access denied"); return Window; }
        public bool Revalidate(nint window, CapturedWindowIdentity identity) => Valid;
        public bool OnMonitor(WaitPixelPoint point) => Monitor;
        public nint AcquireScreen() => Dc;
        public uint Pixel(nint dc, WaitPixelPoint point)
        { Reads++; Assert.AreEqual(Position.X, point.X); Assert.AreEqual(Position.Y, point.Y); if (ThrowPixel) throw new InvalidOperationException("Unavailable"); return Color; }
        public void ReleaseScreen(nint dc) { Assert.AreEqual(Dc, dc); Released++; }
    }

    [TestMethod]
    public void FocusedAndHoveredCommandsSnapshotTheirOwnRootWindowAndPersistOnlySelectors()
    {
        var api = new FakeCaptureApi(); var picker = new WaitTargetPicker(api);
        var focused = picker.Capture(WaitCaptureTarget.FocusedWindow);
        Assert.IsTrue(focused.Succeeded); Assert.AreEqual(api.Foreground, api.Selected); Assert.AreEqual(0, api.PointerCalls);
        api.Foreground = 99;
        var condition = focused.CreateCondition();
        Assert.AreEqual(api.Window.ExecutablePath, condition.Window.Target.ExecutablePath);
        Assert.AreEqual(api.Window.WindowClass, condition.Window.Target.WindowClass);
        Assert.AreEqual(api.Window.Title, condition.Window.Target.Title);
        Assert.AreEqual(TitleMatch.Exact, condition.Window.Target.TitleMatch);
        WaitValidation.Validate(condition);
        var hovered = picker.Capture(WaitCaptureTarget.HoveredWindow);
        Assert.IsTrue(hovered.Succeeded); Assert.AreEqual(api.Hovered, api.Selected); Assert.AreEqual(1, api.PointerCalls);
        condition.Window.Target.Title = "Edited";
        Assert.AreEqual("Quarterly report", focused.Window!.Title);
        CollectionAssert.AreEqual(new nint[] { -4, 96, -4, 96 }, api.Contexts);
    }

    [TestMethod]
    public void PixelUsesSignedPhysicalCoordinatesAndConvertsColorrefToRgbWithoutWindowLookup()
    {
        var api = new FakeCaptureApi();
        var capture = new WaitTargetPicker(api).Capture(WaitCaptureTarget.PointerPixel);
        Assert.AreEqual(new CapturedWaitPixel(-1920, -237, 0x123456), capture.Pixel);
        var condition = capture.CreateCondition();
        Assert.AreEqual(PixelCoordinates.DesktopPhysical, condition.Pixel.Coordinates);
        Assert.IsNull(condition.Pixel.Target); Assert.IsNull(condition.Window);
        Assert.AreEqual(0, (int)api.Selected); Assert.AreEqual(1, api.Released);
        WaitValidation.Validate(condition);
    }

    [TestMethod]
    public void FailuresReleaseScreenAndRestoreDpiWithoutPartialDraft()
    {
        foreach (var throwing in new[] { false, true })
        {
            var api = new FakeCaptureApi { Color = uint.MaxValue, ThrowPixel = throwing };
            var capture = new WaitTargetPicker(api).Capture(WaitCaptureTarget.PointerPixel);
            Assert.IsFalse(capture.Succeeded); Assert.IsNotNull(capture.Error); Assert.AreEqual(1, api.Released);
            CollectionAssert.AreEqual(new nint[] { -4, 96 }, api.Contexts);
            Assert.ThrowsExactly<ArgumentException>(() => capture.CreateCondition());
        }
    }

    [TestMethod]
    public void UnavailableDesktopOrPermissionNeverFallsBackToAnUnrelatedWindow()
    {
        foreach (var api in new[] { new FakeCaptureApi { Denied = true }, new FakeCaptureApi { Valid = false },
            new FakeCaptureApi { Foreground = 0 }, new FakeCaptureApi { Window = new((uint)Environment.ProcessId, 0, @"C:\Macro.exe", "Own", "Own") } })
        {
            var capture = new WaitTargetPicker(api).Capture(WaitCaptureTarget.FocusedWindow);
            Assert.IsFalse(capture.Succeeded); Assert.AreEqual(0, api.PointerCalls); Assert.IsNull(capture.Window);
            Assert.AreEqual((nint)96, api.Contexts.Last());
        }
        foreach (var api in new[] { new FakeCaptureApi { Context = 0 }, new FakeCaptureApi { PointerAvailable = false },
            new FakeCaptureApi { Monitor = false }, new FakeCaptureApi { Dc = 0 } })
        {
            Assert.IsFalse(new WaitTargetPicker(api).Capture(WaitCaptureTarget.PointerPixel).Succeeded);
            Assert.AreEqual(0, api.Reads);
        }
    }
}
