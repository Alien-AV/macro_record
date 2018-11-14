using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;

namespace MacroRecorderGUI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void StartRecord_Click(object sender, RoutedEventArgs e)
        {
            if (ClearListOnStartRecord.IsChecked == true) ClearList_Click(sender, e);
            RecordPlaybackDll.StartRecord();
        }

        private void StopRecord_Click(object sender, RoutedEventArgs e)
        {
            RecordPlaybackDll.StopRecord();
            if (AutoChangeDelay.IsChecked == true) ChangeDelays_Click(sender, e);
        }

        private void PlayEvents_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.ActiveMacro?.PlayMacro();
        }

        private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //TODO: the need to handle this manually disgusts me, however there's no native way in wpf to bind to SelectedItems. another possible WA:
            // when Events will be wrapped in a presentable class, it can have a "isSelected" property which is bound per item in the list
            // (not sure about the overhead of such a mass binding)
            foreach (ProtobufGenerated.InputEvent addedItem in e.AddedItems)
            {
                ((sender as ListBox)?.DataContext as MacroViewModel)?.SelectedEvents.Add(addedItem);
            }
            foreach (ProtobufGenerated.InputEvent removedItem in e.RemovedItems)
            {
                ((sender as ListBox)?.DataContext as MacroViewModel)?.SelectedEvents.Remove(removedItem);
            }
        }

        private void RemoveEvent_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.ActiveMacro?.RemoveSelectedEvents();
        }

        private void EventsListBox_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveEvent_Click(sender, e);
            }
        }

        private void ClearList_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.ActiveMacro?.Clear();
        }

        private void AllowOnlyNumbersInTextBox(object sender, TextCompositionEventArgs e)
        {
            var regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void ChangeDelays_Click(object sender, RoutedEventArgs e)
        {
            if (!DelayTextBox.Text.Any()) return;
            var timeIncrement = Convert.ToUInt64(DelayTextBox.Text);
            (DataContext as MainWindowViewModel)?.ActiveMacro?.ChangeDelays(timeIncrement);
        }
        
        private void SaveEvents_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.ActiveMacro?.SaveToFile();
        }

        private void LoadEvents_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.AddNewTab();
            (DataContext as MainWindowViewModel)?.ActiveMacro?.LoadFromFile();
        }

        private void AbortPlayback_Click(object sender, RoutedEventArgs e)
        {
            RecordPlaybackDll.PlaybackEventAbort();
        }

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.AddNewTab();
        }

        private void CaptureEvent_Click(object sender, RoutedEventArgs e)
        {
            this.KeyDown += new KeyEventHandler(Form1_KeyDown);
            this.KeyUp += new KeyEventHandler(Form1_KeyDown);
        }
        static private ulong GetCuurentTimestamp(ObservableCollection<InputEvent> events)
        {
            return events.Count > 0 ? events[events.Count - 1].TimeSinceStartOfRecording : 0;
        }
        void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            InputEvent capturedKey = new InputEvent
            {
                KeyboardEvent = new InputEvent.Types.KeyboardEventType
                {
                    KeyUp = e.IsUp,
                    VirtualKeyCode = Convert.ToUInt32(KeyInterop.VirtualKeyFromKey(e.Key))
                },
                TimeSinceStartOfRecording = GetCuurentTimestamp((DataContext as MainWindowViewModel)?.ActiveMacro?.Events)
            };
            if(e.IsUp == true)
            {
                this.KeyUp -= new KeyEventHandler(Form1_KeyDown);
                this.KeyDown -= new KeyEventHandler(Form1_KeyDown);
            }
            (DataContext as MainWindowViewModel)?.ActiveMacro?.AddEvent(capturedKey);       
        }
        
    }
}
