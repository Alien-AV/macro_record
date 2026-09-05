using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using Windows.System;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;

namespace MacroRecorderGUI;

public sealed partial class MainWindow : Window
{
    private GlobalHotkeys? _globalHotkeys;
    private bool _synchronizingTabs;

    public MainWindow()
    {
        ViewModel = new MainWindowViewModel();
        InitializeComponent();
        RootGrid.DataContext = ViewModel;

        ViewModel.StatusMessageRequested += ViewModel_StatusMessageRequested;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;

        foreach (var macro in ViewModel.MacroTabs)
        {
            AddTab(macro, select: false);
        }

        MacroTabs.SelectedIndex = ViewModel.SelectedTabIndex;
        ResizeWindow();
    }

    public MainWindowViewModel ViewModel { get; }

    private void ResizeWindow()
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        AppWindow.GetFromWindowId(windowId).Resize(new SizeInt32(1180, 780));
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_globalHotkeys is not null)
        {
            return;
        }

        try
        {
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _globalHotkeys = new GlobalHotkeys(windowHandle);

            var shortcutsRegistered =
                _globalHotkeys.AddHotKey(VirtualKey.Q, HotKeyModifiers.Control, StartRecording)
                & _globalHotkeys.AddHotKey(VirtualKey.W, HotKeyModifiers.Control, StopRecording)
                & _globalHotkeys.AddHotKey(VirtualKey.E, HotKeyModifiers.Control, PlayEvents)
                & _globalHotkeys.AddHotKey(VirtualKey.R, HotKeyModifiers.Control, AbortPlayback);

            if (!shortcutsRegistered)
            {
                StatusText.Text = "One or more global shortcuts are already registered by another application.";
            }
        }
        catch (InvalidOperationException exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _globalHotkeys?.Dispose();
        _globalHotkeys = null;
        ViewModel.StatusMessageRequested -= ViewModel_StatusMessageRequested;
    }

    private void StartRecord_Click(object sender, RoutedEventArgs e)
    {
        StartRecording();
    }

    private void StopRecord_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
    }

    private void PlayEvents_Click(object sender, RoutedEventArgs e)
    {
        PlayEvents();
    }

    private void AbortPlayback_Click(object sender, RoutedEventArgs e)
    {
        AbortPlayback();
    }

    private void StartRecording()
    {
        if (ClearListOnStartRecord.IsChecked == true)
        {
            ViewModel.ActiveMacro?.Clear();
        }

        ViewModel.RecordEngine.StartRecord();
        StatusText.Text = "Recording";
    }

    private void StopRecording()
    {
        ViewModel.RecordEngine.StopRecord();

        if (AutoChangeDelay.IsChecked == true && TryGetDelay(out var delay))
        {
            ViewModel.ActiveMacro?.ChangeDelaysOnAll(delay);
        }

        StatusText.Text = "Recording stopped";
    }

    private void PlayEvents()
    {
        ViewModel.ActiveMacro?.PlayMacro();
        StatusText.Text = "Playback started";
    }

    private void AbortPlayback()
    {
        ViewModel.PlaybackEngine.PlaybackEventAbort();
        StatusText.Text = "Playback aborted";
    }

    private void CreateKeyboardEventManually_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveMacro?.CreateKeyboardEventManually();
    }

    private void CreateMouseEventManually_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveMacro?.CreateMouseEventManually();
    }

    private void RemoveEvent_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveMacro?.RemoveSelectedEvents();
    }

    private void ClearList_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveMacro?.Clear();
    }

    private void ChangeDelays_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetDelay(out var delay))
        {
            ViewModel.ActiveMacro?.ChangeDelaysOnSelected(delay);
        }
    }

    private void ConvertMouseEventsToAbsolutePositioning_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveMacro?.ConvertMouseEventsToAbsolutePositioning();
    }

    private async void SaveEvents_Click(object sender, RoutedEventArgs e)
    {
        var macro = ViewModel.ActiveMacro;
        if (macro is null)
        {
            return;
        }

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var newName = await FileOperations.SaveEventsToFileAsync(macro.Events, macro.Name, windowHandle);
        if (newName is not null)
        {
            macro.Name = newName;
            StatusText.Text = $"Saved {newName}";
        }
    }

    private async void LoadEvents_Click(object sender, RoutedEventArgs e)
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var loadedMacro = await FileOperations.LoadEventsFromFileAsync(windowHandle);
        if (loadedMacro is null)
        {
            return;
        }

        var macro = ViewModel.AddNewTab();
        macro.PopulateEventCollectionWithNewEvents(loadedMacro.Events);
        macro.Name = loadedMacro.Name;
        AddTab(macro, select: true);
        StatusText.Text = $"Loaded {loadedMacro.Name}";
    }

    private void AddTab_Click(object sender, RoutedEventArgs e)
    {
        AddNewTab();
    }

    private void MacroTabs_AddTabButtonClick(TabView sender, object args)
    {
        AddNewTab();
    }

    private void AddNewTab()
    {
        var macro = ViewModel.AddNewTab();
        AddTab(macro, select: true);
    }

    private void AddTab(MacroViewModel macro, bool select)
    {
        var tab = new TabViewItem
        {
            Header = macro.Name,
            IconSource = new SymbolIconSource { Symbol = Symbol.Document },
            Tag = macro,
            Content = new MacroTabContent { DataContext = macro }
        };

        macro.PropertyChanged += Macro_PropertyChanged;
        MacroTabs.TabItems.Add(tab);

        if (select)
        {
            MacroTabs.SelectedItem = tab;
        }
    }

    private void MacroTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Tag is not MacroViewModel macro)
        {
            return;
        }

        _synchronizingTabs = true;
        try
        {
            macro.PropertyChanged -= Macro_PropertyChanged;
            ViewModel.CloseTab(macro);
            sender.TabItems.Remove(args.Tab);
            sender.SelectedIndex = ViewModel.SelectedTabIndex;
        }
        finally
        {
            _synchronizingTabs = false;
        }
    }

    private void MacroTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_synchronizingTabs)
        {
            ViewModel.SelectedTabIndex = MacroTabs.SelectedIndex;
        }
    }

    private void MacroTabs_TabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        var orderedMacros = sender.TabItems
            .OfType<TabViewItem>()
            .Select(tab => tab.Tag)
            .OfType<MacroViewModel>()
            .ToList();

        ViewModel.SynchronizeTabOrder(orderedMacros);
        ViewModel.SelectedTabIndex = sender.SelectedIndex;
    }

    private void Macro_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MacroViewModel macro || e.PropertyName != nameof(MacroViewModel.Name))
        {
            return;
        }

        var tab = MacroTabs.TabItems
            .OfType<TabViewItem>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Tag, macro));
        if (tab is not null)
        {
            tab.Header = macro.Name;
        }
    }

    private bool TryGetDelay(out ulong delay)
    {
        if (ulong.TryParse(DelayTextBox.Text, out delay))
        {
            return true;
        }

        StatusText.Text = "Enter a non-negative delay in microseconds.";
        return false;
    }

    private void UnsignedNumberTextBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        args.Cancel = args.NewText.Any(character => !char.IsDigit(character));
    }

    private async void ViewModel_StatusMessageRequested(object? sender, string message)
    {
        StatusText.Text = message;
        var dialog = new ContentDialog
        {
            Title = "Macro Recorder",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
