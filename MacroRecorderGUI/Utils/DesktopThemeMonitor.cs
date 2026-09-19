using Microsoft.UI;
using Microsoft.UI.System;
using Windows.UI.ViewManagement;

namespace MacroRecorderGUI.Utils;

/// <summary>Desktop theme subscriptions, created and disposed on the window's owning thread.</summary>
internal sealed class DesktopThemeMonitor : IDisposable
{
    private readonly ThemeSettings _themeSettings;
    private readonly UISettings _uiSettings;
    private volatile Action? _changed;

    public DesktopThemeMonitor(WindowId windowId, Action changed)
    {
        _themeSettings = ThemeSettings.CreateForWindowId(windowId);
        _uiSettings = new UISettings();
        _changed = changed;
        _themeSettings.Changed += ThemeSettings_Changed;
        try
        {
            _uiSettings.ColorValuesChanged += SystemColors_Changed;
        }
        catch
        {
            _changed = null;
            _themeSettings.Changed -= ThemeSettings_Changed;
            throw;
        }
    }

    public bool HighContrast => _themeSettings.HighContrast;

    private void ThemeSettings_Changed(ThemeSettings sender, object args) => _changed?.Invoke();
    private void SystemColors_Changed(UISettings sender, object args) => _changed?.Invoke();

    public void Dispose()
    {
        if (_changed is null) return;
        _changed = null;
        try
        {
            _themeSettings.Changed -= ThemeSettings_Changed;
        }
        finally
        {
            _uiSettings.ColorValuesChanged -= SystemColors_Changed;
        }
    }
}
