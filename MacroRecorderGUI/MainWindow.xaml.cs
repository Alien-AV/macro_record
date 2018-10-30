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
            RecordPlaybackDll.StartCapture();
        }

        private void StopRecord_Click(object sender, RoutedEventArgs e)
        {
            RecordPlaybackDll.StopCapture();
            if (AutoChangeDelay.IsChecked == true) ChangeDelays_Click(sender, e);
        }

        private void PlayEvents_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.ActiveMacro.PlayMacro();
        }

        private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //TODO: the need to handle this manually disgusts me, however there's no native way in wpf to bind to SelectedItems. another possible WA:
            // when Events will be wrapped in a presentable class, it can have a "isSelected" property which is bound per item in the list
            // (not sure about the overhead of such a mass binding)
            foreach (ProtobufGenerated.InputEvent addedItem in e.AddedItems)
            {
                ((sender as ListBox)?.DataContext as MainWindowViewModel.MacroTab)?.Macro.SelectedEvents.Add(addedItem);
            }
            foreach (ProtobufGenerated.InputEvent removedItem in e.RemovedItems)
            {
                ((sender as ListBox)?.DataContext as MainWindowViewModel.MacroTab)?.Macro.SelectedEvents.Remove(removedItem);
            }
        }

        private void RemoveEvent_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.ActiveMacro.RemoveSelectedEvents();
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
            (DataContext as MainWindowViewModel)?.ActiveMacro.Clear();
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
            (DataContext as MainWindowViewModel)?.ActiveMacro.ChangeDelays(timeIncrement);
        }
        
        private void SaveEvents_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.ActiveMacro.SaveToFile(); //TODO: inject "filesaver" or whatever
        }

        private void LoadEvents_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.ActiveMacro.LoadFromFile(); //TODO: inject "fileloader" or whatever
        }

        private void AbortPlayback_Click(object sender, RoutedEventArgs e)
        {
            RecordPlaybackDll.PlaybackEventAbort();
        }

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.AddNewTab();
        }
    }
}
