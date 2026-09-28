using MacroRecorderGUI.Models;

namespace MacroRecorderGUI;

public sealed partial class MainWindow
{
    private readonly IWaitTargetPicker _waitPicker;
    private readonly IWaitCapturePreferenceStore _capturePreferenceStore;
    private readonly bool _loadCapturePreferences;
    private readonly CancellationTokenSource _captureLifetime = new();
    private WaitCaptureConfiguration _captureConfiguration = WaitCaptureConfiguration.Default;
    private WaitCaptureHotkeys? _captureHotkeys;
    private bool _capturePreferencesLoaded, _captureCommandPending, _captureSettingsApplying;
    private string _captureShortcutStatus = "Capture shortcuts are off until enabled in Settings.";
    private string? _captureRunNotice;

    private void ReportWaitCaptureStatus(string message)
    {
        if (ViewModel.IsRecording || ViewModel.IsFinalizingRecording) _captureRunNotice = message;
        SetMessage(message);
    }

    private async Task InitializeWaitSourcePreferencesAsync()
    {
        if (_waitSourcePreferences is null) return;
        await _waitSourcePreferences.InitializeAsync(_captureLifetime.Token);
        if (!_closed && !_closing) RefreshShell();
    }

    private async Task InitializeCapturePreferencesAsync()
    {
        if (_capturePreferencesLoaded) return;
        // Hidden test windows explicitly opt out of loading real local preferences.
        var data = _loadCapturePreferences ? await _capturePreferenceStore.LoadAsync(_captureLifetime.Token)
            : new WaitCapturePreferenceData(WaitCaptureConfiguration.Default);
        if (_closed || _closing) return;
        _captureConfiguration = data.Configuration;
        _capturePreferencesLoaded = true;
        InitializeCaptureHotkeys();
        if (data.Warning is { } warning) SetMessage(_captureShortcutStatus = warning);
    }

    private void InitializeCaptureHotkeys()
    {
        if (_globalHotkeys is null || !_capturePreferencesLoaded || _captureHotkeys is not null || _closed || _closing) return;
        _captureHotkeys = new WaitCaptureHotkeys(_globalHotkeys, CaptureWaitHotkey);
        if (!_captureHotkeys.TryApply(_captureConfiguration, out var error))
            SetMessage(_captureShortcutStatus = error ?? "Capture shortcuts could not be registered.");
        else _captureShortcutStatus = DescribeCaptureShortcuts();
        SynchronizeRecordingCaptureBindings();
    }

    private string DescribeCaptureShortcuts() => _captureHotkeys?.RegisteredBindings.Count is > 0
        ? "Capture shortcuts are registered. While stopped they open a draft; while recording they insert a wait without a dialog."
        : "Capture shortcuts are off.";

    private void SynchronizeRecordingCaptureBindings()
    {
        ViewModel.RegisteredRecordingCaptures = _captureHotkeys?.RegisteredBindings
            .Select(pair => new RecordingCaptureGesture((uint)pair.Gesture.Modifiers, (uint)pair.Gesture.Key)).ToArray() ?? [];
    }

    private bool PrepareRecordingCaptureBindings()
    {
        if (_captureSettingsApplying) { SetMessage("Wait for shortcut settings to finish saving before recording."); return false; }
        if (!_capturePreferencesLoaded)
        { SetMessage("Capture shortcut preferences are still loading. Try Record when they are ready."); return false; }
        if (_captureHotkeys is { IsReady: false, HasRegistrations: true }
            || _captureConfiguration.Bindings().Any(pair => pair.Binding.Enabled) && _captureHotkeys?.IsReady != true)
        {
            SetMessage("Capture shortcut registration is incomplete. Review and save shortcuts in Settings before recording.");
            return false;
        }
        SynchronizeRecordingCaptureBindings();
        return true;
    }

    private async Task<string?> ApplyShortcutSettingsAsync(int stopIndex, WaitCaptureConfiguration configuration)
    {
        if (_closed || _closing || RunActive || _captureSettingsApplying) return "Shortcut settings can be changed after the run or current save ends.";
        _captureSettingsApplying = true; RefreshShell();
        try { return await ApplyShortcutSettingsCoreAsync(stopIndex, configuration); }
        finally { _captureSettingsApplying = false; _captureHotkeys?.DiscardPendingMessages(); RefreshShell(); }
    }

