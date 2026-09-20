using System.Diagnostics;
using Microsoft.UI.Windowing;

namespace MacroRecorderGUI.Utils;

internal static class WindowIcon
{
    internal static void Apply(AppWindow window, string? iconPath = null)
    {
        try
        {
            var path = iconPath ?? Path.Combine(AppContext.BaseDirectory, "mouse.ico");
            if (File.Exists(path)) window.SetIcon(path);
        }
        catch (Exception error)
        {
            // A missing/unreadable icon or a windowing API failure must not prevent startup.
            Debug.WriteLine($"Unable to set the Macro Recorder window icon: {error}");
        }
    }
}
