using Windows.UI;

namespace MacroRecorderGUI.Utils;

/// <summary>Native caption colors; null restores the corresponding Windows color.</summary>
public sealed record TitleBarPalette(Color? Background, Color? Foreground, Color? InactiveForeground,
    Color? HoverBackground, Color? PressedBackground)
{
    public static TitleBarPalette ForTheme(bool dark, bool highContrast, bool colorCustomizationSupported)
    {
        if (highContrast || !colorCustomizationSupported) return new(null, null, null, null, null);
        return dark
            ? new(Rgb(32), Rgb(255), Rgb(160), Rgb(51), Rgb(45))
            : new(Rgb(243), Rgb(26), Rgb(112), Rgb(229), Rgb(217));
    }

    private static Color Rgb(byte value) => Color.FromArgb(255, value, value, value);
}