    private async Task<string?> ApplyShortcutSettingsCoreAsync(int stopIndex, WaitCaptureConfiguration configuration)
    {
        if (stopIndex < 0 || stopIndex >= KeyboardShortcuts.EmergencyStopChoices.Count) return "Choose an emergency-stop shortcut.";
        if (_globalHotkeys is null || _captureHotkeys is null) return "Global shortcut registration is unavailable. Reopen the app and try again.";
        configuration.Validate();
        var previous = _captureConfiguration;
        if (!_captureHotkeys.TryApply(configuration, out var registrationError))
        { SynchronizeRecordingCaptureBindings(); return _captureShortcutStatus = registrationError ?? "Capture registration failed."; }
        try { await _capturePreferenceStore.SaveAsync(configuration, _captureLifetime.Token); }
        catch (Exception error)
        {
            if (!_closed && !_closing)
            {
                var restored = _captureHotkeys.TryApply(previous, out _);
                if (!restored) _captureHotkeys.DisableUntilReconfigured();
                SynchronizeRecordingCaptureBindings();
                return _captureShortcutStatus = "Capture shortcuts could not be saved. " + error.Message
                    + (restored ? " Previous settings remain active." : " Previous shortcut registrations could not be restored. Capture is disabled until shortcut settings are saved successfully.");
            }
            _captureHotkeys?.DisableUntilReconfigured();
            SynchronizeRecordingCaptureBindings();
            return _captureShortcutStatus = "Capture shortcuts could not be saved while closing. Capture is disabled until shortcut settings are saved successfully.";
        }
        _captureConfiguration = configuration;
        if (_closed || _closing) return null;
        SynchronizeRecordingCaptureBindings();
        _captureShortcutStatus = DescribeCaptureShortcuts();
        var changed = _globalHotkeys.TrySetEmergencyStop(KeyboardShortcuts.EmergencyStopChoices[stopIndex],
            async command => await StopRunAsync(command), out var stopError);
        ViewModel.RegisteredRecordingStops = _globalHotkeys.RegisteredRecordingStops;
        ViewModel.SetEmergencyStopAvailability(_globalHotkeys.EmergencyStop is not null, stopError);
        RefreshShell();
        return changed ? null : "Capture settings saved. " + stopError;
    }

    private void CaptureWaitHotkey(WaitCaptureTarget target, uint messageTime)
    {
        if (_closed || _closing || _captureHotkeys?.IsReady != true) return;
        var binding = _captureHotkeys.RegisteredBindings.FirstOrDefault(pair => pair.Target == target);
        if (binding.Gesture is null) return;
        var gesture = new RecordingCaptureGesture((uint)binding.Gesture.Modifiers, (uint)binding.Gesture.Key);
        if (ViewModel.IsRecording)
        {
            // Every reserved native marker must be resolved, including failed sampling.
            var accepted = false; var queued = false;
            try
            {
                if (!ViewModel.AcceptsRecordingCapture(gesture, messageTime)) return;
                accepted = true;
                var captured = _waitPicker.Capture(target);
                if (!captured.Succeeded) { ReportWaitCaptureStatus(captured.Error ?? "The wait target is unavailable."); return; }
                var submission = ViewModel.AddCapturedWait(captured.CreateCondition(), gesture, messageTime);
                queued = submission == CapturedWaitSubmission.Queued;
                ReportWaitCaptureStatus(queued ? "Captured wait queued in the recording." : $"The wait was not inserted ({submission}).");
            }
            catch (Exception error) { ReportWaitCaptureStatus("The wait target could not be captured. " + error.Message); }
            finally
            {
                if (accepted && !queued)
                {
                    try
                    {
                        if (ViewModel.CancelCapturedWait(gesture, messageTime) == CapturedWaitSubmission.EnqueueFailed)
                            ReportWaitCaptureStatus("The capture marker cancellation could not be queued. Stop recording; unresolved capture will time out.");
                    }
                    catch (Exception error) { ReportWaitCaptureStatus("The capture marker could not be cancelled. Stop recording. " + error.Message); }
                }
            }
            return;
        }
        if (!WaitSourcesReady || RunActive || _busy || _savingRun || _stopping || _captureCommandPending || _captureSettingsApplying || _libraryVisible
            || ActiveEditor is not { } editor || editor.IsPreviewMode) return;
        try
        {
            // This snapshot precedes OperationAsync, draft commits, and opening the dialog.
            var captured = _waitPicker.Capture(target);
            if (!captured.Succeeded) { SetMessage(captured.Error ?? "The wait target is unavailable."); return; }
            _ = OpenCapturedWaitDraftAsync(editor, captured.CreateCondition());
        }
        catch (Exception error) { SetMessage("The wait target could not be captured. " + error.Message); }
    }

    private async Task OpenCapturedWaitDraftAsync(Views.MacroTabContent editor, ProtobufGenerated.WaitCondition condition)
    {
        if (!ReferenceEquals(ActiveEditor, editor) || !ReferenceEquals(editor.DataContext, ViewModel.ActiveMacro))
        { SetMessage("The destination recording changed during capture. Invoke the shortcut again in its editor."); return; }
        if (_closed || _closing || _busy || RunActive || _captureSettingsApplying) return;
        _captureCommandPending = true; _busy = true;
        try
        {
            // The editor anchors selection before committing drafts. Do not perform
            // a shell precommit or disable controls before it captures that anchor.
            var pending = editor.AddCapturedWaitAsync(condition);
            RefreshShell();
            await pending;
        }
        catch (Exception error) { if (!_closed) SetMessage("The captured wait draft could not be opened. " + error.Message); }
        finally { _captureCommandPending = false; _busy = false; _captureHotkeys?.DiscardPendingMessages(); RefreshShell(); }
    }
}
