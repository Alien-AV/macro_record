using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation.Collections;
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
    private bool _fileOperationInProgress;

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
                _globalHotkeys.AddHotKey(VirtualKey.Q, HotKeyModifiers.Control, () => StartRecording(fromHotkey: true))
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
        foreach (var tab in MacroTabs.TabItems.OfType<TabViewItem>())
            (tab.Content as MacroTabContent)?.Dispose();
        ViewModel.Dispose();
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

    private void StartRecording(bool fromHotkey = false)
    {
        ViewModel.StartRecording(fromHotkey, ClearListOnStartRecord.IsChecked == true);
    }

    private void StopRecording()
    {
        ulong? autoDelay = AutoChangeDelay.IsChecked == true && TryGetDelay(out var delay) ? delay : null;
        ViewModel.StopRecording(autoDelay);
    }

    private async void PlayEvents()
    {
        await ViewModel.PlayActiveMacro();
    }

    private void AbortPlayback()
    {
        ViewModel.AbortPlayback();
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
        if (ViewModel.ActiveMacro is { } macro) macro.Editor.Execute("Clear all", macro.Clear);
    }

    private async void SaveEvents_Click(object sender, RoutedEventArgs e)
    {
        var macro = ViewModel.ActiveMacro;
        if (macro is null || _fileOperationInProgress)
        {
            return;
        }

        _fileOperationInProgress = true;
        try
        {
            var newName = await FileOperations.SaveEventsToFileAsync(macro.Events, macro.Name, AppWindow.Id);
            if (newName is not null)
            {
                macro.Name = newName;
                StatusText.Text = $"Saved {newName}";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not save macro: {exception.Message}";
        }
        finally
        {
            _fileOperationInProgress = false;
        }
    }

    private async void LoadEvents_Click(object sender, RoutedEventArgs e)
    {
        if (_fileOperationInProgress)
        {
            return;
        }

        _fileOperationInProgress = true;
        try
        {
            var loadedMacro = await FileOperations.LoadEventsFromFileAsync(AppWindow.Id);
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
        catch (Exception exception)
        {
            StatusText.Text = $"Could not load macro: {exception.Message}";
        }
        finally
        {
            _fileOperationInProgress = false;
        }
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
        _synchronizingTabs = true;
        try
        {
            MacroTabs.TabItems.Add(tab);
            if (select)
            {
                MacroTabs.SelectedItem = tab;
            }
        }
        finally
        {
            _synchronizingTabs = false;
        }

        SynchronizeTabsFromView();
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
            (args.Tab.Content as MacroTabContent)?.Dispose();
            ViewModel.CloseTab(macro);
            sender.TabItems.Remove(args.Tab);
            sender.SelectedIndex = ViewModel.SelectedTabIndex;
        }
        finally
        {
            _synchronizingTabs = false;
        }

        SynchronizeTabsFromView();
    }

    private void MacroTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SynchronizeTabsFromView();
    }

    private void MacroTabs_TabItemsChanged(TabView sender, IVectorChangedEventArgs args)
    {
        SynchronizeTabsFromView();
    }

    private void SynchronizeTabsFromView()
    {
        if (_synchronizingTabs)
        {
            return;
        }

        var orderedMacros = MacroTabs.TabItems
            .OfType<TabViewItem>()
            .Select(tab => tab.Tag)
            .OfType<MacroViewModel>()
            .ToList();

        var selectedMacro = (MacroTabs.SelectedItem as TabViewItem)?.Tag as MacroViewModel;
        ViewModel.SynchronizeTabs(orderedMacros, selectedMacro);
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

    private void ViewModel_StatusMessageRequested(object? sender, string message)
    {
        StatusText.Text = message;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
