using System.Collections.ObjectModel;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;

namespace MacroRecorderGUI.ViewModels;

public interface IMainWindowViewModel
{
    ObservableCollection<MacroViewModel> MacroTabs { get; }
    int SelectedTabIndex { get; set; }
    MacroViewModel? ActiveMacro { get; }
}

public class MainWindowViewModel : ViewModelBase, IMainWindowViewModel, IDisposable
{
    private readonly SynchronizationContext? _uiContext;
    private int _selectedTabIndex;
    private bool _loopPlayback;
    private bool _disposed;
    private long _playbackVersion;
    private MacroViewModel? _playingMacro;
    private Task? _playbackCompletion;
    private RecordingSession? _recordingSession;
    private ulong _latestRecordingId;

    public MainWindowViewModel()
        : this(new RecordEngine(), new PlaybackEngine())
    {
    }

    public MainWindowViewModel(IRecordEngine recordEngine, IPlaybackEngine playbackEngine)
    {
        _uiContext = SynchronizationContext.Current;
        RecordEngine = recordEngine;
        PlaybackEngine = playbackEngine;
        RecordEngine.RecordStatus += RecordEngineOnRecordStatus;
        RecordEngine.RecordedEvent += RecordEngineOnRecordedEvent;
        RecordEngine.RecordingEnded += RecordEngineOnRecordingEnded;
        MacroTabs = new ObservableCollection<MacroViewModel>
        {
            new("macro0", PlaybackEngine, PlayMacroAsync)
        };
        MacroTabs[0].ContentReplaced += MacroContentReplaced;
    }

    public IRecordEngine RecordEngine { get; }
    public IPlaybackEngine PlaybackEngine { get; }
    public ObservableCollection<MacroViewModel> MacroTabs { get; }

    public event EventHandler<string>? StatusMessageRequested;

    public MacroViewModel? PlayingMacro => _playingMacro;

    public Task PlayActiveMacro() => ActiveMacro is { } macro ? PlayMacroAsync(macro) : Task.CompletedTask;

    private async Task PlayMacroAsync(MacroViewModel macro)
    {
        if (_disposed) return;
        if (_playingMacro is not null)
        {
            StatusMessageRequested?.Invoke(this, "Playback is already running. Abort it before starting another macro.");
            return;
        }
        if (macro.Events.Count == 0 || !MacroTabs.Contains(macro)) return;
        var version = ++_playbackVersion;
        _playingMacro = macro;
        OnPropertyChanged(nameof(PlayingMacro));
        string? completionMessage = null;
        try
        {
            var completion = PlaybackEngine.PlaybackEventsAsync(macro.Events.ToArray(), LoopPlayback);
            _playbackCompletion = completion;
            StatusMessageRequested?.Invoke(this, $"Playing {macro.Name}");
            await completion;
            completionMessage = $"Playback finished: {macro.Name}";
        }
        catch (OperationCanceledException) { }
        // AbortPlayback reports this failure synchronously, before another macro
        // can start. Do not duplicate it from a delayed task continuation.
        catch (PlaybackStoppedException) { }
        catch (Exception error)
        {
            completionMessage = $"Could not play macro: {error.Message}";
        }
        finally
        {
            InvokeDispatcher(() =>
            {
                if (version == _playbackVersion)
                {
                    // Consume the completion and its status in one UI callback.
                    // Abort can inspect the task until this callback runs.
                    ClearPlaybackOwnership();
                    if (!_disposed && completionMessage is not null)
                        StatusMessageRequested?.Invoke(this, completionMessage);
                }
            });
        }
    }

    public void AbortPlayback()
    {
        if (_disposed || _playingMacro is null) return;
        try
        {
            PlaybackEngine.PlaybackEventAbort();
            // Poll may have already collected a native failure and released its
            // worker while the UI continuation is still queued. Consume that
            // failure before invalidating the old completion's version.
            var failure = _playbackCompletion is { IsFaulted: true } completion
                ? completion.Exception!.GetBaseException()
                : null;
            CompleteAbort(failure);
        }
        catch (PlaybackStoppedException error)
        {
            CompleteAbort(error);
        }
        catch (Exception error)
        {
            StatusMessageRequested?.Invoke(this, $"Could not abort playback: {error.Message}");
        }
    }

