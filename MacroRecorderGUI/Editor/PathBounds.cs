namespace MacroRecorderGUI.Editor;

public readonly record struct PathBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public PathBounds Include(PathPosition point) => new(Math.Min(MinX, point.X), Math.Min(MinY, point.Y), Math.Max(MaxX, point.X), Math.Max(MaxY, point.Y));
}
