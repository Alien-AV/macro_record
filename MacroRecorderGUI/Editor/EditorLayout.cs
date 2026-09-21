namespace MacroRecorderGUI.Editor;

/// <summary>Narrow viewports show one selected pane; neither pane expands the page.</summary>
public readonly record struct EditorLayout(bool SinglePane, double ViewportHeight)
{
    public static EditorLayout Fit(double width, double viewportHeight)
    {
        return new(width < 900, Math.Max(0, viewportHeight));
    }
}
