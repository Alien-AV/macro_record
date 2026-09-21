using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ProtobufGenerated;

namespace MacroRecorderGUI.Models;

internal sealed record ObservedWindow(nint Handle, uint ProcessId, ulong ProcessCreated, string WindowClass,
    string Title, string ExecutablePath, bool Visible, bool Foreground)
{
    public string Identity => $"{Handle:X}:{ProcessId}:{ProcessCreated}";
}
internal sealed record WindowObservation(IReadOnlyList<ObservedWindow> Windows, ObservationState? Failure = null, string Detail = "");
internal sealed record PixelObservation(uint Rgb, string Identity, string? Unavailable = null);
internal interface IWindowPixelDesktop
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

internal sealed class WindowsWaitDesktop : IWindowPixelDesktop
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

    public PixelObservation ReadPixel(PixelCondition condition, ObservedWindow? window)
    {
        var previousDpi = SetThreadDpiAwarenessContext(new nint(-4));
        if (previousDpi == 0) return new(0, "", "Physical pixel coordinates are unavailable.");
        try
        {
            var point = new Point { X = condition.X, Y = condition.Y };
            var identity = "desktop";
            if (window is not null)
            {
                if (!Revalidate(window) || !window.Visible || IsIconic(window.Handle)) return new(0, "", "Target is hidden, minimized, or has changed.");
                if (!GetClientRect(window.Handle, out var bounds)) return new(0, "", "Client bounds are unavailable.");
                if (condition.Coordinates == PixelCoordinates.ClientLogical)
                {
                    var dpi = GetDpiForWindow(window.Handle);
                    if (dpi == 0) return new(0, "", "Target DPI is unavailable.");
                    var x = Math.Round((double)point.X * dpi / condition.ReferenceDpi);
                    var y = Math.Round((double)point.Y * dpi / condition.ReferenceDpi);
                    if (x > int.MaxValue || y > int.MaxValue) return new(0, "", "Scaled client coordinates overflow.");
                    point.X = (int)x; point.Y = (int)y;
                }
                if (point.X < bounds.Left || point.Y < bounds.Top || point.X >= bounds.Right || point.Y >= bounds.Bottom)
                    return new(0, "", "Pixel lies outside the current client bounds.");
                if (!ClientToScreen(window.Handle, ref point)) return new(0, "", "Client position is unavailable.");
                if (GetAncestor(WindowFromPoint(point), 2) != window.Handle) return new(0, "", "The target pixel is covered by another window.");
                identity = window.Identity;
            }
            if (MonitorFromPoint(point, 0) == 0) return new(0, "", "Pixel lies outside a connected monitor.");
            var dc = GetDC(0);
            if (dc == 0) return new(0, "", "Screen sampling is unavailable.");
            uint color;
            try { color = GetPixel(dc, point.X, point.Y); }
            finally { ReleaseDC(0, dc); }
            if (color == 0xffffffff) return new(0, "", "Pixel read failed.");
            if (window is not null && (!Revalidate(window) || GetAncestor(WindowFromPoint(point), 2) != window.Handle))
                return new(0, "", "Target changed or became covered during sampling.");
            return new(((color & 255) << 16) | (color & 0xff00) | ((color >> 16) & 255), identity);
        }
        finally { SetThreadDpiAwarenessContext(previousDpi); }
    }

    private static bool Revalidate(ObservedWindow window)
    {
        if (!IsWindow(window.Handle) || GetWindowThreadProcessId(window.Handle, out var pid) == 0 || pid != window.ProcessId) return false;
        using var process = OpenProcess(0x1000, false, pid);
        return !process.IsInvalid && GetProcessTimes(process, out var created, out _, out _, out _) && created == window.ProcessCreated;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
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
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
}