    private void CompleteAbort(Exception? error)
    {
        ClearPlaybackOwnership();
        StatusMessageRequested?.Invoke(this, error is null
            ? "Playback aborted"
            : $"Playback stopped with an error: {error.Message}");
    }

    private void ClearPlaybackOwnership()
    {
        ++_playbackVersion;
        _playingMacro = null;
        _playbackCompletion = null;
        OnPropertyChanged(nameof(PlayingMacro));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_playbackVersion;
        RecordEngine.RecordStatus -= RecordEngineOnRecordStatus;
        RecordEngine.RecordedEvent -= RecordEngineOnRecordedEvent;
        RecordEngine.RecordingEnded -= RecordEngineOnRecordingEnded;
        RecordEngine.Dispose();
        foreach (var macro in MacroTabs) macro.ContentReplaced -= MacroContentReplaced;
        PlaybackEngine.Dispose();
        _playingMacro = null;
        _playbackCompletion = null;
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (value == _selectedTabIndex)
            {
                return;
            }

            _selectedTabIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveMacro));
        }
    }

    public bool LoopPlayback
    {
        get => _loopPlayback;
        set
        {
            if (value == _loopPlayback)
            {
                return;
            }

            _loopPlayback = value;
            try { PlaybackEngine.SetLoopPlayback(value); }
            catch (Exception error)
            {
                StatusMessageRequested?.Invoke(this, $"Could not change playback loop: {error.Message}");
            }
            OnPropertyChanged();
        }
    }

    public MacroViewModel? ActiveMacro =>
        SelectedTabIndex >= 0 && SelectedTabIndex < MacroTabs.Count
            ? MacroTabs[SelectedTabIndex]
            : null;

    public MacroViewModel AddNewTab()
    {
        var macro = new MacroViewModel($"macro{MacroTabs.Count}", PlaybackEngine, PlayMacroAsync);
        macro.ContentReplaced += MacroContentReplaced;
        MacroTabs.Add(macro);
        SelectedTabIndex = MacroTabs.Count - 1;
        return macro;
    }

    public void CloseTab(MacroViewModel macro)
    {
        var removedIndex = MacroTabs.IndexOf(macro);
        if (removedIndex < 0)
        {
            return;
        }

        var selectedMacro = ActiveMacro;
        if (ReferenceEquals(_playingMacro, macro)) AbortPlayback();
        if (_recordingSession?.Context is RecordingTarget target && ReferenceEquals(target.Macro, macro)) StopRecording();
        macro.ContentReplaced -= MacroContentReplaced;
        MacroTabs.RemoveAt(removedIndex);
        if (MacroTabs.Count == 0)
        {
            SelectedTabIndex = -1;
        }
        else if (ReferenceEquals(selectedMacro, macro))
        {
            SelectedTabIndex = Math.Min(removedIndex, MacroTabs.Count - 1);
        }
        else if (selectedMacro is not null)
        {
            SelectedTabIndex = MacroTabs.IndexOf(selectedMacro);
        }
    }

    public void SynchronizeTabs(IReadOnlyList<MacroViewModel> orderedMacros, MacroViewModel? selectedMacro)
    {
        // A reorder can report removal before reinsertion. Wait for the complete tab set.
        if (orderedMacros.Count != MacroTabs.Count
            || orderedMacros.Distinct().Count() != MacroTabs.Count
            || orderedMacros.Any(macro => !MacroTabs.Contains(macro)))
        {
            return;
        }

        var selection = selectedMacro ?? ActiveMacro;
        for (var targetIndex = 0; targetIndex < orderedMacros.Count; targetIndex++)
        {
            var currentIndex = MacroTabs.IndexOf(orderedMacros[targetIndex]);
            if (currentIndex >= 0 && currentIndex != targetIndex)
            {
                MacroTabs.Move(currentIndex, targetIndex);
            }
        }

        SelectedTabIndex = selection is null ? -1 : MacroTabs.IndexOf(selection);
        OnPropertyChanged(nameof(ActiveMacro));
    }

    private void RecordEngineOnRecordStatus(object? sender, RecordEngine.RecordStatusEventArgs e)
    {
        InvokeDispatcher(() =>
            StatusMessageRequested?.Invoke(this, $"Status reported: \"{e.StatusCode}\"."));
    }

    private void RecordEngineOnRecordedEvent(object? sender, RecordEngine.RecordEventsEventArgs e)
    {
        InvokeDispatcher(() =>
        {
            if (e.Session.Context is not RecordingTarget target || !IsCurrentTarget(target)) return;
            var input = InputEvent.CreateInputEvent(e.InputEvent);
            target.Macro.AddEvent(input);
            if (target.Delay is not null) target.DelayEvents.Add(input);
        });
    }

    public bool StartRecording(bool fromHotkey = false, bool clear = false)
    {
        if (_disposed || _recordingSession is not null || ActiveMacro is not { } macro) return false;
        if (clear) macro.Clear();
        var session = new RecordingSession(fromHotkey, new RecordingTarget(macro, macro.ContentRevision));
        return StartRecordingSession(session);
    }

    private bool StartRecordingSession(RecordingSession session)
    {
        var macro = ((RecordingTarget)session.Context!).Macro;
        _recordingSession = session;
        _latestRecordingId = session.Id;
        try
        {
            if (!RecordEngine.StartRecord(session))
            {
                _recordingSession = null;
                return false;
            }
            StatusMessageRequested?.Invoke(this, $"Recording {macro.Name}");
            return true;
        }
        catch (Exception error)
        {
            _recordingSession = null;
            StatusMessageRequested?.Invoke(this, $"Could not start recording: {error.Message}");
            return false;
        }
    }

    public void StopRecording(ulong? autoDelay = null)
    {
        if (_disposed || _recordingSession is not { } session) return;
        var target = (RecordingTarget)session.Context!;
        target.Delay = autoDelay;
        if (autoDelay is not null && IsCurrentTarget(target)) target.DelayEvents.UnionWith(target.Macro.Events);
        try
        {
            RecordEngine.StopRecord();
            _recordingSession = null;
        }
        catch (Exception error)
        {
            target.Delay = null;
            target.DelayEvents.Clear();
            StatusMessageRequested?.Invoke(this, $"Could not stop recording: {error.Message}");
        }
    }

    private void MacroContentReplaced(object? sender, EventArgs args)
    {
        if (_disposed || _recordingSession is not { } previous || previous.Context is not RecordingTarget target
            || !ReferenceEquals(target.Macro, sender)) return;
        StopRecording();
        if (_recordingSession is null)
            StartRecordingSession(previous.Continue(new RecordingTarget(target.Macro, target.Macro.ContentRevision)));
    }

    private bool IsCurrentTarget(RecordingTarget target) => !_disposed
        && MacroTabs.Contains(target.Macro) && target.Macro.ContentRevision == target.Revision;

    private void RecordEngineOnRecordingEnded(RecordingSession session, Exception? error)
    {
        InvokeDispatcher(() =>
        {
            if (_disposed) return;
            if (session.Context is RecordingTarget target && IsCurrentTarget(target) && target.Delay is { } delay)
            {
                foreach (var input in target.Macro.Events.Where(target.DelayEvents.Contains)) input.TimeSinceLastEvent = delay;
            }
            if (ReferenceEquals(_recordingSession, session)) _recordingSession = null;
            if (_latestRecordingId == session.Id)
                StatusMessageRequested?.Invoke(this, error is null ? "Recording stopped" : $"Could not record: {error.Message}");
        });
    }

    private sealed class RecordingTarget(MacroViewModel macro, long revision)
    {
        public MacroViewModel Macro { get; } = macro;
        public long Revision { get; } = revision;
        public ulong? Delay { get; set; }
        public HashSet<InputEvent> DelayEvents { get; } = [];
    }

    protected virtual void InvokeDispatcher(Action action)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }
}
