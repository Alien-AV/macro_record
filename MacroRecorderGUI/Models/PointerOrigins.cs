using System.Runtime.InteropServices;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using Google.Protobuf;
using ProtobufGenerated;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroRecorderGUI.Models;

public enum PlaybackPointerOrigin { RecordedStartingPoint, CurrentPointer }
public enum PointerCoordinateFrame { PhysicalScreenPixels = 1 }
public sealed record PointerPosition(int X, int Y, PointerCoordinateFrame Frame = PointerCoordinateFrame.PhysicalScreenPixels)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }
}
public sealed record PointerOriginBoundary(int EventIndex, PointerPosition? Position, ulong DelayMicroseconds = 0, string? AdoptedEvent = null)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }
    internal ProtobufInputEvent SetupEvent()
    {
        ProtobufInputEvent input;
        try
        {
            input = AdoptedEvent is null ? new ProtobufInputEvent { MouseEvent = new() { ActionType = 1, MappedToVirtualDesktop = true } }
                : ProtobufInputEvent.Parser.ParseFrom(Convert.FromBase64String(AdoptedEvent));
        }
        catch (FormatException error)
        {
            throw new InvalidDataException("An adopted pointer origin contains invalid Base64 event data.", error);
        }
        if (input.MouseEvent is not { RelativePosition: false, ActionType: 1, WheelRotation: 0 })
            throw new InvalidDataException("An adopted pointer origin must contain only an absolute Move.");
        input.TimeSinceLastEvent = DelayMicroseconds;
        input.MouseEvent.X = Position!.X; input.MouseEvent.Y = Position.Y;
        return input;
    }
}

internal readonly record struct PointerBounds(int Left, int Top, int Width, int Height)
{
    public bool Contains(int x, int y) => Width > 0 && Height > 0 && x >= Left && y >= Top
        && (long)x < (long)Left + Width && (long)y < (long)Top + Height;
}

internal interface IPointerEnvironment
{
    PointerPosition GetPosition();
    PointerBounds GetBounds(bool virtualDesktop);
}

internal sealed class WindowsPointerEnvironment : IPointerEnvironment
{
    public PointerPosition GetPosition()
    {
        if (!GetPhysicalCursorPos(out var point)) throw new InvalidOperationException("Windows could not read the current pointer position. Playback did not start.");
        return new(point.X, point.Y);
    }

    public PointerBounds GetBounds(bool virtualDesktop)
    {
        // Match native absolute input's physical coordinate frame on mixed-DPI desktops.
        var previous = SetThreadDpiAwarenessContext(new nint(-4));
        if (previous == 0) throw new InvalidOperationException("Physical desktop coordinates are unavailable. Playback did not start.");
        try
        {
            return virtualDesktop ? new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79))
                : new(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
}

internal static class PointerPlayback
{
    public static void Validate(IReadOnlyList<PointerOriginBoundary> origins, int count, PlaybackPointerOrigin mode)
    {
        var previous = -1;
        foreach (var origin in origins)
        {
            if (origin.EventIndex < previous || origin.EventIndex < 0 || origin.EventIndex > count)
                throw new InvalidDataException("A pointer origin boundary is outside the recording.");
            if (origin.Position is null)
                throw new InvalidOperationException("A capture segment has no recorded pointer position. Its start position could not be sampled; playback cannot safely position this segment.");
            if (origin.Position.Frame != PointerCoordinateFrame.PhysicalScreenPixels)
                throw new InvalidOperationException("This recording uses an unsupported pointer coordinate frame. Playback did not start.");
            previous = origin.EventIndex;
        }
        if (mode == PlaybackPointerOrigin.CurrentPointer && (origins.Count == 0 || origins[0].EventIndex != 0))
            throw new InvalidOperationException("This recording has no starting-point metadata. Keep Recorded starting point for legacy playback, or explicitly use its first position as origin in Playback options.");
    }

    public static InputEvent[] Prepare(InputEvent[] events, IReadOnlyList<PointerOriginBoundary> origins,
        PlaybackPointerOrigin mode, IPointerEnvironment environment)
    {
        Validate(origins, events.Length, mode);
        if (origins.Count == 0) return events;
        long dx = 0, dy = 0;
        if (mode == PlaybackPointerOrigin.CurrentPointer)
        {
            var chosen = environment.GetPosition();
            if (chosen.Frame != PointerCoordinateFrame.PhysicalScreenPixels)
                throw new InvalidOperationException("The current pointer uses an incompatible coordinate frame.");
            dx = (long)chosen.X - origins[0].Position!.X;
            dy = (long)chosen.Y - origins[0].Position!.Y;
        }
        var desktop = environment.GetBounds(true);
        var primary = events.OfType<MouseEvent>().Any(m => !m.RelativePosition && !m.MappedToVirtualDesktop && (m.ActionType & MouseActionTypeFlags.Move) != 0)
            || origins.Any(origin => origin.AdoptedEvent is not null && !origin.SetupEvent().MouseEvent.MappedToVirtualDesktop)
            ? environment.GetBounds(false) : default;
        var result = new List<InputEvent>(events.Length + origins.Count);
        var boundary = 0;
        for (var index = 0; index <= events.Length; index++)
        {
            while (boundary < origins.Count && origins[boundary].EventIndex == index)
            {
                var origin = origins[boundary++];
                var setup = new MouseEvent(origin.SetupEvent());
                var point = Translate(origin.Position!.X, origin.Position.Y, setup.MappedToVirtualDesktop ? desktop : primary);
                setup.X = point.X; setup.Y = point.Y;
                result.Add(setup);
            }
            if (index == events.Length) break;
            var input = events[index];
            if (input is MouseEvent { RelativePosition: false } mouse && (mouse.ActionType & MouseActionTypeFlags.Move) != 0)
            {
                var point = Translate(mouse.X, mouse.Y, mouse.MappedToVirtualDesktop ? desktop : primary);
                mouse.X = point.X; mouse.Y = point.Y;
            }
            result.Add(input);
        }
        return result.ToArray();

        PointerPosition Translate(int x, int y, PointerBounds bounds)
        {
            var tx = (long)x + dx; var ty = (long)y + dy;
            if (tx < int.MinValue || tx > int.MaxValue || ty < int.MinValue || ty > int.MaxValue)
                throw new InvalidOperationException("The chosen pointer origin would overflow screen coordinates. Playback did not start.");
            if (!bounds.Contains((int)tx, (int)ty))
                throw new InvalidOperationException("The chosen pointer origin places a position outside its primary-screen or virtual-desktop frame. Choose another origin or correct the recording; playback did not start.");
            return new((int)tx, (int)ty);
        }
    }
}
