namespace MacroRecorderGUI.Editor;

/// <summary>The original design uses two columns, then stacks sequence and detail on narrow windows.</summary>
public readonly record struct EditorLayout(bool Stacked, double ListHeight, double InspectorHeight)
{
    public static EditorLayout Fit(double width, double viewportHeight)
    {
        var stacked = width < 720;
        return new(stacked, stacked ? 300 : Math.Max(420, viewportHeight),
            stacked ? Math.Max(480, viewportHeight - 300) : Math.Max(420, viewportHeight));
    }
}
