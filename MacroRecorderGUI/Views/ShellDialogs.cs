using System.Globalization;
using MacroRecorderGUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MacroRecorderGUI.Views;

internal sealed record RecordingChoices(string Name, int CountdownSeconds, bool Clear, ulong? OverrideDelay);
internal sealed record TimingChoices(PlaybackOptions Options, bool Loop);

internal sealed class ShellDialogs(FrameworkElement owner)
{
    private ContentDialog? _active;
    public void Dismiss() => _active?.Hide();
    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        _active = dialog;
        try { return await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_active, dialog)) _active = null; }
    }
    private Brush Brush(string key) => DesignResources.Brush(owner, key);
    private TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap,
        FontSize = 13, Foreground = Brush("MacroMutedBrush") };
    private StackPanel Body(string glyph, string description)
    {
        var panel = new StackPanel { Spacing = 14, MinWidth = 280 };
        panel.Children.Add(new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Left, Background = Brush("MacroSoftBrush"),
            Child = new FontIcon { Glyph = glyph, FontSize = 20, Foreground = Brush("MacroBlueBrush") } });
        panel.Children.Add(Note(description));
        return panel;
    }
    private ContentDialog Dialog(FrameworkElement owner, string title, StackPanel body, string action)
    {
        body.Children.Insert(1, new TextBlock { Text = title, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("MacroInkBrush"), CharacterSpacing = -22, TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog { XamlRoot = owner.XamlRoot, RequestedTheme = owner.RequestedTheme,
            Content = new ScrollViewer { Content = body, MaxHeight = 440, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dialog, title);
        return dialog;
    }
    private void Row(StackPanel panel, string label, FrameworkElement value)
    {
        var grid = new Grid { ColumnSpacing = 20, Padding = new Thickness(0, 8, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Brush("MacroInkBrush"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(value, 1); grid.Children.Add(value);
        if (value is Control control) Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, label);
        panel.Children.Add(new Border { BorderBrush = Brush("MacroLineBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid });
    }

    public async Task<RecordingChoices?> RecordAsync(string name, bool intoExisting, ulong? delay)
    {
        var body = Body("\uE765", intoExisting
            ? "Record into this document. Choose whether to append or replace its captured input."
            : "Perform the task once. Review and refine it afterwards.");
        var nameBox = new TextBox { Header = "Recording name", Text = name, MaxLength = 200 };
        body.Children.Add(nameBox);
        Row(body, "Start / stop recording", Note("Ctrl + Q / Ctrl + W"));
        var countdown = new NumberBox { Value = 3, Minimum = 0, Maximum = 30, SmallChange = 1, Width = 110, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Row(body, "Countdown (seconds)", countdown);
        var clear = new CheckBox { Content = "Replace all input in this recording", IsChecked = false };
        var overrideDelay = new CheckBox { Content = "Override each raw event delay after capture", IsChecked = delay is not null };
        var delayBox = new TextBox { Header = "Delay per raw event (µs)", Text = (delay ?? 5000).ToString(CultureInfo.InvariantCulture) };
        var advanced = new StackPanel { Spacing = 10 };
        if (intoExisting) advanced.Children.Add(clear);
        advanced.Children.Add(overrideDelay); advanced.Children.Add(delayBox);
        advanced.Children.Add(Note("The delay override changes every captured raw event, including timing within actions."));
        body.Children.Add(new Expander { Header = "Advanced recording options", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        body.Children.Add(Note("Use Ctrl + W to stop without clicking the controller. Pointer input may be captured before an on-screen Stop takes effect."));
        var error = Note(""); body.Children.Add(error);
        var dialog = Dialog(owner, "Record a task", body, "Start recording");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text)) { args.Cancel = true; error.Text = "Enter a recording name."; }
            else if (!double.IsFinite(countdown.Value) || countdown.Value != Math.Truncate(countdown.Value))
            { args.Cancel = true; error.Text = "Enter a whole countdown in seconds."; }
            else if (overrideDelay.IsChecked == true && !ulong.TryParse(delayBox.Text, out var parsedDelay))
            { args.Cancel = true; error.Text = "Enter a non-negative whole delay in microseconds."; }
        };
        if (await ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return new(nameBox.Text.Trim(), (int)countdown.Value, intoExisting && clear.IsChecked == true,
            overrideDelay.IsChecked == true ? ulong.Parse(delayBox.Text, CultureInfo.InvariantCulture) : null);
    }

    public async Task<TimingChoices?> TimingAsync(PlaybackOptions options, bool loop, bool start, string name, string stopShortcut)
    {
        var body = Body(start ? "\uE768" : "\uE916", start
            ? "Playback sends real keyboard and mouse input. Switch to your target app during the countdown."
            : "Scale playback in other apps, including waits. Preview keeps the captured timing.");
        if (start) Row(body, "Recording", Note(name));
        var speed = new ComboBox { MinWidth = 155 };
        foreach (var value in new[] { 0.25, 0.5, 1, 1.5, 2, 4 })
            speed.Items.Add(new ComboBoxItem { Content = $"{value:0.##}×{(value == 1 ? " · recorded speed" : "")}", Tag = value });
        speed.SelectedItem = speed.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (double)item.Tag == options.Speed) ?? speed.Items[2];
        Row(body, "Speed", speed);
        var repeat = new NumberBox { Value = options.RepeatCount, Minimum = 1, Maximum = 1000, SmallChange = 1, Width = 110, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Row(body, "Repeat", repeat);
        var loopBox = new CheckBox { Content = "Repeat until stopped", IsChecked = loop };
        repeat.IsEnabled = !loop;
        loopBox.Checked += (_, _) => repeat.IsEnabled = false;
        loopBox.Unchecked += (_, _) => repeat.IsEnabled = true;
        body.Children.Add(loopBox);
        var countdown = new NumberBox { Value = options.Countdown.TotalSeconds, Minimum = 0, Maximum = 60, SmallChange = 1, Width = 110, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Row(body, "Countdown (seconds)", countdown);
        Row(body, "Emergency stop", Note(stopShortcut));
        var error = Note(""); body.Children.Add(error);
        var dialog = Dialog(owner, start ? "Ready to play?" : "Playback timing", body, start ? "Start playback" : "Apply");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if ((loopBox.IsChecked != true && (!double.IsFinite(repeat.Value) || repeat.Value != Math.Truncate(repeat.Value)))
                || !double.IsFinite(countdown.Value))
            { args.Cancel = true; error.Text = "Enter a whole repeat count and a valid countdown."; }
        };
        if (await ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return new(new PlaybackOptions { Speed = (double)((ComboBoxItem)speed.SelectedItem).Tag,
            RepeatCount = loopBox.IsChecked == true ? 1 : (int)repeat.Value, Countdown = TimeSpan.FromSeconds(countdown.Value) }, loopBox.IsChecked == true);
    }

    public async Task<string?> RenameAsync(string name)
    {
        var body = Body("\uE70F", "Give this recording a name you can find later.");
        var field = new TextBox { Text = name, MaxLength = 200, Header = "Recording name" }; body.Children.Add(field);
        var dialog = Dialog(owner, "Rename recording", body, "Save name");
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = string.IsNullOrWhiteSpace(field.Text);
        return await ShowAsync(dialog) == ContentDialogResult.Primary ? field.Text.Trim() : null;
    }

    public async Task<int?> SettingsAsync(int shortcutIndex, string registrationStatus)
    {
        var body = Body("\uE765", "These shortcuts work while you are using another application.");
        Row(body, "New recording", Note("Ctrl + Q"));
        Row(body, "Stop recording", Note("Ctrl + W"));
        Row(body, "Play in other apps", Note("Ctrl + E"));
        var shortcut = new ComboBox { MinWidth = 155 };
        foreach (var gesture in KeyboardShortcuts.EmergencyStopChoices) shortcut.Items.Add(gesture.DisplayName);
        shortcut.SelectedIndex = shortcutIndex;
        Row(body, "Emergency stop / cancel", shortcut);
        body.Children.Add(Note(registrationStatus));
        body.Children.Add(Note("Emergency stop also cancels a countdown. Recording and playback options are available before each run."));
        var dialog = Dialog(owner, "Keyboard shortcuts", body, "Save shortcut");
        return await ShowAsync(dialog) == ContentDialogResult.Primary ? shortcut.SelectedIndex : null;
    }

    public async Task<bool> CloseFailureAsync(string error)
    {
        var body = Body("\uE783", "The recording could not be saved. Keep the app open to retry or export your work.");
        body.Children.Add(Note(error));
        var dialog = Dialog(owner, "Could not close safely", body, "Retry");
        dialog.CloseButtonText = "Keep open";
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }
}
