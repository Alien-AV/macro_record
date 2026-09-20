using System.Diagnostics;
using MacroRecorderGUI.Models;

namespace MacroRecorderGUI.ViewModels;

public partial class MainWindowViewModel
{
    private readonly PlaybackWorkflow _playbackWorkflow;
    private bool _usingPlaybackWorkflow;
    private readonly Stopwatch _recordingClock = new();
    private readonly Dictionary<ulong, (MacroViewModel Macro, TaskCompletionSource Completion)> _recordingDrains = [];
    private MacroViewModel? _recordingMacro;
    private int _recordedEventCount;
    public bool EmergencyStopAvailable { get; private set; } = true;
    public string? EmergencyStopError { get; private set; }
    public bool IsRecording => _recordingSession is not null;
    public bool IsFinalizingRecording => _recordingDrains.Count != 0 && !IsRecording;
    public MacroViewModel? RecordingMacro => _recordingMacro;
    public TimeSpan RecordingElapsed => _recordingClock.Elapsed;
    public int RecordedEventCount => _recordedEventCount;
    public PlaybackState PlaybackState => _playbackWorkflow.State;
    public bool CanRecord => !_disposed && !_shuttingDown && EmergencyStopAvailable && _playingMacro is null && !IsRecording;
    public bool CanPlay => !_disposed && !_shuttingDown && EmergencyStopAvailable && _playingMacro is null
        && !IsRecording && !IsFinalizingRecording;

    public Task PlayActiveMacro(PlaybackOptions options) => ActiveMacro is { } macro
        ? PlayMacroAsync(macro, options) : Task.CompletedTask;

    public void SetEmergencyStopAvailability(bool available, string? error = null)
    {
        EmergencyStopAvailable = available;
        EmergencyStopError = available ? null : error ?? "Register an emergency-stop shortcut before recording or playback.";
        OnPropertyChanged(nameof(EmergencyStopAvailable));
        OnPropertyChanged(nameof(EmergencyStopError));
        OnPropertyChanged(nameof(CanRecord));
        OnPropertyChanged(nameof(CanPlay));
        if (!available) EmergencyStop();
    }

    public void EmergencyStop()
    {
        AbortPlayback();
        StopRecording();
    }

    /// <summary>Call from the shell's UI timer to refresh live elapsed labels; performs no input work.</summary>
    public void RefreshWorkflowSummaries()
    {
        OnPropertyChanged(nameof(RecordingElapsed));
        OnPropertyChanged(nameof(PlaybackState));
    }

    public async Task StopRecordingAsync(ulong? autoDelay = null, RecordingStopCommand? command = null)
    {
        if (IsRecording && command is { } hotkey && !AcceptsRecordingStop(hotkey)) return;
        var drains = _recordingDrains.Values.Select(drain => drain.Completion.Task).ToArray();
        StopRecording(autoDelay, command);
        if (IsRecording) throw new InvalidOperationException("Recording could not be stopped.");
        await Task.WhenAll(drains);
    }

    private void NotifyRecordingState()
    {
        OnPropertyChanged(nameof(IsRecording)); OnPropertyChanged(nameof(IsFinalizingRecording));
        OnPropertyChanged(nameof(RecordingMacro)); OnPropertyChanged(nameof(RecordedEventCount));
        OnPropertyChanged(nameof(RecordingElapsed)); OnPropertyChanged(nameof(CanRecord)); OnPropertyChanged(nameof(CanPlay));
    }

    private bool HasRecordingDrain(MacroViewModel macro) => _recordingDrains.Values.Any(drain => ReferenceEquals(drain.Macro, macro));

    private void PlaybackStateChanged(PlaybackState state)
    {
        var version = _playbackVersion;
        InvokeDispatcher(() =>
        {
            if (_disposed || version != _playbackVersion) return;
            OnPropertyChanged(nameof(PlaybackState));
            OnPropertyChanged(nameof(CanRecord)); OnPropertyChanged(nameof(CanPlay));
        });
    }
}
