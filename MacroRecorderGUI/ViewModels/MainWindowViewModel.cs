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

public class MainWindowViewModel : ViewModelBase, IMainWindowViewModel
{
    private readonly SynchronizationContext? _uiContext;
    private int _selectedTabIndex;
    private bool _loopPlayback;

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
            new("macro0", PlaybackEngine)
        };
    }

    public IRecordEngine RecordEngine { get; }
    public IPlaybackEngine PlaybackEngine { get; }
    public ObservableCollection<MacroViewModel> MacroTabs { get; }

    public event EventHandler<string>? StatusMessageRequested;

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
            OnPropertyChanged();
        }
    }

    public MacroViewModel? ActiveMacro =>
        SelectedTabIndex >= 0 && SelectedTabIndex < MacroTabs.Count
            ? MacroTabs[SelectedTabIndex]
            : null;

    public MacroViewModel AddNewTab()
    {
        var macro = new MacroViewModel($"macro{MacroTabs.Count}", PlaybackEngine);
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
            if (LoopPlayback)
            {
                ActiveMacro?.PlayMacro();
            }

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
