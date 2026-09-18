using System.Collections.ObjectModel;
using RecordPlaybackDLLEnums;
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
        MacroTabs = new ObservableCollection<MacroViewModel>
        {
            new("macro0", PlaybackEngine, PlayMacroAsync)
        };
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
        try
        {
            var completion = PlaybackEngine.PlaybackEventsAsync(macro.Events.ToArray(), LoopPlayback);
            StatusMessageRequested?.Invoke(this, $"Playing {macro.Name}");
            await completion;
            InvokeDispatcher(() =>
            {
                if (!_disposed && version == _playbackVersion)
                    StatusMessageRequested?.Invoke(this, $"Playback finished: {macro.Name}");
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            InvokeDispatcher(() =>
            {
                if (!_disposed && version == _playbackVersion)
                    StatusMessageRequested?.Invoke(this, $"Could not play macro: {error.Message}");
            });
        }
        finally
        {
            InvokeDispatcher(() =>
            {
                if (version == _playbackVersion)
                {
                    _playingMacro = null;
                    OnPropertyChanged(nameof(PlayingMacro));
                }
            });
        }
    }

    public void AbortPlayback()
    {
        if (_disposed) return;
        try
        {
            PlaybackEngine.PlaybackEventAbort();
            ++_playbackVersion;
            _playingMacro = null;
            OnPropertyChanged(nameof(PlayingMacro));
            StatusMessageRequested?.Invoke(this, "Playback aborted");
        }
        catch (Exception error)
        {
            StatusMessageRequested?.Invoke(this, $"Could not abort playback: {error.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_playbackVersion;
        RecordEngine.RecordStatus -= RecordEngineOnRecordStatus;
        RecordEngine.RecordedEvent -= RecordEngineOnRecordedEvent;
        PlaybackEngine.Dispose();
        _playingMacro = null;
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
        if (e.StatusCode == StatusCode.PlaybackFinished)
        {
            return;
        }

        InvokeDispatcher(() =>
            StatusMessageRequested?.Invoke(this, $"Status reported: \"{e.StatusCode}\"."));
    }

    private void RecordEngineOnRecordedEvent(object? sender, RecordEngine.RecordEventsEventArgs e)
    {
        InvokeDispatcher(() => ActiveMacro?.AddEvent(InputEvent.CreateInputEvent(e.InputEvent)));
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
