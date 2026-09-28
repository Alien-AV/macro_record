using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ProtobufGenerated;
using Point = MacroRecorder.Waiting.WaitPixelPoint;
using Rect = MacroRecorder.Waiting.WaitPixelRect;

namespace MacroRecorder.Waiting;

public sealed record ObservedWindow(nint Handle, uint ProcessId, ulong ProcessCreated, string WindowClass,
    string Title, string ExecutablePath, bool Visible, bool Foreground)
{
    public string Identity => $"{Handle:X}:{ProcessId}:{ProcessCreated}";
}
public sealed record WindowObservation(IReadOnlyList<ObservedWindow> Windows, ObservationState? Failure = null, string Detail = "");
public sealed record PixelObservation(uint Rgb, string Identity, string? Unavailable = null);
public interface IWindowPixelDesktop
{
    WindowObservation FindWindows(WindowSelector selector, CancellationToken token);
    PixelObservation ReadPixel(PixelCondition condition, ObservedWindow? window);
}

// Selector resolution and predicates are tested against a fake desktop.
internal sealed class WindowPixelObserver(IWindowPixelDesktop desktop) : IWaitObserver
{
    public ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selector = condition.Window?.Target ?? condition.Pixel?.Target;
        var found = selector is null ? new WindowObservation([]) : desktop.FindWindows(selector, cancellationToken);
        if (found.Failure is { } failure) return ValueTask.FromResult(new WaitObservation(failure, found.Detail));
        if (found.Windows.Count > 1 && selector?.AnyMatch != true)
            return ValueTask.FromResult(new WaitObservation(ObservationState.Error, $"Ambiguous target: {found.Windows.Count} windows match. Refine the selector or choose any matching window."));
        if (condition.Window is { } windowCondition)
        {
            var matching = found.Windows.Where(window => windowCondition.Test switch
            {
                WindowTest.Exists => true, WindowTest.Visible => window.Visible, WindowTest.Foreground => window.Foreground, _ => false
            }).Select(window => window.Identity).ToArray();
            var match = windowCondition.Test == WindowTest.Absent ? found.Windows.Count == 0 : matching.Length > 0;
            return ValueTask.FromResult(new WaitObservation(match ? ObservationState.Match : ObservationState.NoMatch,
                $"{found.Windows.Count} matching window(s); condition {(match ? "true" : "false")}",
                found.Windows.Count == 1 ? found.Windows[0].Identity : "", match ? 1u : 0u,
                found.Windows.Select(w => w.Identity).ToArray(), matching));
        }
        var pixel = condition.Pixel ?? throw new ArgumentException("A pixel condition is required.");
        if (selector is not null && found.Windows.Count == 0)
            return ValueTask.FromResult(new WaitObservation(ObservationState.Unavailable, "Target window is unavailable."));
        var sample = desktop.ReadPixel(pixel, found.Windows.FirstOrDefault());
        if (sample.Unavailable is { } reason) return ValueTask.FromResult(new WaitObservation(ObservationState.Unavailable, reason));
        var equal = WaitEvaluator.ColorsClose(sample.Rgb, pixel.Rgb, pixel.Tolerance);
        return ValueTask.FromResult(new WaitObservation(equal != pixel.NotEqual ? ObservationState.Match : ObservationState.NoMatch,
            $"Observed #{sample.Rgb:X6}", sample.Identity, sample.Rgb));
    }
}

public interface IWaitPixelApi : IWaitGeometryApi
{
    bool Revalidate(ObservedWindow window);
    bool OnMonitor(Point point);
    nint AcquireScreen();
    uint ScreenPixel(nint dc, Point point);
    void ReleaseScreen(nint dc);
}

internal static class WaitPixelSampler
{
    public static PixelObservation Read(PixelCondition condition, ObservedWindow? window, IWaitPixelApi api)
    {
        var previousDpi = api.SetContext(new nint(-4));
        if (previousDpi == 0) return new(0, "", "Physical pixel coordinates are unavailable.");
        try
        {
            var point = new Point { X = condition.X, Y = condition.Y };
            var identity = "desktop";
            if (window is not null)
            {
                if (!api.Revalidate(window) || !window.Visible || api.Surface(window.Handle) is not { Visible: true, Minimized: false, Cloaked: false })
                    return new(0, "", "Target is hidden, minimized, cloaked, or has changed; visibility may be unavailable.");
                if (!WaitPixelGeometry.Resolve(condition, window.Handle, api, out point)) return new(0, "", "Client pixel is out of bounds or its physical rendering transform is unavailable.");
                if (!WaitPixelGeometry.Uncovered(window.Handle, point, api)) return new(0, "", "The target pixel is covered or occlusion cannot be established.");
                identity = window.Identity;
            }
            if (!api.OnMonitor(point)) return new(0, "", "Pixel lies outside a connected monitor.");
            var dc = api.AcquireScreen();
            if (dc == 0) return new(0, "", "Screen sampling is unavailable.");
            uint color;
            try { color = api.ScreenPixel(dc, point); }
            finally { api.ReleaseScreen(dc); }
            if (color == 0xffffffff) return new(0, "", "Pixel read failed.");
            if (window is not null && (!api.Revalidate(window) || api.Surface(window.Handle) is not { Visible: true, Minimized: false, Cloaked: false }
                || !WaitPixelGeometry.Resolve(condition, window.Handle, api, out var after) || after.X != point.X || after.Y != point.Y
                || !WaitPixelGeometry.Uncovered(window.Handle, point, api)))
                return new(0, "", "Target changed or became covered during sampling.");
            return new(((color & 255) << 16) | (color & 0xff00) | ((color >> 16) & 255), identity);
        }
        finally { api.SetContext(previousDpi); }
    }
}

