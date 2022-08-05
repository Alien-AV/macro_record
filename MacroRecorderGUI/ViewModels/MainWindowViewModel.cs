using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using RecordPlaybackDLLEnums;
using MacroRecorderGUI.Commands;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;

namespace MacroRecorderGUI.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
        {
            MacroTabs = new ObservableCollection<MacroViewModel> {new MacroViewModel("macro0")};
            _recordEngine = new RecordEngine();
            _recordEngine.RecordStatus += _recordEngine_RecordStatus;
            _recordEngine.RecordedEvent += _recordEngine_RecordedEvent;
        }

        #region record engine event handlers
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly RecordEngine _recordEngine;
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
            Application.Current.Dispatcher.Invoke(()=> ActiveMacro?.AddEvent(InputEvent.CreateInputEvent(e.InputEvent)));
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
            MacroTabs.Add(new MacroViewModel($"macro{MacroTabs.Count}"));
            SelectedTabIndex = MacroTabs.Count - 1;
        }
    }
}