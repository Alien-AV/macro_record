using System.ComponentModel;
using System.Runtime.InteropServices;
using MacroRecorderGUI.Utils;
using Microsoft.UI;
using Windows.UI.ViewManagement;

namespace MacroRecorderGUITests;

[TestClass]
public class DesktopThemeMonitorTests
{
    [STATestMethod]
    [TestCategory("DesktopThemeIntegration")]
    public void DesktopSettingsSubscribeReadAndUnsubscribeWithoutXamlOrVisibleWindows()
    {
        // ThemeSettings requires a top-level HWND owned by this thread. Omitting
        // WS_VISIBLE creates one without showing or activating any window.
        var window = CreateWindowEx(0, "STATIC", "Theme subscription test", 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            Assert.IsFalse(IsWindowVisible(window));
            var windowId = Win32Interop.GetWindowIdFromWindow(window);
            // Exercise the production API binding and both real event subscriptions.
            // A palette/fake-only test cannot detect a COM failure in event add/remove.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var monitor = new DesktopThemeMonitor(windowId, () => { });
                Assert.AreEqual(new AccessibilitySettings().HighContrast, monitor.HighContrast);
                monitor.Dispose(); // Closing twice must not remove an event token twice.
            }
            Assert.IsFalse(IsWindowVisible(window));
        }
        finally
        {
            if (!DestroyWindow(window)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);
}
