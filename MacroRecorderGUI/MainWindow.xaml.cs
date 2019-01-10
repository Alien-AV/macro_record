using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUI
{
    public partial class MainWindow : Window
    {
        private GlobalHotkeys _globalHotkeys;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _globalHotkeys = new GlobalHotkeys(this);
        }

        internal void StartRecord_Click(object sender, RoutedEventArgs e)
        {
            if (ClearListOnStartRecord.IsChecked == true) ClearList_Click(sender, e);
            RecordPlaybackDll.StartRecord();
        }

        internal void StopRecord_Click(object sender, RoutedEventArgs e)
        {
            RecordPlaybackDll.StopRecord();
            if (AutoChangeDelay.IsChecked == true) ChangeDelays_Click(sender, e);
        }

        internal void PlayEvents_Click(object sender, RoutedEventArgs e)
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

        internal void AbortPlayback_Click(object sender, RoutedEventArgs e)
        {
            RecordPlaybackDll.PlaybackEventAbort();
        }

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MainWindowViewModel)?.AddNewTab();
        }

        private void TabItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!(e.Source is TabItem tabItem)) return;

            if (Mouse.PrimaryDevice.LeftButton == MouseButtonState.Pressed)
            {
                DragDrop.DoDragDrop(tabItem, tabItem, DragDropEffects.All);
            }
        }

        private void TabItem_Drop(object sender, DragEventArgs e)
        {
            if (!(e.Source is TabItem tabItemTarget) || !(tabItemTarget.DataContext is MacroViewModel macroViewModelTarget)) return;
            if (!(e.Data.GetData(typeof(TabItem)) is TabItem tabItemSource) || !(tabItemSource.DataContext is MacroViewModel macroViewModelSource)) return;
            if (!(TabControl.DataContext is MainWindowViewModel mainWindowViewModel)) return;

            if (macroViewModelTarget == macroViewModelSource) return;

            var targetIndex = mainWindowViewModel.MacroTabs.IndexOf(macroViewModelTarget);
            mainWindowViewModel.MacroTabs.Remove(macroViewModelSource);
            mainWindowViewModel.MacroTabs.Insert(targetIndex, macroViewModelSource);
            mainWindowViewModel.SelectedTabIndex = targetIndex;
        }
    }
}