public sealed class WindowsWaitDesktop : IWindowPixelDesktop, IWaitPixelApi
{
    public WindowObservation FindWindows(WindowSelector selector, CancellationToken token)
    {
        var windows = new List<ObservedWindow>();
        var count = 0;
        string? error = null;
        ObservationState failure = ObservationState.Unavailable;
        var completed = EnumWindows((hwnd, parameter) =>
        {
            if (token.IsCancellationRequested) { error = "Observation cancelled."; return false; }
            if (++count > 4096) { error = "Window enumeration exceeded 4096 candidates."; failure = ObservationState.Error; return false; }
            var className = new StringBuilder(1025);
            if (GetClassName(hwnd, className, className.Capacity) == 0) { error = "A candidate window class could not be read."; return false; }
            if (selector.WindowClass.Length > 0 && !string.Equals(selector.WindowClass, className.ToString(), StringComparison.Ordinal)) return true;
            var title = new StringBuilder(32769);
            Marshal.SetLastPInvokeError(0);
            var length = GetWindowText(hwnd, title, title.Capacity);
            if (length == 0 && Marshal.GetLastPInvokeError() != 0) { error = "A candidate window title could not be read."; return false; }
            if (length == title.Capacity - 1) { error = "A candidate window title exceeds the read limit."; return false; }
            var comparison = selector.IgnoreTitleCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (selector.Title.Length > 0 && !(selector.TitleMatch == TitleMatch.Contains ? title.ToString().Contains(selector.Title, comparison)
                : string.Equals(title.ToString(), selector.Title, comparison))) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            using var process = OpenProcess(0x1000, false, pid);
            if (process.IsInvalid || !GetProcessTimes(process, out var created, out _, out _, out _))
            { error = "Access denied or process identity unavailable for a candidate window."; return false; }
            var path = new StringBuilder(32768); var size = (uint)path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref size)) { error = "Executable identity could not be read."; return false; }
            if (selector.ExecutablePath.Length > 0 && !string.Equals(selector.ExecutablePath, path.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
            if (!IsWindow(hwnd) || GetWindowThreadProcessId(hwnd, out var currentPid) == 0 || currentPid != pid)
            { error = "A candidate window changed during observation."; return false; }
            var visible = IsWindowVisible(hwnd) && !IsIconic(hwnd);
            if (DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) != 0) { error = "Window visibility could not be established."; return false; }
            windows.Add(new(hwnd, pid, created, className.ToString(), title.ToString(), path.ToString(), visible && cloaked == 0, GetForegroundWindow() == hwnd));
            return true;
        }, 0);
        token.ThrowIfCancellationRequested();
        return error is not null ? new([], failure, error) : !completed ? new([], ObservationState.Unavailable, "Window enumeration failed.") : new(windows);
    }

    public PixelObservation ReadPixel(PixelCondition condition, ObservedWindow? window) => WaitPixelSampler.Read(condition, window, this);

    bool IWaitPixelApi.Revalidate(ObservedWindow window)
    {
        if (!IsWindow(window.Handle) || GetWindowThreadProcessId(window.Handle, out var pid) == 0 || pid != window.ProcessId) return false;
        using var process = OpenProcess(0x1000, false, pid);
        return !process.IsInvalid && GetProcessTimes(process, out var created, out _, out _, out _) && created == window.ProcessCreated;
    }

    bool IWaitPixelApi.OnMonitor(Point point) => MonitorFromPoint(point, 0) != 0;
    nint IWaitPixelApi.AcquireScreen() => GetDC(0);
    uint IWaitPixelApi.ScreenPixel(nint dc, Point point) => GetPixel(dc, point.X, point.Y);
    void IWaitPixelApi.ReleaseScreen(nint dc) => ReleaseDC(0, dc);

    nint IWaitGeometryApi.WindowContext(nint window) => GetWindowDpiAwarenessContext(window);
    nint IWaitGeometryApi.SetContext(nint context) => SetThreadDpiAwarenessContext(context);
    uint IWaitGeometryApi.WindowDpi(nint window) => GetDpiForWindow(window);
    bool IWaitGeometryApi.ClientBounds(nint window, out Rect rect) => GetClientRect(window, out rect);
    bool IWaitGeometryApi.ToScreen(nint window, ref Point point) => ClientToScreen(window, ref point);
    bool IWaitGeometryApi.ToPhysical(nint window, ref Point point) => LogicalToPhysicalPointForPerMonitorDPI(window, ref point);
    bool IWaitGeometryApi.Above(nint window, out nint above)
    {
        Marshal.SetLastPInvokeError(0);
        above = GetWindow(window, 3);
        return above != 0 || Marshal.GetLastPInvokeError() == 0;
    }
    WaitSurface? IWaitGeometryApi.Surface(nint window)
    {
        if (!IsWindow(window)) return null;
        var visible = IsWindowVisible(window); var minimized = IsIconic(window);
        if (!visible || minimized) return new(visible, minimized, false, null);
        if (DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) != 0) return null;
        return new(visible, minimized, cloaked != 0, GetWindowRect(window, out var bounds) ? bounds : null);
    }
    private delegate bool EnumWindowsCallback(nint hwnd, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] private static extern int GetClassName(nint hwnd, StringBuilder value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW", SetLastError = true)] private static extern int GetWindowText(nint hwnd, StringBuilder value, int size);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out ulong created, out ulong exited, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern nint GetWindowDpiAwarenessContext(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint hwnd, ref Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetWindow(nint hwnd, uint relationship);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
}
