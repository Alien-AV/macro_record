using System;
using System.Collections.ObjectModel;
using System.Windows;
using RecordPlaybackDLLEnums;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;

namespace MacroRecorderGUI.ViewModels
{
    public interface IMainWindowViewModel //just here to make sure DesignTimeMainWindowViewModel stays in sync
    {
        ObservableCollection<MacroViewModel> MacroTabs { get; set; }
        int SelectedTabIndex { get; set; }
        MacroViewModel ActiveMacro { get; }
    }

    public class MainWindowViewModel : ViewModelBase, IMainWindowViewModel
    {
        public MainWindowViewModel():this(new RecordEngine(), new PlaybackEngine())
        {
        }

        public MainWindowViewModel(IRecordEngine recordEngine, IPlaybackEngine playbackEngine)
        {
            RecordEngine = recordEngine;
            PlaybackEngine = playbackEngine;
            RecordEngine.RecordStatus += _recordEngine_RecordStatus;
            RecordEngine.RecordedEvent += _recordEngine_RecordedEvent;
            MacroTabs = new ObservableCollection<MacroViewModel> { new MacroViewModel("macro0", PlaybackEngine) };
        }

        public readonly IRecordEngine RecordEngine;
        public readonly IPlaybackEngine PlaybackEngine;

        #region record engine event handlers
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private void _recordEngine_RecordStatus(object sender, RecordEngine.RecordStatusEventArgs e)
        {
            if (e.StatusCode == StatusCode.PlaybackFinished)
            {
                if (LoopPlayback) ActiveMacro?.PlayMacro();
            }
            else
            {
                MessageBox.Show("Status reported: \"" + e.StatusCode + "\".");
            }
        }
        private void _recordEngine_RecordedEvent(object sender, RecordEngine.RecordEventsEventArgs e)
        {
            InvokeDispatcher(()=> ActiveMacro?.AddEvent(InputEvent.CreateInputEvent(e.InputEvent)));
        }

        protected virtual void InvokeDispatcher(Action action)
        {
            Application.Current.Dispatcher.Invoke(action);
        }
        #endregion
        
        public ObservableCollection<MacroViewModel> MacroTabs { get; set; }
        
        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { _selectedTabIndex = value; OnPropertyChanged();}
        }

        public bool LoopPlayback { get; set; }
        public MacroViewModel ActiveMacro => (SelectedTabIndex != -1)? MacroTabs[SelectedTabIndex] : null;

        public void AddNewTab()
        {
            MacroTabs.Add(new MacroViewModel($"macro{MacroTabs.Count}", PlaybackEngine));
            SelectedTabIndex = MacroTabs.Count - 1;
        }
    }
}