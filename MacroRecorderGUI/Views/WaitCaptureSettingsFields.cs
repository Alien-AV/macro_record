using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace MacroRecorderGUI.Views;

internal sealed class WaitCaptureSettingsFields : UserControl
{
    private readonly List<(CheckBox Enabled, ComboBox Key, CheckBox Control, CheckBox Alt, CheckBox Shift)> _rows = [];
    public WaitCaptureSettingsFields(WaitCaptureConfiguration configuration)
    {
        var panel = new StackPanel { Spacing = 12 }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Add a wait from the current target", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Optional global shortcuts, off by default. While stopped, they open an editable draft. During recording, they insert a wait without a dialog. Shortcut changes are available after the run ends.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "New waits use a 30-second timeout and 200 ms stability. Window waits match a visible window by executable, class, and title. Pixel waits match the captured color at its desktop position.", TextWrapping = TextWrapping.Wrap });
        foreach (var (target, binding) in configuration.Bindings())
        {
            var label = target switch { WaitCaptureTarget.FocusedWindow => "Focused window", WaitCaptureTarget.HoveredWindow => "Window under pointer", _ => "Current pixel color under pointer" };
            var enabled = new CheckBox { Content = label, IsChecked = binding.Enabled };
            var key = new ComboBox { Header = "Key", ItemsSource = WaitCaptureConfiguration.Keys, SelectedItem = binding.Key, MinWidth = 100 };
            AutomationProperties.SetName(key, label + " shortcut key");
            var control = new CheckBox { Content = "Ctrl", IsChecked = binding.Modifiers.HasFlag(HotKeyModifiers.Control) };
            var alt = new CheckBox { Content = "Alt", IsChecked = binding.Modifiers.HasFlag(HotKeyModifiers.Alt) };
            var shift = new CheckBox { Content = "Shift", IsChecked = binding.Modifiers.HasFlag(HotKeyModifiers.Shift) };
            var modifiers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            foreach (var modifier in new[] { control, alt, shift }) { AutomationProperties.SetName(modifier, label + " " + modifier.Content); modifiers.Children.Add(modifier); }
            var body = new StackPanel { Spacing = 6 };
            body.Children.Add(modifiers); body.Children.Add(key);
            void UpdateEnabled() => key.IsEnabled = control.IsEnabled = alt.IsEnabled = shift.IsEnabled = enabled.IsChecked == true;
            UpdateEnabled(); enabled.Checked += (_, _) => UpdateEnabled(); enabled.Unchecked += (_, _) => UpdateEnabled();
            panel.Children.Add(enabled); panel.Children.Add(body);
            _rows.Add((enabled, key, control, alt, shift));
        }
        panel.Children.Add(new TextBlock { Text = "Use Ctrl or Alt, optional Shift, and a letter, digit, or function key. Record, Play, and every emergency-stop choice are reserved. Capturing never focuses another window.", TextWrapping = TextWrapping.Wrap });
    }

    public WaitCaptureConfiguration Read()
    {
        var values = _rows.Select(row => new WaitCaptureBinding(row.Enabled.IsChecked == true,
            row.Key.SelectedItem is VirtualKey key ? key : VirtualKey.None,
            (row.Control.IsChecked == true ? HotKeyModifiers.Control : 0)
            | (row.Alt.IsChecked == true ? HotKeyModifiers.Alt : 0)
            | (row.Shift.IsChecked == true ? HotKeyModifiers.Shift : 0))).ToArray();
        var result = new WaitCaptureConfiguration(values[0], values[1], values[2]);
        result.Validate(); return result;
    }
}
