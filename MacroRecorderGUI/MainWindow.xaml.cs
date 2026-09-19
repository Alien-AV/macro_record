using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;
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
    private bool _closed;
    private readonly AccessibilitySettings _accessibility = new();
    private readonly UISettings _uiSettings = new();
    // Construct these eagerly: recording shortcuts work before the flyout is first opened.
    private readonly CheckBox _clearBeforeRecording = new() { Content = "Clear macro before recording", IsChecked = true };
    private readonly CheckBox _overrideDelay = new() { Content = "Override each raw event delay", IsChecked = false };
    private readonly TextBox _recordingDelay = new() { Header = "Delay per raw event (µs)", Text = "5000" };

    public MainWindow()
    {
        ViewModel = new MainWindowViewModel();
        InitializeComponent();
        RootGrid.DataContext = ViewModel;
        CreateOptionsFlyout();
        _accessibility.HighContrastChanged += Accessibility_Changed;
        _uiSettings.ColorValuesChanged += SystemColors_Changed;
        UpdateTitleBar();

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

    private void CreateOptionsFlyout()
    {
        var loop = new CheckBox { Content = "Loop playback", IsChecked = ViewModel.LoopPlayback };
        loop.Checked += (_, _) => ViewModel.LoopPlayback = true;
        loop.Unchecked += (_, _) => ViewModel.LoopPlayback = false;
        _recordingDelay.BeforeTextChanging += UnsignedNumberTextBox_BeforeTextChanging;
        var content = new StackPanel { Spacing = 8, Width = 280 };
        content.Children.Add(new TextBlock { Text = "Recording and playback", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(_clearBeforeRecording);
        content.Children.Add(loop);
        content.Children.Add(new TextBlock { Text = "After recording", Margin = new Thickness(0, 12, 0, 0) });
        content.Children.Add(_overrideDelay);
        content.Children.Add(_recordingDelay);
        content.Children.Add(new TextBlock { Text = "Applies to every raw event after capture, including delays within an action.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        OptionsButton.Flyout = new Flyout
        {
            Content = new ScrollViewer { Content = content, MaxHeight = 420,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ShortcutHint is null) return;
        var compact = e.NewSize.Width < 640;
        // Text and the real-input cue remain visible at narrow widths.
        foreach (var icon in new[] { RecordIcon, StopIcon, PlayIcon, AbortIcon })
            icon.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        PlayLabels.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
        PlayLabels.Spacing = compact ? 0 : 6;
        ShortcutHint.Visibility = e.NewSize.Width < 920 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Root_Loaded(object sender, RoutedEventArgs e) => UpdateTitleBar();
    private void Root_ThemeChanged(FrameworkElement sender, object args) => UpdateTitleBar();
    private void Accessibility_Changed(AccessibilitySettings sender, object args) => QueueTitleBarUpdate();
    private void SystemColors_Changed(UISettings sender, object args) => QueueTitleBarUpdate();
    private void QueueTitleBarUpdate() => DispatcherQueue.TryEnqueue(() => { if (!_closed) UpdateTitleBar(); });

    private void UpdateTitleBar()
    {
        if (_closed || RootGrid is null || !AppWindowTitleBar.IsCustomizationSupported()) return;
        // AppWindow color properties are ignored on Windows 10, even when customization is supported.
        var colors = TitleBarPalette.ForTheme(RootGrid.ActualTheme == ElementTheme.Dark,
            _accessibility.HighContrast, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000));
        var bar = AppWindow.TitleBar;
        bar.BackgroundColor = colors.Background;
        bar.ForegroundColor = colors.Foreground;
        bar.InactiveBackgroundColor = colors.Background;
        bar.InactiveForegroundColor = colors.InactiveForeground;
        bar.ButtonBackgroundColor = colors.Background;
        bar.ButtonForegroundColor = colors.Foreground;
        bar.ButtonInactiveBackgroundColor = colors.Background;
        bar.ButtonInactiveForegroundColor = colors.InactiveForeground;
        bar.ButtonHoverBackgroundColor = colors.HoverBackground;
        bar.ButtonHoverForegroundColor = colors.Foreground;
        bar.ButtonPressedBackgroundColor = colors.PressedBackground;
        bar.ButtonPressedForegroundColor = colors.Foreground;
    }

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
        _closed = true;
        _accessibility.HighContrastChanged -= Accessibility_Changed;
        _uiSettings.ColorValuesChanged -= SystemColors_Changed;
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
        ViewModel.StartRecording(fromHotkey, _clearBeforeRecording.IsChecked == true);
    }

    private void StopRecording()
    {
        ulong? autoDelay = _overrideDelay.IsChecked == true && TryGetDelay(out var delay) ? delay : null;
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
        if (ulong.TryParse(_recordingDelay.Text, out delay))
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
