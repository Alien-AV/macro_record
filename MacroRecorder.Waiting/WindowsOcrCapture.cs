using System.Runtime.InteropServices;
using ProtobufGenerated;

namespace MacroRecorder.Waiting;

internal interface IOcrScreenApi : IWaitPixelApi
{
    bool RegionOnMonitor(WaitPixelRect region);
    byte[]? CaptureRegion(WaitPixelRect region);
}

internal sealed class WindowsOcrCapture(IOcrScreenApi? api = null) : IOcrCapture
{
    private readonly IOcrScreenApi _api = api ?? new WindowsOcrScreenApi();

    public OcrCaptureResult Capture(OcrRegion region, ObservedWindow? window, CancellationToken token)
    {
        WaitValidation.Region(region);
        token.ThrowIfCancellationRequested();
        var previous = _api.SetContext(new nint(-4));
        if (previous == 0) return new(ReadStatus.Unavailable, Detail: "Physical screen coordinates are unavailable.");
        try
        {
            if (!Resolve(region, window, out var rect)) return new(ReadStatus.Unavailable, Detail: "OCR region is hidden, covered, outside its client/monitor, or unavailable.");
            var width = (long)rect.Right - rect.Left; var height = (long)rect.Bottom - rect.Top;
            if (width is < 1 or > 4096 || height is < 1 or > 4096 || width * height > 1_000_000)
                return new(ReadStatus.Unavailable, Detail: "The physical OCR region exceeds one megapixel or its dimension limit.");
            var bytes = _api.CaptureRegion(rect);
            token.ThrowIfCancellationRequested();
            if (bytes is null || bytes.Length != width * height * 4) return new(ReadStatus.Unavailable, Detail: "OCR screen capture failed or was incomplete.");
            if (!Resolve(region, window, out var after) || !rect.Equals(after)) return new(ReadStatus.Unavailable, Detail: "OCR target moved or became covered during capture.");
            return new(ReadStatus.Success, new((int)width, (int)height, bytes, window?.Identity ?? "desktop"));
        }
        finally { _api.SetContext(previous); }
    }

    private bool Resolve(OcrRegion region, ObservedWindow? window, out WaitPixelRect rect)
    {
        rect = default;
        if (region.Coordinates == PixelCoordinates.DesktopPhysical)
            rect = new() { Left = region.X, Top = region.Y, Right = checked(region.X + (int)region.Width), Bottom = checked(region.Y + (int)region.Height) };
        else
        {
            if (window is null || !_api.Revalidate(window) || _api.Surface(window.Handle) is not { Visible: true, Minimized: false, Cloaked: false }
                || !WaitPixelGeometry.ResolveRegion(region, window.Handle, _api, out rect) || !WaitPixelGeometry.RegionUncovered(window.Handle, rect, _api)) return false;
        }
        return _api.RegionOnMonitor(rect);
    }
}

internal sealed class WindowsOcrScreenApi : IOcrScreenApi
{
    private readonly IWaitPixelApi _desktop = new WindowsWaitDesktop();
    public bool Revalidate(ObservedWindow window) => _desktop.Revalidate(window);
    public bool OnMonitor(WaitPixelPoint point) => _desktop.OnMonitor(point);
    public nint AcquireScreen() => _desktop.AcquireScreen();
    public uint ScreenPixel(nint dc, WaitPixelPoint point) => _desktop.ScreenPixel(dc, point);
    public void ReleaseScreen(nint dc) => _desktop.ReleaseScreen(dc);
    public nint WindowContext(nint window) => _desktop.WindowContext(window);
    public nint SetContext(nint context) => _desktop.SetContext(context);
    public uint WindowDpi(nint window) => _desktop.WindowDpi(window);
    public bool ClientBounds(nint window, out WaitPixelRect bounds) => _desktop.ClientBounds(window, out bounds);
    public bool ToScreen(nint window, ref WaitPixelPoint point) => _desktop.ToScreen(window, ref point);
    public bool ToPhysical(nint window, ref WaitPixelPoint point) => _desktop.ToPhysical(window, ref point);
    public bool Above(nint window, out nint above) => _desktop.Above(window, out above);
    public WaitSurface? Surface(nint window) => _desktop.Surface(window);

    public bool RegionOnMonitor(WaitPixelRect region)
    {
        var monitor = MonitorFromRect(ref region, 0);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        // Conservatively require one monitor to contain the entire region; no
        // unobserved holes between monitors can masquerade as absent text.
        return monitor != 0 && GetMonitorInfo(monitor, ref info) && region.Left >= info.Monitor.Left && region.Top >= info.Monitor.Top
            && region.Right <= info.Monitor.Right && region.Bottom <= info.Monitor.Bottom;
    }

    public byte[]? CaptureRegion(WaitPixelRect region)
    {
        var width = checked(region.Right - region.Left); var height = checked(region.Bottom - region.Top);
        var screen = AcquireScreen(); if (screen == 0) return null;
        nint dc = 0, bitmap = 0, old = 0;
        try
        {
            dc = CreateCompatibleDC(screen); if (dc == 0) return null;
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(screen, ref info, 0, out var bits, 0, 0);
            if (bitmap == 0 || bits == 0) return null;
            old = SelectObject(dc, bitmap); if (old == 0 || old == new nint(-1)) return null;
            if (!BitBlt(dc, 0, 0, width, height, screen, region.Left, region.Top, 0x40cc0020)) return null;
            if (!GdiFlush()) return null;
            var bytes = new byte[checked(width * height * 4)];
            Marshal.Copy(bits, bytes, 0, bytes.Length);
            // BI_RGB's fourth byte is unused, but OCR image decoders may treat
            // it as alpha. Supply fully opaque pixels in the in-memory BMP.
            for (var index = 3; index < bytes.Length; index += 4) bytes[index] = 255;
            return bytes;
        }
        finally
        {
            if (old != 0 && old != new nint(-1)) SelectObject(dc, old);
            if (bitmap != 0) DeleteObject(bitmap);
            if (dc != 0) DeleteDC(dc);
            ReleaseScreen(screen);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public WaitPixelRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XPels, YPels; public uint Used, Important; }
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref WaitPixelRect rect, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint value);
}
