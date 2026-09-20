using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace MacroRecorderGUI.Views;

internal static class DesignResources
{
    // Imperative drawing needs the element's effective theme, not Application.RequestedTheme.
    // AccessibilitySettings is read only; desktop change subscriptions stay on ThemeSettings.
    public static Brush Brush(FrameworkElement owner, string key)
    {
        var theme = new AccessibilitySettings().HighContrast ? "HighContrast"
            : owner.ActualTheme == ElementTheme.Dark ? "Default" : "Light";
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
            if (dictionary.ThemeDictionaries.TryGetValue(theme, out var themed)
                && themed is ResourceDictionary palette && palette.TryGetValue(key, out var value))
                return (Brush)value;
        return (Brush)Application.Current.Resources[key];
    }
}
