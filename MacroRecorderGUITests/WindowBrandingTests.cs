using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.Views;
using Microsoft.UI.Xaml;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WindowBrandingTests
{
    [TestMethod]
    [DataRow("MainWindow.xaml")]
    [DataRow("Views/RunController.xaml")]
    public void WindowTitleUsesTheFullApplicationName(string source)
    {
        Assert.AreEqual("Macro Recorder", (string?)XDocument.Load(SourceFile(source)).Root!.Attribute("Title"));
    }

    [TestMethod]
    public void OriginalIconIsDeliveredToTheTestOutputUnchanged()
    {
        var delivered = Path.Combine(AppContext.BaseDirectory, "mouse.ico");
        Assert.IsTrue(File.Exists(delivered), "The icon must propagate from the app project to referencing test output.");
        CollectionAssert.AreEqual(File.ReadAllBytes(SourceFile("mouse.ico")), File.ReadAllBytes(delivered));
    }

    internal static void CheckHiddenWindows(Window mainWindow)
    {
        CheckWindow(mainWindow);
        var controller = new RunController(ElementTheme.Default, "Branding check", "Unavailable");
        try
        {
            CheckWindow(controller);
            CheckIconFailures(controller);
        }
        finally { controller.Finish(); }
    }

    private static void CheckIconFailures(Window window)
    {
        var directory = Directory.CreateTempSubdirectory("MacroRecorder-branding-");
        try
        {
            var missing = Path.Combine(directory.FullName, "missing.ico");
            WindowIcon.Apply(window.AppWindow, missing);
            CheckWindow(window);
            var corrupt = Path.Combine(directory.FullName, "corrupt.ico");
            File.WriteAllText(corrupt, "not an icon");
            WindowIcon.Apply(window.AppWindow, corrupt);
            Assert.AreEqual("Macro Recorder", window.Title);
            Assert.IsFalse(IsWindowVisible(WinRT.Interop.WindowNative.GetWindowHandle(window)));
            WindowIcon.Apply(window.AppWindow);
            CheckWindow(window);
        }
        finally { directory.Delete(true); }
    }

    private static void CheckWindow(Window window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        Assert.IsFalse(IsWindowVisible(hwnd));
        Assert.AreEqual("Macro Recorder", window.Title);
        Assert.AreEqual("Macro Recorder", window.AppWindow.Title);
        // WM_GETICON queries the actual per-window title bar and taskbar icons.
        Assert.AreNotEqual(nint.Zero, SendMessage(hwnd, 0x007F, 0, 0), "Missing small window icon.");
        Assert.AreNotEqual(nint.Zero, SendMessage(hwnd, 0x007F, 1, 0), "Missing large taskbar icon.");
        Assert.IsFalse(IsWindowVisible(hwnd));
    }

    private static string SourceFile(string relative, [CallerFilePath] string testFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "MacroRecorderGUI", relative));

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
}
