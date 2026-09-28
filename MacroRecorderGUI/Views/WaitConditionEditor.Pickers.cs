using System.Globalization;
using MacroRecorderGUI.Models;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ProtobufGenerated;

namespace MacroRecorderGUI.Views;

internal sealed partial class WaitConditionEditor
{
    private readonly IWaitTargetPicker _picker;
    private readonly Func<TimeSpan, CancellationToken, Task> _pickerDelay;
    private readonly List<Button> _pickerButtons = [];
    private CancellationTokenSource? _pick;
    private Button? _pickButton;
    private string? _pickLabel;

    private Button CreateWindowPickerButton(string label, Action<WindowSelector> apply) =>
        CreatePickerButton(label, WaitCaptureTarget.HoveredWindow, capture => apply(capture.Window!.Clone()));

    private Button CreatePickerButton(string label, WaitCaptureTarget target, Action<WaitTargetCapture> apply)
    {
        var button = new Button { Content = label };
        AutomationProperties.SetName(button, label);
        _pickerButtons.Add(button);
        button.Click += async (_, _) => await PickAsync(button, label, target, apply);
        return button;
    }

    internal async Task PickAsync(Button button, string label, WaitCaptureTarget target, Action<WaitTargetCapture> apply)
    {
        if (_disposed) return;
        if (_pick is not null) { CancelPicker(); return; }
        CancelTest();
        using var cancellation = new CancellationTokenSource();
        _pick = cancellation; _pickButton = button; _pickLabel = label;
        button.Content = "Cancel target pick";
        try
        {
            // A countdown lets the pointer reach the target without intercepting clicks
            // or installing temporary global shortcuts. Only the final sample reads the desktop.
            for (var seconds = 3; seconds > 0; seconds--)
            {
                Feedback.Text = $"Point at the {(target == WaitCaptureTarget.PointerPixel ? "pixel" : "window")} · capturing in {seconds}s. No click needed. Click Cancel target pick to cancel.";
                await _pickerDelay(TimeSpan.FromSeconds(1), cancellation.Token).WaitAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (_disposed || !ReferenceEquals(_pick, cancellation)) return;
            }
            var capture = _picker.Capture(target);
            if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(_pick, cancellation)) return;
            _pick = null; _pickButton = null; _pickLabel = null; button.Content = label;
            if (!capture.Succeeded) { Feedback.Text = capture.Error ?? "No target was captured."; return; }
            apply(capture);
            Feedback.Text = "Target captured into the draft. Review the fields and apply the condition.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        { if (!_disposed && !cancellation.IsCancellationRequested) Feedback.Text = "Target capture is unavailable. " + error.Message; }
        finally
        {
            if (ReferenceEquals(_pick, cancellation))
            { _pick = null; _pickButton = null; _pickLabel = null; button.Content = label; }
        }
    }

    private void CancelPicker()
    {
        if (_pick is not { } cancellation) return;
        _pick = null;
        if (_pickButton is { } button) button.Content = _pickLabel;
        _pickButton = null; _pickLabel = null;
        cancellation.Cancel();
        if (!_disposed) Feedback.Text = "Target pick cancelled.";
    }

    internal void ApplyWindowCapture(WindowSelector target)
    {
        if (_disposed) return;
        _populating = true;
        try
        {
            Executable.Text = target.ExecutablePath; Class.Text = target.WindowClass; Title.Text = target.Title;
            TitleRule.SelectedIndex = (int)TitleMatch.Exact; IgnoreCase.IsChecked = false;
        }
        finally { _populating = false; }
        Change();
    }

    internal void ApplyPixelCapture(WaitTargetCapture capture)
    {
        if (_disposed || capture.Pixel is not { } pixel) return;
        _populating = true;
        try
        {
            Source.SelectedIndex = 1;
            Coordinates.SelectedIndex = (int)PixelCoordinates.DesktopPhysical;
            X.Text = pixel.X.ToString(CultureInfo.InvariantCulture); Y.Text = pixel.Y.ToString(CultureInfo.InvariantCulture);
            Rgb.Text = pixel.Rgb.ToString("X6", CultureInfo.InvariantCulture);
            // A new-window trigger has no pixel meaning; other trigger/timing choices survive.
            if (Trigger.SelectedIndex < 0 || Trigger.SelectedIndex == (int)WaitTrigger.NewWindow) Trigger.SelectedIndex = (int)WaitTrigger.IsTrue;
        }
        finally { _populating = false; }
        UpdateFields(); Change();
    }
}
