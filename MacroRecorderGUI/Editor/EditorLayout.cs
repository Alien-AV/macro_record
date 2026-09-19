namespace MacroRecorderGUI.Editor;

/// <summary>Panel sizes use the scroll viewport, so transport rows remain inside the preview panel.</summary>
public readonly record struct EditorLayout(bool Stacked, bool InspectorBelow, double MainHeight, double ListHeight, double InspectorHeight)
{
    public static EditorLayout Fit(double width, double viewportHeight)
    {
        var stacked = width < 720;
        var below = width < 1100;
        var height = Math.Max(360, viewportHeight);
        if (stacked) height = Math.Clamp(viewportHeight, 360, 460);
        return new(stacked, below, height, stacked ? 240 : height, below ? 460 : height);
    }
}
