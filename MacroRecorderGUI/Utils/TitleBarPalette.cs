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
            ? new(Rgb(29, 37, 49), Rgb(237, 242, 249), Rgb(164, 176, 193), Rgb(48, 59, 74), Rgb(53, 65, 82))
            : new(Rgb(246, 247, 248), Rgb(32, 40, 50), Rgb(107, 116, 129), Rgb(238, 240, 243), Rgb(228, 231, 235));
    }

    private static Color Rgb(byte red, byte green, byte blue) => Color.FromArgb(255, red, green, blue);
}
