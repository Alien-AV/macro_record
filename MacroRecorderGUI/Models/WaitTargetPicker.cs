using ProtobufGenerated;

namespace MacroRecorderGUI.Models;

internal enum WaitCaptureTarget { FocusedWindow, HoveredWindow, PointerPixel }
internal sealed record CapturedWaitPixel(int X, int Y, uint Rgb);
internal sealed record WaitTargetCapture(WindowSelector? Window = null, CapturedWaitPixel? Pixel = null, string? Error = null)
{
    public bool Succeeded => Error is null && (Window is not null || Pixel is not null);

    public WaitCondition CreateCondition()
    {
        if (!Succeeded) throw new ArgumentException(Error ?? "No target was captured.");
        var result = WaitValidation.NewWindow();
        if (Pixel is { } pixel)
            result.Pixel = new() { Coordinates = PixelCoordinates.DesktopPhysical, X = pixel.X, Y = pixel.Y, Rgb = pixel.Rgb };
        else result.Window.Target = Window!.Clone();
        return result;
    }
}

// Capture is synchronous: the hotkey callback takes its snapshot before any dialog,
// dispatcher hop, or await can change the foreground window or pointer location.
internal interface IWaitTargetPicker
{
    WaitTargetCapture Capture(WaitCaptureTarget target);
}

internal sealed record CapturedWindowIdentity(uint ProcessId, ulong ProcessCreated, string ExecutablePath, string WindowClass, string Title);

internal interface IWaitTargetCaptureApi
{
    nint SetDpiContext(nint context);
    nint ForegroundWindow();
    bool Pointer(out WaitPixelPoint point);
    nint WindowAt(WaitPixelPoint point);
    nint RootWindow(nint window);
    CapturedWindowIdentity Identity(nint window);
    bool Revalidate(nint window, CapturedWindowIdentity identity);
    bool OnMonitor(WaitPixelPoint point);
    nint AcquireScreen();
    uint Pixel(nint dc, WaitPixelPoint point);
    void ReleaseScreen(nint dc);
}

internal sealed class WaitTargetPicker(IWaitTargetCaptureApi api) : IWaitTargetPicker
{
    public WaitTargetCapture Capture(WaitCaptureTarget target)
    {
        if (!Enum.IsDefined(target)) return new(Error: "Choose a supported capture target.");
        var previous = api.SetDpiContext(-4); // Per-monitor aware v2; screen APIs use physical pixels.
        if (previous == 0) return new(Error: "Physical desktop coordinates are unavailable.");
        try
        {
            var point = new WaitPixelPoint();
            if (target != WaitCaptureTarget.FocusedWindow && !api.Pointer(out point))
                return new(Error: "The current pointer position is unavailable.");
            if (target == WaitCaptureTarget.PointerPixel)
            {
                if (!api.OnMonitor(point)) return new(Error: "The pointer is outside a connected display.");
                var dc = api.AcquireScreen();
                if (dc == 0) return new(Error: "Screen sampling is unavailable on this desktop.");
                uint color;
                try { color = api.Pixel(dc, point); }
                finally { api.ReleaseScreen(dc); }
                if (color == uint.MaxValue) return new(Error: "The pixel could not be read. Check desktop access and try again.");
                var rgb = ((color & 255) << 16) | (color & 0xff00) | ((color >> 16) & 255);
                return new(Pixel: new(point.X, point.Y, rgb));
            }
            var window = api.RootWindow(target == WaitCaptureTarget.FocusedWindow ? api.ForegroundWindow() : api.WindowAt(point));
            if (window == 0) return new(Error: "No target window is available.");
            var identity = api.Identity(window);
            if (identity.ProcessId == Environment.ProcessId)
                return new(Error: "Point at another application's window, or invoke the focused-window shortcut while that application has focus.");
            if (!api.Revalidate(window, identity)) return new(Error: "The target window changed during capture. Try again.");
            if (!Path.IsPathFullyQualified(identity.ExecutablePath) || string.IsNullOrWhiteSpace(identity.WindowClass)
                || new[] { identity.ExecutablePath, identity.WindowClass, identity.Title }.Any(s => s.Length > 1024 || s.Contains('\0')))
                return new(Error: "A complete executable path, class, and title could not be captured within the supported limits.");
            return new(Window: new() { ExecutablePath = identity.ExecutablePath, WindowClass = identity.WindowClass,
                Title = identity.Title, TitleMatch = TitleMatch.Exact });
        }
        catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { return new(Error: "Target capture is unavailable. " + error.Message); }
        finally { api.SetDpiContext(previous); }
    }
}
