using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MacroRecorderGUI.Models;

// No input injection, activation, hooks, enumeration, or cross-process SendMessage.
internal sealed class WindowsWaitTargetCaptureApi : IWaitTargetCaptureApi
{
    public nint SetDpiContext(nint context) => SetThreadDpiAwarenessContext(context);
    public nint ForegroundWindow() => GetForegroundWindow();
    public bool Pointer(out WaitPixelPoint point) => GetPhysicalCursorPos(out point);
    public nint WindowAt(WaitPixelPoint point) => WindowFromPoint(point);
    public nint RootWindow(nint window) => window == 0 ? 0 : GetAncestor(window, 2);
    public bool OnMonitor(WaitPixelPoint point) => MonitorFromPoint(point, 0) != 0;
    public nint AcquireScreen() => GetDC(0);
    public uint Pixel(nint dc, WaitPixelPoint point) => GetPixel(dc, point.X, point.Y);
    public void ReleaseScreen(nint dc) => ReleaseDC(0, dc);

    public CapturedWindowIdentity Identity(nint window)
    {
        if (GetWindowThreadProcessId(window, out var pid) == 0) throw Unavailable("Window identity is unavailable.");
        // GetWindowText is bounded for another process; never call it on our own hung UI thread.
        if (pid == Environment.ProcessId) throw new InvalidOperationException("Choose a window belonging to another application.");
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid || !GetProcessTimes(process, out var created, out _, out _, out _))
            throw Unavailable("Access denied or target process is unavailable.");
        var path = new StringBuilder(32768); var size = (uint)path.Capacity;
        if (!QueryFullProcessImageName(process, 0, path, ref size)) throw Unavailable("Executable path is unavailable.");
        var windowClass = new StringBuilder(1026);
        if (GetClassName(window, windowClass, windowClass.Capacity) == 0) throw Unavailable("Window class is unavailable.");
        var title = new StringBuilder(1026);
        Marshal.SetLastPInvokeError(0);
        var length = GetWindowText(window, title, title.Capacity);
        if (length == 0 && Marshal.GetLastPInvokeError() != 0) throw Unavailable("Window title is unavailable.");
        if (length >= 1025) throw new InvalidOperationException("Window title exceeds the 1024-character selector limit.");
        return new(pid, created, path.ToString(), windowClass.ToString(), title.ToString());
    }

    public bool Revalidate(nint window, CapturedWindowIdentity identity)
    {
        if (!IsWindow(window) || GetWindowThreadProcessId(window, out var pid) == 0 || pid != identity.ProcessId) return false;
        // Compare the complete snapshot to reject handle reuse and titles changing during the read.
        return Identity(window) == identity;
    }
    private static Win32Exception Unavailable(string message) => new(Marshal.GetLastPInvokeError(), message);

    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPhysicalCursorPos(out WaitPixelPoint point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(WaitPixelPoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(WaitPixelPoint point, uint flags);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW", SetLastError = true)] private static extern int GetClassName(nint window, StringBuilder value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW", SetLastError = true)] private static extern int GetWindowText(nint window, StringBuilder value, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out ulong created, out ulong exited, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
}
