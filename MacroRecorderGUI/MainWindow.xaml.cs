using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using MacroRecorderGUI.ViewModels;

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
            (DataContext as MainWindowViewModel)?.ActiveMacro.PlayMacro();
        }

        private void RemoveEvent_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine(sender.GetType());
            // TODO: broken. TabControl.SelectedContent is actually a MacroTab type. how to get a the specific instance of the tab content template and fetch the ListBox from it?
            // various WAs
            // 1. eventually when Events are wrapped in a presentable class, it will have a "isSelected" property which will be bound to each item
            // 2. SelectionChanged="ListBox_SelectionChanged" and edit a SelectedItems on the bound macro manually in codebehind?
//            var selectedEvents = (TabControl.SelectedContent as ListBox)?.SelectedItems.Cast<ProtobufGenerated.InputEvent>().ToList();
//            (DataContext as MainWindowViewModel)?.ActiveMacro.RemoveSelectedEvents(selectedEvents);
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
            InjectAndCaptureDll.InjectEventAbort();
        }

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.AddNewTab();
        }
    }
}
