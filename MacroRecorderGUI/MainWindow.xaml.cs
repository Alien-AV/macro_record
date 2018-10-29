using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using MacroRecorderGUI.Models;

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
            InjectAndCaptureDll.StartCapture();
        }

        private void StopRecord_Click(object sender, RoutedEventArgs e)
        {
            InjectAndCaptureDll.StopCapture();
            if (AutoChangeDelay.IsChecked == true) ChangeDelays_Click(sender, e);
        }

        private void PlayEvents_Click(object sender, RoutedEventArgs e)
        {
            (TabControl.SelectedContent as MainWindowModel.MacroTab).Macro.PlayMacro();
        }

        private void RemoveEvent_Click(object sender, RoutedEventArgs e)
        {
//            var selectedItems = EventsListBox.SelectedItems.Cast<ProtobufGenerated.InputEvent>().ToList();
//            _currentMacro.RemoveEvents(selectedItems);
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
            (TabControl.SelectedContent as MainWindowModel.MacroTab).Macro.Clear();
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
            (TabControl.SelectedContent as MainWindowModel.MacroTab).Macro.ChangeDelays(timeIncrement);
        }
        
        private void SaveEvents_Click(object sender, RoutedEventArgs e)
        {
            (TabControl.SelectedContent as MainWindowModel.MacroTab).Macro.SaveToFile(); //TODO: inject "filesaver" or whatever
        }

        private void LoadEvents_Click(object sender, RoutedEventArgs e)
        {
            (TabControl.SelectedContent as MainWindowModel.MacroTab).Macro.LoadFromFile(); //TODO: inject "fileloader" or whatever
        }

        private void AbortPlayback_Click(object sender, RoutedEventArgs e)
        {
            InjectAndCaptureDll.InjectEventAbort();
        }

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowModel).AddNewTab();
        }
    }
}
