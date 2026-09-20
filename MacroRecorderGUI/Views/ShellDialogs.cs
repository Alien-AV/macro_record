using System.Globalization;
using MacroRecorderGUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        var content = (ShellDialogContent)dialog.Content;
        var root = dialog.XamlRoot;
        void Resize(XamlRoot sender, XamlRootChangedEventArgs args) => content.FitToViewport(sender);
        content.FitToViewport(root);
        root.Changed += Resize;
        try { return await dialog.ShowAsync(); }
        finally
        {
            root.Changed -= Resize;
            if (ReferenceEquals(_active, dialog)) _active = null;
        }
    }
    private TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources["MacroDialogNoteStyle"] };
    private ShellDialogContent Body(string title, string glyph, string description)
    {
        var content = new ShellDialogContent();
        content.SetHeading(title, glyph, description);
        return content;
    }
    private ContentDialog Dialog(ShellDialogContent body, string action)
    {
        var dialog = new ContentDialog { XamlRoot = owner.XamlRoot, RequestedTheme = owner.RequestedTheme,
            Content = body, PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.None };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dialog, body.Title);
        return dialog;
    }
    private void Row(StackPanel panel, string label, FrameworkElement value)
    {
        var grid = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = value is TextBlock ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["MacroDialogLabelStyle"], VerticalAlignment = VerticalAlignment.Center });
        value.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(value, 1); grid.Children.Add(value);
        if (value is Control control)
        {
            control.FontSize = 14;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, label);
        }
        panel.Children.Add(new Border { Style = (Style)Application.Current.Resources["MacroDialogRowStyle"], Child = grid });
    }

    public async Task<RecordingChoices?> RecordAsync(string name, bool intoExisting, ulong? delay)
    {
        var content = Body("Record a task", "\uE765", intoExisting
            ? "Record into this document. Choose whether to append or replace its captured input."
            : "Perform the task once. Review and refine it afterwards.");
        var body = content.Fields;
        var nameBox = new TextBox { Header = "Recording name", Text = name, MaxLength = 200, FontSize = 14 };
        body.Children.Add(nameBox);
        Row(body, "Start / stop recording", Note("Ctrl + Q / Ctrl + W"));
        var countdown = new NumberBox { Value = 3, Minimum = 0, Maximum = 30, SmallChange = 1, Width = 110, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Row(body, "Countdown (seconds)", countdown);
        var clear = new CheckBox { Content = "Replace all input in this recording", IsChecked = false, FontSize = 14 };
        var overrideDelay = new CheckBox { Content = new TextBlock { Text = "Set one delay for every raw event", TextWrapping = TextWrapping.Wrap },
            IsChecked = delay is not null, FontSize = 14 };
        var delayBox = new TextBox { Header = "Delay per raw event (µs)", Text = (delay ?? 5000).ToString(CultureInfo.InvariantCulture),
            FontSize = 14, IsEnabled = delay is not null };
        overrideDelay.Checked += (_, _) => delayBox.IsEnabled = true;
        overrideDelay.Unchecked += (_, _) => delayBox.IsEnabled = false;
        var advanced = new StackPanel { Spacing = 10 };
        if (intoExisting) advanced.Children.Add(clear);
        advanced.Children.Add(overrideDelay); advanced.Children.Add(delayBox);
        advanced.Children.Add(Note("Replaces captured timing, including delays within actions."));
        body.Children.Add(new Expander { Header = "Advanced recording options", Content = advanced, FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        content.SetSafety("Stop recording · Ctrl + W", "Clicking Stop may capture pointer input before recording ends.");
        var dialog = Dialog(content, "Start recording");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text)) { args.Cancel = true; content.ShowError("Enter a recording name."); }
            else if (!double.IsFinite(countdown.Value) || countdown.Value != Math.Truncate(countdown.Value))
            { args.Cancel = true; content.ShowError("Enter a whole countdown in seconds."); }
            else if (overrideDelay.IsChecked == true && !ulong.TryParse(delayBox.Text, out var parsedDelay))
            { args.Cancel = true; content.ShowError("Enter a non-negative whole delay in microseconds."); }
        };
        if (await ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return new(nameBox.Text.Trim(), (int)countdown.Value, intoExisting && clear.IsChecked == true,
            overrideDelay.IsChecked == true ? ulong.Parse(delayBox.Text, CultureInfo.InvariantCulture) : null);
    }

    public async Task<TimingChoices?> TimingAsync(PlaybackOptions options, bool loop, bool start, string name, string stopShortcut)
    {
        var content = Body(start ? "Ready to play?" : "Playback timing", start ? "\uE768" : "\uE916", start
            ? "Playback sends real keyboard and mouse input. Switch to your target app during the countdown."
            : "Scale playback in other apps, including waits. Preview keeps the captured timing.");
        var body = content.Fields;
        if (start)
        {
            var recordingName = Note(name);
            recordingName.MaxLines = 2;
            recordingName.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTipService.SetToolTip(recordingName, name);
            Row(body, "Recording", recordingName);
        }
        var speed = new ComboBox { MinWidth = 155 };
        foreach (var value in new[] { 0.25, 0.5, 1, 1.5, 2, 4 })
            speed.Items.Add(new ComboBoxItem { Content = $"{value:0.##}×{(value == 1 ? " · recorded speed" : "")}", Tag = value });
        speed.SelectedItem = speed.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (double)item.Tag == options.Speed) ?? speed.Items[2];
        Row(body, "Speed", speed);
        var repeat = new NumberBox { Value = options.RepeatCount, Minimum = 1, Maximum = 1000, SmallChange = 1, Width = 110, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Row(body, "Repeat", repeat);
        var loopBox = new CheckBox { Content = "Repeat until stopped", IsChecked = loop, FontSize = 14 };
        repeat.IsEnabled = !loop;
        loopBox.Checked += (_, _) => repeat.IsEnabled = false;
        loopBox.Unchecked += (_, _) => repeat.IsEnabled = true;
        body.Children.Add(loopBox);
        var countdown = new NumberBox { Value = options.Countdown.TotalSeconds, Minimum = 0, Maximum = 60, SmallChange = 1, Width = 110, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Row(body, "Countdown (seconds)", countdown);
        content.SetSafety($"Emergency stop · {stopShortcut}");
        var dialog = Dialog(content, start ? "Start playback" : "Apply");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if ((loopBox.IsChecked != true && (!double.IsFinite(repeat.Value) || repeat.Value != Math.Truncate(repeat.Value)))
                || !double.IsFinite(countdown.Value))
            { args.Cancel = true; content.ShowError("Enter a whole repeat count and a valid countdown."); }
        };
        if (await ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return new(new PlaybackOptions { Speed = (double)((ComboBoxItem)speed.SelectedItem).Tag,
            RepeatCount = loopBox.IsChecked == true ? 1 : (int)repeat.Value, Countdown = TimeSpan.FromSeconds(countdown.Value) }, loopBox.IsChecked == true);
    }

    public async Task<string?> RenameAsync(string name)
    {
        var body = Body("Rename recording", "\uE70F", "Give this recording a name you can find later.");
        var field = new TextBox { Text = name, MaxLength = 200, Header = "Recording name", FontSize = 14 }; body.Fields.Children.Add(field);
        var dialog = Dialog(body, "Save name");
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = string.IsNullOrWhiteSpace(field.Text);
        return await ShowAsync(dialog) == ContentDialogResult.Primary ? field.Text.Trim() : null;
    }

    public async Task<int?> SettingsAsync(int shortcutIndex, string registrationStatus)
    {
        var content = Body("Keyboard shortcuts", "\uE765", "These shortcuts work while you are using another application.");
        var body = content.Fields;
        Row(body, "New recording", Note("Ctrl + Q"));
        Row(body, "Stop recording", Note("Ctrl + W"));
        Row(body, "Play in other apps", Note("Ctrl + E"));
        var shortcut = new ComboBox { MinWidth = 155 };
        foreach (var gesture in KeyboardShortcuts.EmergencyStopChoices) shortcut.Items.Add(gesture.DisplayName);
        shortcut.SelectedIndex = shortcutIndex;
        Row(body, "Emergency stop / cancel", shortcut);
        body.Children.Add(Note(registrationStatus));
        body.Children.Add(Note("Emergency stop also cancels a countdown. Recording and playback options are available before each run."));
        var dialog = Dialog(content, "Save shortcut");
        return await ShowAsync(dialog) == ContentDialogResult.Primary ? shortcut.SelectedIndex : null;
    }

    public async Task<bool> CloseFailureAsync(string error)
    {
        var body = Body("Could not close safely", "\uE783", "The recording could not be saved. Keep the app open to retry or export your work.");
        body.Fields.Children.Add(Note(error));
        var dialog = Dialog(body, "Retry");
        dialog.CloseButtonText = "Keep open";
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }
}
