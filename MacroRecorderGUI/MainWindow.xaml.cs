using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace MacroRecorderGUI;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<MacroViewModel, MacroTabContent> _editors = [];
    private readonly Dictionary<Guid, (DateTimeOffset Updated, IReadOnlyList<PathSample> Samples, string Summary)> _thumbnails = [];
    private readonly DispatcherTimer _runTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly ShellRunLifetime _runLifetime = new();
    private readonly bool _registerGlobalHotkeys;
    private GlobalHotkeys? _globalHotkeys;
    private DesktopThemeMonitor? _themeMonitor;
    private RunController? _controller;
    private RunLease? _recordCountdown;
    private RunLease? _activeRun;
    private MacroViewModel? _runMacro;
    private ShellDialogs? _dialogs;
    private ShellDialogs Dialogs => _dialogs ??= new ShellDialogs(RootGrid);
    private PlaybackOptions _playbackOptions = new();
    private ulong? _overrideRecordingDelay;
    private string _shortcutStatus = "Global shortcuts register when the main window is activated.";
    private readonly ShellFeedback _feedback = new();
    private string? _runError;
    private bool _libraryVisible, _initialized, _initializing, _busy, _stopping, _closing, _allowClose, _constructed, _preparingRun, _isRecordingRun, _savingRun, _mainHiddenForRun;
    private volatile bool _closed;
    private int _libraryRefreshVersion;
    private double _recordCountdownSeconds;
    private MacroTabContent? ActiveEditor => EditorHost.Content as MacroTabContent;
    private bool RunActive => _preparingRun || _recordCountdown is not null || ViewModel.IsRecording || ViewModel.IsFinalizingRecording || ViewModel.PlaybackState.IsActive;
    private string EmergencyShortcut => _globalHotkeys?.EmergencyStop?.DisplayName ?? "Unavailable";

    public MainWindow() : this(new MainWindowViewModel()) { }

    // This path never constructs native engines and never shows or activates a window.
    // The caller supplies fake engines and a temporary library through the view model.
    public MainWindow(MainWindowViewModel viewModel, bool registerGlobalHotkeys = true)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ViewModel.SetEmergencyStopAvailability(false, "The emergency stop shortcut has not registered yet.");
        _registerGlobalHotkeys = registerGlobalHotkeys;
        InitializeComponent();
        _constructed = true;
        RootGrid.DataContext = ViewModel;
        _themeMonitor = new DesktopThemeMonitor(AppWindow.Id, QueueTitleBarUpdate);
        ViewModel.PropertyChanged += ViewModel_Changed;
        ViewModel.StatusMessageRequested += ViewModel_StatusMessageRequested;
        ViewModel.MacroTabs.CollectionChanged += Macros_Changed;
        Library.OpenRequested += Library_OpenRequested;
        Library.RenameRequested += Library_RenameRequested;
        Library.ExportRequested += Library_ExportRequested;
        Activated += MainWindow_Activated;
        AppWindow.Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        _runTimer.Tick += RunTimer_Tick;
        RootGrid.KeyDown += Root_KeyDown;
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new SizeInt32(Math.Min(workArea.Width, (int)(1328 * scale)), Math.Min(workArea.Height, (int)(878 * scale))));
        AttachActiveEditor();
        UpdateTitleBar();
        RefreshShell();
    }

    public MainWindowViewModel ViewModel { get; }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateTitleBar();
        if (_initialized || _initializing) return;
        _initializing = true;
        try
        {
            await ViewModel.InitializeLibraryAsync();
            if (_closed || _closing) return;
            _initialized = true;
            if (ViewModel.Library.Count > 0 || ViewModel.ActiveMacro?.Events.Count == 0) await ShowLibraryAsync();
            if (ViewModel.LibraryWarnings.Count > 0) SetMessage(string.Join(" ", ViewModel.LibraryWarnings));
        }
        catch (Exception error) { if (!_closed && !_closing) SetMessage(error.Message); }
        finally { _initializing = false; }
    }

    private void AttachActiveEditor()
    {
        var macro = ViewModel.ActiveMacro;
        if (ActiveEditor is { } previous && !ReferenceEquals(previous.DataContext, macro)) previous.IsPreviewMode = false;
        if (macro is null) { EditorHost.Content = null; return; }
        if (!_editors.TryGetValue(macro, out var editor))
        {
            editor = new MacroTabContent { DataContext = macro };
            editor.StateChanged += Editor_StateChanged;
            macro.PropertyChanged += Macro_Changed;
            _editors.Add(macro, editor);
        }
        EditorHost.Content = editor;
    }

    private void Macros_Changed(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (var macro in _editors.Keys.Where(m => !ViewModel.MacroTabs.Contains(m)).ToArray())
        {
            macro.PropertyChanged -= Macro_Changed;
            var editor = _editors[macro]; editor.StateChanged -= Editor_StateChanged; editor.Dispose();
            _editors.Remove(macro);
        }
        AttachActiveEditor(); RefreshShell();
    }
    private void ViewModel_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (_closed) return;
        if (e.PropertyName is nameof(MainWindowViewModel.ActiveMacro) or nameof(MainWindowViewModel.SelectedTabIndex)) AttachActiveEditor();
        RefreshShell();
    }
    private void Macro_Changed(object? sender, PropertyChangedEventArgs e) => RefreshShell();
    private void Editor_StateChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, ActiveEditor)) return;
        if (!RunActive) _feedback.ReportEditor(ActiveEditor?.Status);
        RefreshShell();
    }

    private void RefreshShell()
    {
        if (_closed || !_constructed) return;
        var macro = ViewModel.ActiveMacro;
        var preview = !_libraryVisible && ActiveEditor?.IsPreviewMode == true;
        PageTitle.Text = _libraryVisible ? "Recordings" : macro?.Name ?? "Your workspace";
        PageTitle.MaxWidth = Math.Max(150, RootGrid.ActualWidth - 450);
        BreadcrumbLibrary.Content = _libraryVisible ? "Your workspace" : "Recordings";
        BreadcrumbName.Text = macro?.Name ?? "";
        BreadcrumbName.MaxWidth = Math.Max(80, RootGrid.ActualWidth - 560);
        BreadcrumbChevron.Visibility = BreadcrumbName.Visibility = _libraryVisible ? Visibility.Collapsed : Visibility.Visible;
        SaveState.Visibility = _libraryVisible || macro is null ? Visibility.Collapsed : Visibility.Visible;
        SaveState.Text = macro?.SaveState switch
        {
            RecordingSaveState.Saved => "✓  Saved",
            RecordingSaveState.Saving => "Saving…",
            RecordingSaveState.Failed => "Save failed",
            RecordingSaveState.Dirty => "Unsaved changes",
            _ => "Draft"
        };
        SaveState.Foreground = Resource(macro?.SaveState == RecordingSaveState.Failed ? "MacroRedBrush"
            : macro?.IsDirty == true ? "MacroMutedBrush" : "MacroGreenBrush");
        ToolTipService.SetToolTip(SaveState, macro?.SaveError ?? "Saved in your local recordings library");
        Library.Visibility = _libraryVisible ? Visibility.Visible : Visibility.Collapsed;
        EditorHost.Visibility = !_libraryVisible && macro is not null ? Visibility.Visible : Visibility.Collapsed;
        EmptyEditor.Visibility = !_libraryVisible && macro is null ? Visibility.Visible : Visibility.Collapsed;
        UndoButton.Visibility = _libraryVisible ? Visibility.Collapsed : Visibility.Visible;
        UndoButton.IsEnabled = !_busy && !RunActive && ActiveEditor?.CanUndo == true;
        NewRecordingButton.IsEnabled = !_busy && !RunActive && ViewModel.CanRecord;
        DocumentButton.IsEnabled = !_busy && !RunActive;
        EditorHost.IsEnabled = !_busy && !RunActive;
        SpeedButton.Visibility = RepeatButton.Visibility = _libraryVisible ? Visibility.Collapsed : Visibility.Visible;
        SpeedButton.IsEnabled = RepeatButton.IsEnabled = !_busy && !RunActive;
        SpeedLabel.Text = $"{_playbackOptions.Speed:0.##}× {(preview ? "playback" : "speed")}";
        RepeatLabel.Text = _playbackOptions.RepeatUntilStopped ? "Until stopped" : _playbackOptions.RepeatCount == 1 ? "Play once" : $"{_playbackOptions.RepeatCount} repeats";
        BackToEditorButton.Visibility = NextActionButton.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        PreviewButton.Visibility = _libraryVisible || RunActive ? Visibility.Collapsed : Visibility.Visible;
        PreviewButton.IsEnabled = !_busy && ActiveEditor?.CanPreview == true;
        PreviewLabel.Text = preview ? ActiveEditor?.IsPreviewPlaying == true ? "Pause" : "Play preview" : "Preview";
        PreviewIcon.Glyph = ActiveEditor?.IsPreviewPlaying == true ? "\uE769" : "\uE768";
        PlayButton.Visibility = _libraryVisible || preview || RunActive ? Visibility.Collapsed : Visibility.Visible;
        PlayButton.IsEnabled = !_busy && ViewModel.CanPlay && macro?.Events.Count > 0;
        ImportButton.Visibility = _libraryVisible ? Visibility.Visible : Visibility.Collapsed;
        ImportButton.IsEnabled = !_busy && !RunActive;
        StopButton.Visibility = RunActive ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Content = _recordCountdown is not null || ViewModel.PlaybackState.Phase == PlaybackPhase.Countdown ? "Cancel countdown" : "Stop";
        StatusDot.Fill = Resource(preview ? "MacroBlueBrush" : ViewModel.IsRecording ? "MacroRedBrush" : "MacroMutedBrush");
        StatusText.Text = RunActive ? _preparingRun ? (_isRecordingRun ? "Preparing recording…" : "Preparing playback…") : ViewModel.IsRecording ? "Recording" : "Run in progress"
            : macro?.SaveState == RecordingSaveState.Failed ? $"Save failed: {macro.SaveError}"
            : _feedback.Text ?? (preview ? "Preview only" : _libraryVisible ? $"{ViewModel.Library.Count} saved recordings" : "Stopped");
        ToolTipService.SetToolTip(StatusText, StatusText.Text);
        SummaryText.Text = _libraryVisible ? "Local recordings · original .macro format" : ActiveEditor?.Summary ?? "";
        RefreshController();
    }
    private Brush Resource(string key) => DesignResources.Brush(RootGrid, key);
    private void SetMessage(string message) { _feedback.Report(message); RefreshShell(); }
    private void ViewModel_StatusMessageRequested(object? sender, string message)
    {
        if (_closed) return;
        DispatcherQueue.TryEnqueue(() => { if (!_closed) SetMessage(message); });
    }

    private async Task OperationAsync(Func<Task> action)
    {
        if (_busy || _closed || _closing || RunActive) return;
        _busy = true; RefreshShell();
        try { await action(); }
        catch (OperationCanceledException) when (_closed || _closing) { }
        catch (Exception error) { if (!_closed) SetMessage(error.Message); }
        finally { _busy = false; RefreshShell(); }
    }
    private async Task SaveActiveAsync()
    {
        if (ViewModel.ActiveMacro is { IsDirty: true } macro && (macro.ChangeVersion > 0 || macro.SavedAt is not null))
            await ViewModel.SaveRecordingAsync(macro);
    }
    private async Task ShowLibraryAsync()
    {
        if (RunActive) return;
        if (ActiveEditor is { } editor) editor.IsPreviewMode = false;
        await SaveActiveAsync();
        if (_closed || _closing) return;
        _libraryVisible = true; _feedback.Clear(); RefreshShell();
        await RefreshLibraryAsync();
    }
    private async Task RefreshLibraryAsync()
    {
        var version = ++_libraryRefreshVersion;
        var cards = new List<LibraryCard>();
        foreach (var item in ViewModel.Library.ToArray())
        {
            if (_closed || _closing || version != _libraryRefreshVersion) return;
            IReadOnlyList<PathSample> samples;
            string? thumbnailError = null;
            var summary = item.Summary;
            if (_thumbnails.TryGetValue(item.Id, out var cached) && cached.Updated == item.UpdatedAt)
            { samples = cached.Samples; summary = cached.Summary; }
            else
            {
                try
                {
                    var events = await ViewModel.LoadRecordingSnapshotAsync(item.Id);
                    if (_closed || _closing || version != _libraryRefreshVersion) return;
                    var preview = await Task.Run(() =>
                    {
                        var projection = new ActionProjection();
                        foreach (var input in events) projection.Append(input);
                        return (Samples: PathDisplay.Decimate(projection.Samples, 0, projection.Samples.Count, 160),
                            Summary: $"{projection.Actions.Count:N0} actions · {TimeText.Human(projection.TotalTime)}");
                    });
                    if (_closed || _closing || version != _libraryRefreshVersion) return;
                    samples = preview.Samples; summary = preview.Summary;
                    _thumbnails[item.Id] = (item.UpdatedAt, samples, summary);
                }
                catch (Exception error) { samples = []; thumbnailError = "Path unavailable · could not read recording"; SetMessage($"Could not load thumbnail for {item.Name}: {error.Message}"); }
            }
            cards.Add(new LibraryCard(item.Id, item.Name, summary, $"Saved {item.UpdatedAt.ToLocalTime():g}", samples, thumbnailError));
        }
        if (!_closed && version == _libraryRefreshVersion) Library.SetCards(cards);
    }
    private async void Library_Click(object sender, RoutedEventArgs e) => await OperationAsync(ShowLibraryAsync);
    private async void Library_OpenRequested(object? sender, LibraryCard card) => await OperationAsync(async () =>
    {
        await SaveActiveAsync(); await ViewModel.OpenRecordingAsync((Guid)card.Key); ShowEditor();
    });
    private async void Library_RenameRequested(object? sender, LibraryCard card) => await OperationAsync(async () =>
    {
        var name = await Dialogs.RenameAsync(card.Name);
        if (name is null) return;
        await SaveActiveAsync();
        var macro = await ViewModel.OpenRecordingAsync((Guid)card.Key);
        macro.Name = name; await ViewModel.SaveRecordingAsync(macro); await RefreshLibraryAsync();
    });
    private async void Library_ExportRequested(object? sender, LibraryCard card) => await OperationAsync(async () =>
    {
        var path = await FileOperations.PickExportPathAsync(card.Name, AppWindow.Id);
        if (path is null) return;
        await SaveActiveAsync();
        var previous = ViewModel.ActiveMacro;
        var macro = await ViewModel.OpenRecordingAsync((Guid)card.Key);
        await ViewModel.ExportRecordingAsync(macro, path);
        if (previous is not null) ViewModel.SelectedTabIndex = ViewModel.MacroTabs.IndexOf(previous);
        SetMessage($"Exported {macro.Name}");
    });
    private void ShowEditor() { if (_closed || _closing) return; _libraryVisible = false; _feedback.Clear(); AttachActiveEditor(); RefreshShell(); }
    private async void Import_Click(object sender, RoutedEventArgs e) => await OperationAsync(ImportAsync);
    private async Task ImportAsync()
    {
        var path = await FileOperations.PickImportPathAsync(AppWindow.Id);
        if (path is null) return;
        await SaveActiveAsync(); await ViewModel.ImportRecordingAsync(path); ShowEditor();
    }
    private async Task ExportActiveAsync()
    {
        if (ViewModel.ActiveMacro is not { } macro) return;
        var path = await FileOperations.PickExportPathAsync(macro.Name, AppWindow.Id);
        if (path is null) return;
        await ViewModel.ExportRecordingAsync(macro, path);
        SetMessage($"Exported {macro.Name}");
    }
    private void DocumentMenu_Opening(object sender, object e)
    {
        DocumentMenu.Items.Clear();
        void Item(string title, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = title };
            item.Click += async (_, _) => await OperationAsync(action);
            DocumentMenu.Items.Add(item);
        }
        Item("New empty recording", async () => { await SaveActiveAsync(); await ViewModel.CreateDraftAsync("Untitled recording"); ShowEditor(); });
        Item("Import .macro…", ImportAsync);
        if (ViewModel.ActiveMacro is { } active)
        {
            DocumentMenu.Items.Add(new MenuFlyoutSeparator());
            Item("Save recording", async () => { await ViewModel.SaveRecordingAsync(active); SetMessage($"Saved {active.Name}"); });
            Item("Rename…", async () =>
            {
                var name = await Dialogs.RenameAsync(active.Name);
                if (name is not null) { active.Name = name; await ViewModel.SaveRecordingAsync(active); }
            });
            Item("Export .macro…", ExportActiveAsync);
            Item("Record into this recording…", () => PrepareRecordingAsync(intoExisting: true));
            Item("Close recording", async () => { await ViewModel.CloseRecordingAsync(active); ShowEditor(); });
        }
        if (ViewModel.MacroTabs.Count > 1)
        {
            var open = new MenuFlyoutSubItem { Text = "Open recordings" };
            foreach (var macro in ViewModel.MacroTabs)
            {
                var item = new MenuFlyoutItem { Text = macro.Name + (macro.IsDirty ? " · unsaved" : "") };
                item.Click += async (_, _) => await OperationAsync(async () => { await SaveActiveAsync(); ViewModel.SelectedTabIndex = ViewModel.MacroTabs.IndexOf(macro); ShowEditor(); });
                open.Items.Add(item);
            }
            DocumentMenu.Items.Add(open);
        }
    }
    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (RunActive || _busy || ActiveEditor is null) return;
        if (ActiveEditor.Undo()) await OperationAsync(SaveActiveAsync);
    }
    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (RunActive || _busy || ActiveEditor is not { } editor) return;
        _feedback.Clear(); editor.IsPreviewMode = true; editor.TogglePreview(); RefreshShell();
    }
    private void BackToEditor_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveEditor is { } editor) editor.IsPreviewMode = false;
        _feedback.Clear(); RefreshShell();
    }
    private void NextAction_Click(object sender, RoutedEventArgs e) { ActiveEditor?.StepPreview(); RefreshShell(); }
    private async void Timing_Click(object sender, RoutedEventArgs e) => await OperationAsync(async () =>
    {
        var choice = await Dialogs.TimingAsync(_playbackOptions, _playbackOptions.RepeatUntilStopped, false, "", EmergencyShortcut);
        if (choice is null) return;
        _playbackOptions = choice.Options with { RepeatUntilStopped = choice.Loop };
    });

    private async void NewRecording_Click(object sender, RoutedEventArgs e) => await OperationAsync(() => PrepareRecordingAsync(false));
    private async Task PrepareRecordingAsync(bool intoExisting)
    {
        var choice = await Dialogs.RecordAsync(intoExisting ? ViewModel.ActiveMacro?.Name ?? "Untitled recording" : "New recording", intoExisting, _overrideRecordingDelay);
        if (choice is null) return;
        await StartRecordingAsync(choice, intoExisting, fromHotkey: false);
    }
    private async Task StartRecordingAsync(RecordingChoices choice, bool intoExisting, bool fromHotkey)
    {
        if (!ViewModel.CanRecord || RunActive || _globalHotkeys?.EmergencyStop is null)
        { SetMessage("An emergency stop shortcut must be registered before recording."); return; }
        var cancellation = _runLifetime.Begin();
        _isRecordingRun = true;
        _runError = null;
        _activeRun = cancellation;
        _preparingRun = true; RefreshShell();
        try
        {
            await cancellation.PrepareAsync(SaveActiveAsync);
            if (!intoExisting) await cancellation.PrepareAsync(async () => { await ViewModel.CreateDraftAsync(choice.Name); });
            else if (ViewModel.ActiveMacro is { } existing) existing.Name = choice.Name;
            cancellation.ThrowIfCancelled(); ThrowIfClosing();
            ShowEditor(); ActiveEditor!.IsPreviewMode = false;
            _overrideRecordingDelay = choice.OverrideDelay;
            _runMacro = ViewModel.ActiveMacro;
            _recordCountdown = cancellation;
            _recordCountdownSeconds = choice.CountdownSeconds;
            _preparingRun = false;
            ShowController(ViewModel.ActiveMacro!.Name);
            var watch = Stopwatch.StartNew();
            while (_recordCountdownSeconds > 0)
            {
                RefreshController();
                await Task.Delay(50, cancellation.Token);
                _recordCountdownSeconds = Math.Max(0, choice.CountdownSeconds - watch.Elapsed.TotalSeconds);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            _recordCountdown = null;
            if (!ViewModel.StartRecording(fromHotkey, choice.Clear)) FinishController(cancellation);
        }
        catch (OperationCanceledException) { if (!_closed) SetMessage("Recording cancelled"); FinishController(cancellation); }
        catch { FinishController(cancellation); throw; }
        finally { _preparingRun = false; if (ReferenceEquals(_recordCountdown, cancellation)) _recordCountdown = null; RefreshShell(); }
    }
    private async void Play_Click(object sender, RoutedEventArgs e) => await OperationAsync(PreparePlaybackAsync);
    private async Task PreparePlaybackAsync()
    {
        if (ViewModel.ActiveMacro is not { } macro || macro.Events.Count == 0) return;
        if (_globalHotkeys?.EmergencyStop is null) { SetMessage("An emergency stop shortcut must be registered before playback."); return; }
        var choice = await Dialogs.TimingAsync(_playbackOptions, _playbackOptions.RepeatUntilStopped, true, macro.Name, EmergencyShortcut);
        if (choice is null) return;
        _playbackOptions = choice.Options with { RepeatUntilStopped = choice.Loop };
        var run = _runLifetime.Begin();
        _isRecordingRun = false;
        _runError = null;
        _activeRun = run;
        _runMacro = macro;
        _preparingRun = true; RefreshShell();
        try
        {
            await run.PrepareAsync(SaveActiveAsync);
            run.ThrowIfCancelled(); ThrowIfClosing(); ActiveEditor!.IsPreviewMode = false;
            _preparingRun = false;
            ShowController(macro.Name);
            await ViewModel.PlayActiveMacro(_playbackOptions);
        }
        catch (OperationCanceledException) { if (!_closed) SetMessage("Playback cancelled"); }
        finally { _preparingRun = false; if (!RunActive) FinishController(run); RefreshShell(); }
    }
    private void ShowController(string name)
    {
        _controller = new RunController(RootGrid.RequestedTheme, name, EmergencyShortcut);
        _controller.StopRequested += Controller_StopRequested;
        RefreshController();
        _controller.ShowWithoutActivation();
        AppWindow.Hide();
        _mainHiddenForRun = true;
        _runTimer.Start();
    }
    private async void Controller_StopRequested(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _controller)) await StopRunAsync();
    }
    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopRunAsync();
    private async Task StopRunAsync()
    {
        if (_stopping) return;
        var run = _activeRun;
        _stopping = true;
        try
        {
            _runLifetime.Cancel();
            _recordCountdown?.Cancel();
            var recording = ViewModel.RecordingMacro;
            if (ViewModel.IsRecording || ViewModel.IsFinalizingRecording) await ViewModel.StopRecordingAsync(_overrideRecordingDelay);
            if (_closed) return;
            ViewModel.EmergencyStop();
            if (recording is not null) await SaveRunAsync(recording);
            if (!RunActive) FinishController(run);
        }
        catch (Exception error) { _runError = error.Message; if (!_closed) SetMessage(error.Message); if (!RunActive) FinishController(run); }
        finally { _stopping = false; RefreshShell(); }
    }
    private async void RunTimer_Tick(object? sender, object e)
    {
        if (_closed) return;
        ViewModel.RefreshWorkflowSummaries();
        RefreshShell();
        if (RunActive || _stopping || _activeRun is not { } run) return;
        _stopping = true;
        try
        {
            if (_runMacro is { IsDirty: true } macro) await SaveRunAsync(macro);
        }
        catch (Exception error) { if (!_closed) SetMessage(error.Message); }
        finally { FinishController(run); _stopping = false; }
    }
    private void RefreshController()
    {
        if (_controller is null) return;
        if (_recordCountdown is not null)
            _controller.SetState(RunControllerPresentation.RecordingCountdown(_recordCountdownSeconds), EmergencyShortcut);
        else if (_isRecordingRun)
        {
            var state = RunControllerPresentation.ForRecording(ViewModel.IsRecording, ViewModel.IsFinalizingRecording,
                _savingRun, ViewModel.RecordingElapsed, ViewModel.RecordedEventCount);
            _controller.SetState(_runError is null ? state : state with { Note = _runError }, EmergencyShortcut);
        }
        else
            _controller.SetState(RunControllerPresentation.ForPlayback(ViewModel.PlaybackState, _savingRun), EmergencyShortcut);
    }
    private async Task SaveRunAsync(MacroViewModel macro)
    {
        _savingRun = true; RefreshShell();
        try { await ViewModel.SaveRecordingAsync(macro); }
        finally { _savingRun = false; RefreshShell(); }
    }
    private void FinishController(RunLease? expected = null)
    {
        if (expected is not null && !_runLifetime.Owns(expected)) return;
        _runTimer.Stop();
        if (_controller is not null)
        {
            _controller.StopRequested -= Controller_StopRequested;
            _controller.Finish(); _controller = null;
        }
        if (_activeRun is { } run) _runLifetime.Complete(run);
        _activeRun = null; _runMacro = null;
        if (_mainHiddenForRun && !_closed && !_allowClose) AppWindow.Show(false);
        _mainHiddenForRun = false;
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_globalHotkeys is not null || !_registerGlobalHotkeys || _closed) return;
        try
        {
            _globalHotkeys = new GlobalHotkeys(WinRT.Interop.WindowNative.GetWindowHandle(this));
            var recorded = _globalHotkeys.AddHotKey(VirtualKey.Q, HotKeyModifiers.Control,
                async () => await OperationAsync(() => StartRecordingAsync(new("New recording", 3, false, _overrideRecordingDelay), false, true)));
            var stopped = _globalHotkeys.AddHotKey(VirtualKey.W, HotKeyModifiers.Control, async () => await StopRunAsync());
            var played = _globalHotkeys.AddHotKey(VirtualKey.E, HotKeyModifiers.Control, async () => await OperationAsync(async () =>
            {
                Activate();
                await PreparePlaybackAsync();
            }));
            SetEmergencyShortcut(KeyboardShortcuts.EmergencyStopChoices[0]);
            _shortcutStatus = recorded && stopped && played ? "Start, stop, and playback shortcuts are registered."
                : "One or more Ctrl + Q / W / E shortcuts are unavailable. Use the on-screen controls.";
            if (!recorded || !stopped || !played) SetMessage(_shortcutStatus);
        }
        catch (Exception error) { ViewModel.SetEmergencyStopAvailability(false, error.Message); _shortcutStatus = error.Message; SetMessage(error.Message); }
    }
    private void SetEmergencyShortcut(HotkeyGesture gesture)
    {
        if (_globalHotkeys is null) return;
        var changed = _globalHotkeys.TrySetEmergencyStop(gesture, async () => await StopRunAsync(), out var error);
        ViewModel.SetEmergencyStopAvailability(_globalHotkeys.EmergencyStop is not null, error);
        if (!changed) SetMessage(error ?? "The shortcut could not be registered. The previous emergency shortcut remains active.");
        RefreshShell();
    }
    private async void Settings_Click(object sender, RoutedEventArgs e) => await OperationAsync(async () =>
    {
        var actual = _globalHotkeys?.EmergencyStop;
        var selected = KeyboardShortcuts.EmergencyStopChoices.ToList().FindIndex(choice => Equals(choice, actual));
        var choice = await Dialogs.SettingsAsync(Math.Max(0, selected),
            _shortcutStatus + $" Emergency stop: {EmergencyShortcut}.");
        if (choice is { } index) SetEmergencyShortcut(KeyboardShortcuts.EmergencyStopChoices[index]);
    });
    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && ActiveEditor?.IsPreviewMode == true) { BackToEditor_Click(sender, e); e.Handled = true; return; }
        if (e.Key != VirtualKey.Z || IsTextInput(e.OriginalSource as DependencyObject)) return;
        if ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) == 0) return;
        Undo_Click(sender, e); e.Handled = true;
    }
    private static bool IsTextInput(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBox or NumberBox or RichEditBox or PasswordBox) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private async void MainWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            _recordCountdown?.Cancel();
            _runLifetime.Cancel();
            _dialogs?.Dismiss();
            await ViewModel.ShutdownAsync();
            _allowClose = true; FinishController(); Close();
        }
        catch (Exception error)
        {
            SetMessage(error.Message);
            if (await Dialogs.CloseFailureAsync(error.Message))
            { _closing = false; Close(); }
        }
        finally { _closing = false; }
    }
    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _closed = true; ++_libraryRefreshVersion;
        _runLifetime.Dispose();
        _recordCountdown = null; _runTimer.Stop();
        _controller?.Finish(); _controller = null;
        _themeMonitor?.Dispose(); _themeMonitor = null;
        _globalHotkeys?.Dispose(); _globalHotkeys = null;
        ViewModel.PropertyChanged -= ViewModel_Changed;
        ViewModel.StatusMessageRequested -= ViewModel_StatusMessageRequested;
        ViewModel.MacroTabs.CollectionChanged -= Macros_Changed;
        foreach (var (macro, editor) in _editors)
        { macro.PropertyChanged -= Macro_Changed; editor.StateChanged -= Editor_StateChanged; editor.Dispose(); }
        _editors.Clear();
        ViewModel.Dispose();
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Transport is null) return;
        var narrow = e.NewSize.Width < 820;
        RailColumn.Width = new GridLength(74);
        PageHeading.Padding = narrow ? new Thickness(18) : new Thickness(30, 18, 30, 17);
        Transport.Padding = narrow ? new Thickness(18, 12, 18, 12) : new Thickness(24, 12, 24, 12);
        TransportInfo.Spacing = narrow ? 10 : 22;
        Grid.SetRow(TransportControls, narrow ? 1 : 0);
        Grid.SetColumn(TransportControls, narrow ? 0 : 1);
        Grid.SetColumnSpan(TransportControls, narrow ? 2 : 1);
        NewRecordingLabel.Text = e.NewSize.Width < 680 ? "Record" : "New recording";
        PageTitle.FontSize = narrow ? 21 : 25;
        var stackedHeading = e.NewSize.Width < 680;
        Grid.SetRow(HeadingActions, stackedHeading ? 1 : 0);
        Grid.SetColumn(HeadingActions, stackedHeading ? 0 : 1);
        Grid.SetColumnSpan(HeadingActions, stackedHeading ? 2 : 1);
        HeadingActions.HorizontalAlignment = HorizontalAlignment.Right;
        SaveState.Visibility = e.NewSize.Width < 760 || _libraryVisible ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ThrowIfClosing()
    {
        if (_closed || _closing) throw new OperationCanceledException("The window is closing.");
    }
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: string theme }) return;
        RootGrid.RequestedTheme = Enum.Parse<ElementTheme>(theme);
        _controller?.SetTheme(RootGrid.RequestedTheme);
        RefreshShell(); UpdateTitleBar();
    }
    private void Root_ThemeChanged(FrameworkElement sender, object args) { UpdateTitleBar(); RefreshShell(); }
    private void QueueTitleBarUpdate()
    {
        if (_closed) return;
        DispatcherQueue.TryEnqueue(() => { if (!_closed) UpdateTitleBar(); });
    }
    private void UpdateTitleBar()
    {
        if (_closed || !_constructed || _themeMonitor is null) return;
        foreach (var editor in _editors.Values) editor.RefreshTheme();
        Library.RefreshTheme();
        RefreshShell();
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var colors = TitleBarPalette.ForTheme(RootGrid.ActualTheme == ElementTheme.Dark,
            _themeMonitor.HighContrast, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000));
        var bar = AppWindow.TitleBar;
        bar.BackgroundColor = bar.InactiveBackgroundColor = bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = colors.Background;
        bar.ForegroundColor = bar.ButtonForegroundColor = colors.Foreground;
        bar.InactiveForegroundColor = bar.ButtonInactiveForegroundColor = colors.InactiveForeground;
        bar.ButtonHoverBackgroundColor = colors.HoverBackground; bar.ButtonHoverForegroundColor = colors.Foreground;
        bar.ButtonPressedBackgroundColor = colors.PressedBackground; bar.ButtonPressedForegroundColor = colors.Foreground;
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}
