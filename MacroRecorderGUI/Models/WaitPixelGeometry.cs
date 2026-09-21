using System.Runtime.InteropServices;
using ProtobufGenerated;

namespace MacroRecorderGUI.Models;

[StructLayout(LayoutKind.Sequential)] internal struct WaitPixelPoint { public int X, Y; }
[StructLayout(LayoutKind.Sequential)] internal struct WaitPixelRect { public int Left, Top, Right, Bottom; }
internal readonly record struct WaitSurface(bool Visible, bool Minimized, bool Cloaked, WaitPixelRect? Bounds);
internal interface IWaitGeometryApi
{
    nint WindowContext(nint window);
    nint SetContext(nint context);
    uint WindowDpi(nint window);
    bool ClientBounds(nint window, out WaitPixelRect bounds);
    bool ToScreen(nint window, ref WaitPixelPoint point);
    bool ToPhysical(nint window, ref WaitPixelPoint point);
    bool Above(nint window, out nint above);
    WaitSurface? Surface(nint window);
}

internal static class WaitPixelGeometry
{
    public static bool Resolve(PixelCondition condition, nint window, IWaitGeometryApi api, out WaitPixelPoint point)
    {
        point = new() { X = condition.X, Y = condition.Y };
        var logical = condition.Coordinates == PixelCoordinates.ClientLogical;
        // Logical offsets first enter the target's own coordinate space, then use
        // its rendering transform. This also handles Windows bitmap scaling of
        // DPI-unaware and system-aware windows on another monitor.
        var context = logical ? api.WindowContext(window) : new nint(-4);
        if (context == 0) return false;
        var previous = api.SetContext(context);
        if (previous == 0) return false;
        try
        {
            if (!api.ClientBounds(window, out var bounds)) return false;
            if (logical)
            {
                var dpi = api.WindowDpi(window);
                if (dpi == 0 || condition.ReferenceDpi == 0) return false;
                var x = Math.Round((double)point.X * dpi / condition.ReferenceDpi);
                var y = Math.Round((double)point.Y * dpi / condition.ReferenceDpi);
                if (x < 0 || y < 0 || x > int.MaxValue || y > int.MaxValue) return false;
                point.X = (int)x; point.Y = (int)y;
            }
            if (!Contains(bounds, point) || !api.ToScreen(window, ref point)) return false;
            return !logical || api.ToPhysical(window, ref point);
        }
        finally { api.SetContext(previous); }
    }

    public static bool Uncovered(nint window, WaitPixelPoint point, IWaitGeometryApi api)
    {
        var visited = new HashSet<nint> { window };
        for (var current = window; ;)
        {
            if (!api.Above(current, out var above)) return false;
            if (above == 0) return true;
            if (visited.Count >= 4096 || !visited.Add(above)) return false;
            current = above;
            if (api.Surface(above) is not { } surface) return false;
            if (!surface.Visible || surface.Minimized || surface.Cloaked) continue;
            // Visibility/geometry, not input eligibility: disabled windows and
            // disabled overlays participate. Layered/region uncertainty is
            // conservatively unavailable instead of guessing transparency.
            if (surface.Bounds is not { } bounds || Contains(bounds, point)) return false;
        }
    }
    private static bool Contains(WaitPixelRect rect, WaitPixelPoint point) =>
        point.X >= rect.Left && point.Y >= rect.Top && point.X < rect.Right && point.Y < rect.Bottom;
}
