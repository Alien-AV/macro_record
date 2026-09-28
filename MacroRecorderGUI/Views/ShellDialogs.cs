using System.Globalization;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MacroRecorderGUI.Views;

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
        ClickAwayFocus.Attach(dialog, body);
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

    public async Task<RecordingOptions?> RecordingOptionsAsync(RecordingOptions options)
    {
        var content = Body("Recording options", "\uE765", "Used by Record and Ctrl + Q. Apply saves these options for future recordings.");
        var body = content.Fields;
        Row(body, "Start / stop recording", Note("Ctrl + Q / Ctrl + W"));
        var countdown = new NumberBox { Value = options.CountdownSeconds, Minimum = 0, Maximum = 30, SmallChange = 1, Width = 110, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Row(body, "Countdown (seconds)", countdown);
        var overrideDelay = new CheckBox { Content = new TextBlock { Text = "Set one delay for every raw event", TextWrapping = TextWrapping.Wrap },
            IsChecked = options.OverrideDelay is not null, FontSize = 14 };
        var delayBox = new TextBox { Header = "Delay per raw event (µs)", Text = (options.OverrideDelay ?? 5000).ToString(CultureInfo.InvariantCulture),
            FontSize = 14, IsEnabled = options.OverrideDelay is not null };
        overrideDelay.Checked += (_, _) => delayBox.IsEnabled = true;
        overrideDelay.Unchecked += (_, _) => delayBox.IsEnabled = false;
        var advanced = new StackPanel { Spacing = 10 };
        advanced.Children.Add(overrideDelay); advanced.Children.Add(delayBox);
        advanced.Children.Add(Note("Replaces captured timing, including delays within actions."));
        body.Children.Add(new Expander { Header = "Advanced recording options", Content = advanced, FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        content.SetSafety("Stop recording · Ctrl + W", "Clicking Stop may capture pointer input before recording ends.");
        RecordingOptions ReadOptions()
        {
            if (!double.IsFinite(countdown.Value) || countdown.Value != Math.Truncate(countdown.Value))
                throw new ArgumentException("Enter a whole countdown in seconds.");
            if (overrideDelay.IsChecked == true && (!ulong.TryParse(delayBox.Text, out var delay) || delay > long.MaxValue))
                throw new ArgumentException("Enter a non-negative whole delay no greater than 9223372036854775807 µs.");
            var result = new RecordingOptions { CountdownSeconds = checked((int)countdown.Value),
                OverrideDelay = overrideDelay.IsChecked == true ? ulong.Parse(delayBox.Text, CultureInfo.InvariantCulture) : null };
            result.Validate();
            return result;
        }
        var dialog = Dialog(content, "Apply");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { ReadOptions(); }
            catch (Exception error) when (error is ArgumentException or OverflowException)
            { args.Cancel = true; content.ShowError(error.Message); }
        };
        if (await ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return ReadOptions();
    }

    public async Task<bool?> RecordIntoExistingAsync(string name)
    {
        var content = Body("Record into this recording", "\uE765", name);
        var mode = new ComboBox { PlaceholderText = "Choose append or replace", HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.Items.Add("Append to captured input");
        mode.Items.Add("Replace all captured input");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mode, "Append or replace captured input");
        content.Fields.Children.Add(mode);
        content.SetSafety("Replace removes this recording's captured input when capture starts.",
            "Uses your saved recording options. Stop recording with Ctrl + W.");
        var dialog = Dialog(content, "Start recording");
        dialog.IsPrimaryButtonEnabled = false;
        mode.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = mode.SelectedIndex >= 0;
        return await ShowAsync(dialog) == ContentDialogResult.Primary ? mode.SelectedIndex == 1 : null;
    }

    public async Task<PlaybackOptions?> PlaybackOptionsAsync(PlaybackOptions options, string name, string stopShortcut, MacroViewModel? macro = null)
    {
        var content = Body("Playback options", "\uE916",
            "Saved for this recording. Play sends real keyboard and mouse input. Preview keeps the captured timing and sends no input.");
        var body = content.Fields;
        var recordingName = Note(name);
        recordingName.MaxLines = 2;
        recordingName.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(recordingName, name);
        Row(body, "Recording", recordingName);
        var originMode = new ComboBox { MinWidth = 190 };
        originMode.Items.Add("Recorded starting point"); originMode.Items.Add("Current pointer");
        originMode.SelectedIndex = (int)options.PointerOrigin;
        Row(body, "Pointer origin", originMode);
        body.Children.Add(Note("Current pointer is sampled after countdown, when playback starts. Every repeat uses that same origin. Relative movements remain device counts; absolute positions keep their screen coordinate frame."));
        if (macro is { HasOriginMetadata: true })
            body.Children.Add(Note($"{macro.PointerOrigins.Count} capture origin boundaries. Default .macro export preserves this metadata and requires a current player; older players reject it. Use explicit legacy export to materialize recorded setup moves."));
        else body.Children.Add(Note("Legacy recording: Recorded starting point preserves every stored event. No first event is assumed to be synthetic. Current pointer requires explicit starting-point adoption below."));
        CheckBox? adopt = null, recover = null;
        if (macro?.OriginAdoptionDescription() is { } description)
        {
            body.Children.Add(Note(description));
            adopt = new CheckBox { Content = new TextBlock { Text = "Use first position as origin (remove raw event 1)", TextWrapping = TextWrapping.Wrap } };
            body.Children.Add(adopt);
        }
        if (macro?.CanRecoverBeforeOriginAdoption == true)
        {
            body.Children.Add(Note("Recovery replaces input and origin metadata with the saved pre-adoption copy, including discarding later edits and appended captures. Undo remains available."));
            recover = new CheckBox { Content = new TextBlock { Text = "Recover the complete pre-adoption copy", TextWrapping = TextWrapping.Wrap } };
            body.Children.Add(recover);
        }
        var speed = new NumberBox { Value = options.Speed, Minimum = 0.1, Maximum = 10, SmallChange = 0.25, Width = 110, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Row(body, "Speed (×)", speed);
        var repeat = new NumberBox { Value = options.RepeatCount, Minimum = 1, Maximum = 1000, SmallChange = 1, Width = 110, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Row(body, "Repeat", repeat);
        var loopBox = new CheckBox { Content = "Repeat until stopped", IsChecked = options.RepeatUntilStopped, FontSize = 14 };
        repeat.IsEnabled = !options.RepeatUntilStopped;
        loopBox.Checked += (_, _) => repeat.IsEnabled = false;
        loopBox.Unchecked += (_, _) => repeat.IsEnabled = true;
        body.Children.Add(loopBox);
        var countdown = new NumberBox { Value = options.Countdown.TotalSeconds, Minimum = 0, Maximum = 60, SmallChange = 1, Width = 110, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Row(body, "Countdown (seconds)", countdown);
        content.SetSafety($"Emergency stop · {stopShortcut}", "Until stopped keeps sending real input until you stop it. Apply saves options without starting playback.");
        PlaybackOptions ReadOptions()
        {
            if (loopBox.IsChecked != true && (!double.IsFinite(repeat.Value) || repeat.Value != Math.Truncate(repeat.Value)))
                throw new ArgumentException("Enter a whole repeat count.");
            if (!double.IsFinite(countdown.Value)) throw new ArgumentException("Enter a valid countdown.");
            var selectedOrigin = recover?.IsChecked == true ? PlaybackPointerOrigin.RecordedStartingPoint : (PlaybackPointerOrigin)originMode.SelectedIndex;
            if (macro is not null && adopt?.IsChecked != true && recover?.IsChecked != true)
            {
                try { PointerPlayback.Validate(macro.PointerOrigins, macro.Events.Count, selectedOrigin); }
                catch (InvalidOperationException error) { throw new ArgumentException(error.Message); }
            }
            var result = new PlaybackOptions { Speed = speed.Value,
                RepeatCount = loopBox.IsChecked == true ? options.RepeatCount : checked((int)repeat.Value),
                Countdown = TimeSpan.FromSeconds(countdown.Value), RepeatUntilStopped = loopBox.IsChecked == true, PointerOrigin = selectedOrigin };
            result.Validate();
            return result;
        }
        var dialog = Dialog(content, "Apply");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { ReadOptions(); }
            catch (Exception error) when (error is ArgumentException or OverflowException)
            { args.Cancel = true; content.ShowError(error.Message); }
        };
        if (await ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        var chosen = ReadOptions();
        if (adopt?.IsChecked == true) macro!.AdoptFirstPositionAsOrigin();
        if (recover?.IsChecked == true) macro!.RecoverBeforeOriginAdoption();
        return chosen;
    }

    public async Task<bool> ConfirmLegacyExportAsync()
    {
        var body = Body("Export legacy .macro", "\uE783", "Create a copy for older players using Recorded starting point.");
        body.Fields.Children.Add(Note("Each capture origin becomes an ordinary absolute Move at its recorded position, with its setup delay preserved. True input and raw counts are unchanged. The exported copy loses origin metadata, Current pointer support, and adoption recovery; the library recording stays intact."));
        return await ShowAsync(Dialog(body, "Export legacy copy")) == ContentDialogResult.Primary;
    }

    public async Task<string?> RenameAsync(string name)
    {
        var body = Body("Rename recording", "\uE70F", "Give this recording a name you can find later.");
        var field = new TextBox { Text = name, MaxLength = 200, Header = "Recording name", FontSize = 14 }; body.Fields.Children.Add(field);
        var dialog = Dialog(body, "Save name");
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = string.IsNullOrWhiteSpace(field.Text);
        return await ShowAsync(dialog) == ContentDialogResult.Primary ? field.Text.Trim() : null;
    }

    public async Task SettingsAsync(int shortcutIndex, string registrationStatus, WaitCaptureConfiguration captureConfiguration,
        Func<int, WaitCaptureConfiguration, Task<string?>> apply)
    {
        var content = Body("Keyboard shortcuts", "\uE765", "These shortcuts work while you are using another application.");
        var body = content.Fields;
        Row(body, "Record", Note("Ctrl + Q"));
        Row(body, "Stop recording", Note("Ctrl + W"));
        Row(body, "Play in other apps", Note("Ctrl + E"));
        var shortcut = new ComboBox { MinWidth = 155 };
        foreach (var gesture in KeyboardShortcuts.EmergencyStopChoices) shortcut.Items.Add(gesture.DisplayName);
        shortcut.SelectedIndex = shortcutIndex;
        Row(body, "Emergency stop / cancel", shortcut);
        body.Children.Add(Note(registrationStatus));
        body.Children.Add(Note("Emergency stop also cancels preparation and countdowns. Record and Play start directly; use their adjacent options buttons to change saved settings."));
        var capture = new WaitCaptureSettingsFields(captureConfiguration);
        body.Children.Add(capture);
        var dialog = Dialog(content, "Save shortcuts");
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            capture.IsEnabled = shortcut.IsEnabled = false;
            try
            {
                var error = await apply(shortcut.SelectedIndex, capture.Read());
                if (error is not null) { content.ShowError(error); args.Cancel = true; }
            }
            catch (Exception error) { content.ShowError(error.Message); args.Cancel = true; }
            finally { capture.IsEnabled = shortcut.IsEnabled = dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        await ShowAsync(dialog);
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
