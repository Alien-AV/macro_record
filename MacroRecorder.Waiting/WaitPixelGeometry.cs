using System.Runtime.InteropServices;
using ProtobufGenerated;

namespace MacroRecorder.Waiting;

[StructLayout(LayoutKind.Sequential)] public struct WaitPixelPoint { public int X, Y; }
[StructLayout(LayoutKind.Sequential)] public struct WaitPixelRect { public int Left, Top, Right, Bottom; }
public readonly record struct WaitSurface(bool Visible, bool Minimized, bool Cloaked, WaitPixelRect? Bounds);
public interface IWaitGeometryApi
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

public static class WaitPixelGeometry
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
        => RegionUncovered(window, new WaitPixelRect { Left = point.X, Top = point.Y,
            Right = point.X == int.MaxValue ? int.MaxValue : point.X + 1, Bottom = point.Y == int.MaxValue ? int.MaxValue : point.Y + 1 }, api);

    public static bool RegionUncovered(nint window, WaitPixelRect region, IWaitGeometryApi api)
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
            if (surface.Bounds is not { } bounds || (bounds.Left < region.Right && bounds.Right > region.Left
                && bounds.Top < region.Bottom && bounds.Bottom > region.Top)) return false;
        }
    }
    private static bool Contains(WaitPixelRect rect, WaitPixelPoint point) =>
        point.X >= rect.Left && point.Y >= rect.Top && point.X < rect.Right && point.Y < rect.Bottom;

    public static bool ResolveRegion(OcrRegion region, nint window, IWaitGeometryApi api, out WaitPixelRect result)
    {
        result = default;
        var logical = region.Coordinates == PixelCoordinates.ClientLogical;
        var context = logical ? api.WindowContext(window) : new nint(-4);
        if (context == 0) return false;
        var previous = api.SetContext(context);
        if (previous == 0) return false;
        try
        {
            if (!api.ClientBounds(window, out var bounds)) return false;
            var scale = 1.0;
            if (logical)
            {
                var dpi = api.WindowDpi(window);
                if (dpi == 0 || region.ReferenceDpi == 0) return false;
                scale = (double)dpi / region.ReferenceDpi;
            }
            var left = Math.Round(region.X * scale); var top = Math.Round(region.Y * scale);
            var right = Math.Round(((long)region.X + region.Width) * scale);
            var bottom = Math.Round(((long)region.Y + region.Height) * scale);
            if (left < bounds.Left || top < bounds.Top || right > bounds.Right || bottom > bounds.Bottom || right <= left || bottom <= top) return false;
            var start = new WaitPixelPoint { X = (int)left, Y = (int)top };
            var end = new WaitPixelPoint { X = (int)right, Y = (int)bottom };
            if (!api.ToScreen(window, ref start) || !api.ToScreen(window, ref end)
                || (logical && (!api.ToPhysical(window, ref start) || !api.ToPhysical(window, ref end)))) return false;
            result = new() { Left = start.X, Top = start.Y, Right = end.X, Bottom = end.Y };
            return end.X > start.X && end.Y > start.Y;
        }
        finally { api.SetContext(previous); }
    }
}
