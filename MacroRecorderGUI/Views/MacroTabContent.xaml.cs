using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;

namespace MacroRecorderGUI.Views;

public sealed partial class MacroTabContent : UserControl, IDisposable
{
    private MacroViewModel? _macro;
    private ActionEditor? _editor;
    private EditorPresentation? _presentation;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _previewWatch = new();
    private bool _loaded, _sync, _disposed, _dragging;
    private readonly PreviewPosition _previewPosition = new();
    private BigInteger _previewStart;
    private InputEvent? _rawEvent;
    private readonly RawEventRows _rawRows = [];
    private readonly InspectorDrafts<Control> _rawDrafts = new();
    private ActionFieldEdits? _actionEdits;
    private bool _populatingActionFields, _committingFields;
    private InputEvent[] _inspectorSelection = [];
    private RecordedAction? Selected => ActionsList.SelectedItem as RecordedAction;
    private IReadOnlyList<PathSample> _display = [];
    private IReadOnlyList<PathSample> _selectedDisplay = [];
    private CoordinateSpace _space;
    private PathViewport _viewport;
    private Polygon? _cursor;
    private Ellipse? _handle;
    private VisualPreview? _preview;
    private PreviewFrame? _previewFrame;
    private RecordedAction? _pathAction;
    private bool _isPreviewMode;
    private string _status = "";
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibility = new();

    public event EventHandler? StateChanged;
    public bool CanUndo => _editor?.CanUndo == true;
    public bool CanPreview => _editor?.Projection.Actions.Count > 0;
    public bool IsPreviewPlaying => _previewTimer.IsEnabled;
    public string Summary => SummaryText?.Text ?? "";
    public string Status
    {
        get => _status;
        private set { _status = value; StateChanged?.Invoke(this, EventArgs.Empty); }
    }
    public bool IsPreviewMode
    {
        get => _isPreviewMode;
        set
        {
            if (_isPreviewMode == value) return;
            if (value && !TryCommitPendingEdits()) return;
            StopPreview(); CancelDrag(); _isPreviewMode = value;
            if (value)
            {
                EditorPathHost.Content = null; PreviewPathHost.Content = PathPanel;
                _previewPosition.SeekTime(0, _editor?.Projection.TotalTime ?? 0);
                _preview?.ResetCheckpoints();
            }
            else { PreviewPathHost.Content = null; EditorPathHost.Content = PathPanel; }
            EditorWorkspace.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            PreviewPage.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value) BuildTimeline();
            UpdatePreviewFrame(); DrawPath();
            Status = value ? "Preview only · no input sent" : "";
        }
    }

    public MacroTabContent()
    {
        InitializeComponent();
        IsTabStop = false;
        ClickAwayFocus.Attach(this, this);
        DataContextChanged += Context_Changed;
        _refreshTimer.Tick += Refresh_Tick;
        _previewTimer.Tick += Preview_Tick;
        foreach (var field in new[] { WaitInput, DurationInput, DestinationX, DestinationY, RawDelay, RawFixedDuration, RawX, RawY, RawFlags, RawData, RawKey })
            field.TextChanging += Draft_Changing;
        foreach (var field in new[] { RawRelative, RawDesktop, RawKeyUp }) { field.Checked += CheckDraft_Changed; field.Unchecked += CheckDraft_Changed; }
        RawEachDelay.TextChanging += (_, _) => _rawEachEdited = true;
    }

    private void Editor_Loaded(object sender, RoutedEventArgs e) { _loaded = true; Attach(); }
    private void Editor_Unloaded(object sender, RoutedEventArgs e) { _loaded = false; Detach(); }
    private void Context_Changed(FrameworkElement sender, DataContextChangedEventArgs args) { if (_loaded) Attach(); }
    private void Attach()
    {
        Detach();
        if (_disposed || DataContext is not MacroViewModel macro) return;
        _macro = macro; _editor = macro.Editor;
        _actionEdits = new ActionFieldEdits(macro);
        _presentation = new EditorPresentation(_editor);
        _editor.Invalidated += Editor_Invalidated;
        ActionsList.ItemsSource = _editor.Projection.Actions;
        RawList.ItemsSource = _rawRows;
        RefreshEditor();
    }
    private void Detach()
    {
        _modalConditionEditor?.Dispose(); _modalConditionEditor = null;
        _conditionEditor?.Dispose(); _conditionEditor = null; _conditionOwner = null; WaitConditionHost.Content = null;
        StopPreview(); _refreshTimer.Stop(); CancelDrag();
        if (_editor is not null) _editor.Invalidated -= Editor_Invalidated;
        _sync = true;
        ActionsList.ItemsSource = null; RawList.ItemsSource = null;
        _rawRows.Close(); _rawDrafts.Clear();
        _rawEachEdited = false;
        _sync = false;
        _editor = null; _presentation = null; _macro = null; _rawEvent = null;
        _actionEdits = null; _inspectorSelection = []; _leadingDelayAnchor = null;
        _preview = null; _previewFrame = null; _pathAction = null;
        _timelineSegments = []; _heldLabels = []; _previewInitialSpace = CoordinateSpace.Unknown;
        Timeline.Children.Clear(); Timeline.ColumnDefinitions.Clear(); HeldInputs.ItemsSource = null;
        _previewPosition.SeekTime(0, 0);
        PreviewStep.Text = "NO ACTIONS"; PreviewTitle.Text = "Empty recording";
        PreviewDescription.Text = "Record or add input to preview a sequence.";
        PreviewNext.Text = "End of sequence"; PreviewClock.Text = "0ms / 0ms"; PreviewTiming.Text = "Recorded timing · 0 actions";
        HeldNote.Text = "No recorded keys or buttons held";
        _rawOpen = false; RawContent.Visibility = Visibility.Collapsed; InspectorScroller.Visibility = Visibility.Visible;
        _rawSelection = []; _detailsPane = false; _actionFocus = _rawFocus = _sequenceFocus = null;
        _display = []; _selectedDisplay = []; PathCanvas.Children.Clear();
        SummaryText.Text = "0 steps · 0ms";
        EmptySequence.Visibility = Visibility.Visible;
        UpdateInspector();
        Status = "";
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Detach();
        DataContextChanged -= Context_Changed;
        _refreshTimer.Tick -= Refresh_Tick; _previewTimer.Tick -= Preview_Tick;
    }
    private void Editor_Invalidated(object? sender, EventArgs e)
    {
        StopPreview(); CancelDrag();
        if (_loaded) _refreshTimer.Start();
    }
    private void Refresh_Tick(object? sender, object e) { _refreshTimer.Stop(); RefreshEditor(); }
    private void RefreshEditor(bool resetDrafts = false)
    {
        if (_editor is null || _macro is null || _presentation is null) return;
        var primary = Selected;
        _sync = true;
        try
        {
            var previous = ActionsList.SelectedItems.OfType<RecordedAction>().ToArray();
            var refresh = _presentation.Refresh(primary, previous);
            if (!previous.SequenceEqual(refresh.Selection))
            {
                ActionsList.SelectedItems.Clear();
                foreach (var a in refresh.Selection) ActionsList.SelectedItems.Add(a);
            }
        }
        finally { _sync = false; }
        SummaryText.Text = $"{EditorText.Count(_editor.Projection.StepCount, "step")} · {TimeText.Human(_editor.Projection.TotalTime)}";
        if (_macro.Events.OfType<WaitConditionEvent>().Count() is var waitCount && waitCount > 0)
            SummaryText.Text += $" recorded timing + {EditorText.Count(waitCount, "conditional wait")}";
        EmptySequence.Visibility = _macro.Events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _previewPosition.RefreshDuration(_editor.Projection.TotalTime);
        _preview = new VisualPreview(_macro.Events, _editor.Projection);
        if (IsPreviewMode) { BuildTimeline(); UpdatePreviewFrame(); }
        UpdateInspector(resetDrafts); UpdateSelectionScope(); RefreshRowColors(); DrawPath();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Actions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sync || _committingFields || _editor is null) return;
        var anchors = ActionsList.SelectedItems.OfType<RecordedAction>().Select(a => a.First).ToArray();
        var committed = CanLeaveRawDraft() && CommitCondition() && CommitActionFields(refresh: false);
        if (!committed) anchors = _inspectorSelection;
        // Commit against the old inspector identity before adopting the requested selection.
        if (_macro is not null && (_editor.IsDirty || !committed
            || !anchors.SequenceEqual(ActionsList.SelectedItems.OfType<RecordedAction>().Select(a => a.First))))
        {
            _sync = true;
            try
            {
                _editor.Refresh(); ActionsList.SelectedItems.Clear();
                foreach (var anchor in anchors)
                    if (_editor.Projection.ActionAt(_macro.Events.IndexOf(anchor)) is { } current && !ActionsList.SelectedItems.Contains(current)) ActionsList.SelectedItems.Add(current);
            }
            finally { _sync = false; }
        }
        StopPreview(); CancelDrag();
        if (committed)
        {
            _leadingDelayAnchor = null;
            _editor.SelectActions(ActionsList.SelectedItems.OfType<RecordedAction>());
            _rawSelection = []; _rawEvent = null; _rawDrafts.Clear();
        }
        if (Selected is { } a) _previewPosition.SeekTime(a.StartTime + a.Wait, _editor.Projection.TotalTime);
        RefreshEditor();
    }
    private void UpdateSelectionScope()
    {
        if (_editor is not null && _macro is not null)
        {
            SelectionScope.Text = _leadingDelayAnchor is not null ? "Delay selected" : ActionsList.SelectedItems.Count > 0 ? $"{EditorText.Count(ActionsList.SelectedItems.Count, "action")} selected" : "Select an action";
            DeleteButton.IsEnabled = _rawOpen ? RawList.SelectedItems.Count > 0 : ActionsList.SelectedItems.Count > 0;
            var deleteScope = _rawOpen ? "Delete selected raw events" : _leadingDelayAnchor is not null ? "Delete this delay" : "Delete selected actions";
            ToolTipService.SetToolTip(DeleteButton, deleteScope + " (Delete)");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DeleteButton, deleteScope);
        }
    }
    // WinUI TextChanging is synchronous; TextChanged is asynchronous and cannot use population suppression.
    private void Draft_Changing(TextBox sender, TextBoxTextChangingEventArgs e)
    {
        if (ActionFieldFor(sender) is { } field)
        {
            if (!_populatingActionFields) _actionEdits?.Change(field, sender.Text);
        }
        else _rawDrafts.Changing(sender);
    }
    private void CheckDraft_Changed(object sender, RoutedEventArgs e) => _rawDrafts.Changing((CheckBox)sender);
    private void Field(CheckBox field, bool value) => _rawDrafts.Populate(field, () => field.IsChecked = value);
    private void Field(TextBox field, string text)
    {
        if (ActionFieldFor(field) is { } actionField)
        {
            _populatingActionFields = true;
            try
            {
                var value = _actionEdits?.Text(actionField, text) ?? text;
                if (field.Text != value) field.Text = value;
            }
            finally { _populatingActionFields = false; }
        }
        else _rawDrafts.Populate(field, () => field.Text = text);
    }
    private void UpdateInspector(bool resetDrafts = false)
    {
        var a = Selected;
        if (_leadingDelayAnchor is not null)
        {
            var index = _macro?.Events.IndexOf(_leadingDelayAnchor) ?? -1;
            if (a is null || index < a.Start || index >= a.End) _leadingDelayAnchor = null;
        }
        _actionEdits?.Select(a, resetDrafts);
        _inspectorSelection = ActionsList.SelectedItems.OfType<RecordedAction>().Select(action => action.First).ToArray();
        if (resetDrafts) _rawDrafts.Clear();
        ActionFields.IsEnabled = a is not null;
        ActionFields.Visibility = a is null ? Visibility.Collapsed : Visibility.Visible;
        SelectedTitle.Text = a?.Name ?? "Select an action";
        var rawCount = ActionsList.SelectedItems.OfType<RecordedAction>().Sum(action => action.Count);
        RawHeader.Text = a is null ? "Exact input" : $"Exact input · {EditorText.Count(rawCount, "event")}";
        RawToggle.IsEnabled = a is not null;
        InspectorScope.Text = a is null ? "" : $"Editing step {a.StepNumber} only";
        InspectorScope.Visibility = ActionsList.SelectedItems.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        TechnicalDetail.Text = a is null ? "" : a.TechnicalSummary + $"\nPause before: {TimeText.Seconds(a.Wait)}s · execution time: {TimeText.Seconds(a.Duration)}s";
        ActionWarning.Text = _presentation?.InspectorWarning(a) ?? "";
        ActionWarning.Visibility = ActionWarning.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(ActionWarning, a is { Complete: false } ? a.WarningExplanation : ActionWarning.Text);
        var path = a is not null && a.Kind is not (ActionKind.Keys or ActionKind.Scroll);
        EditorPathHost.Visibility = path || a is null ? Visibility.Visible : Visibility.Collapsed;
        InputVisual.Visibility = a is not null && !path ? Visibility.Visible : Visibility.Collapsed;
        CapturedKeys.ItemsSource = a?.KeyLabels ?? Array.Empty<string>();
        InputGlyph.Glyph = a?.Glyph ?? "\uE8A5";
        InputDetail.Text = a?.Description ?? "";
        if (a is null || _editor is null) { UpdateConditionInspector(null, resetDrafts); _sync = true; _rawRows.Close(); _sync = false; LoadRaw(); return; }
        Field(WaitInput, TimeText.Seconds(_leadingDelayAnchor?.TimeSinceLastEvent ?? (a.First is DelayEvent delay ? delay.DurationMicroseconds : a.Wait)));
        Field(DurationInput, TimeText.Seconds(a.Duration));
        DurationFields.Visibility = a.CanEditDuration ? Visibility.Visible : Visibility.Collapsed;
        TimingSummary.Text = a.Summary;
        var reason = _editor.GeometryBlockReason(a);
        GeometryNote.Text = reason is null ? "Adjusts the end of this movement."
            : a.Kind is ActionKind.Keys ? "Original key order is preserved."
            : a.Kind is ActionKind.Scroll ? "Original wheel units and timing are preserved." : reason;
        ToolTipService.SetToolTip(GeometryNote, reason ?? "Drag the outlined endpoint or apply coordinates. The following path remains connected.");
        DestinationFields.Visibility = reason is null ? Visibility.Visible : Visibility.Collapsed;
        var end = _editor.Projection.Samples[a.End - 1].Position;
        Field(DestinationX, end?.X.ToString("0", CultureInfo.InvariantCulture) ?? "");
        Field(DestinationY, end?.Y.ToString("0", CultureInfo.InvariantCulture) ?? "");
        ConversionPanel.Visibility = _editor.Projection.HasRelativeMovement ? Visibility.Visible : Visibility.Collapsed;
        if (_rawOpen) PopulateRaw();
        UpdateConditionInspector(a, resetDrafts);
        UpdateDelayInspector(a);
        ResizeWorkspace();
    }

    private void RunEdit(Action operation, string message, bool resetDrafts = true)
    {
        if (!TryCommitPendingEdits()) return;
        try { StopPreview(); operation(); RefreshEditor(resetDrafts); Status = message; }
        catch (Exception error) when (error is ArgumentException or OverflowException or FormatException)
        { Status = error.Message; RefreshEditor(); }
    }
    private void Destination_Click(object sender, RoutedEventArgs e)
    {
        CommitActionFields(ActionField.DestinationX);
    }
    private void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (AcceptEstimate.IsChecked != true) { Status = "Read the conversion assumptions and select ‘Use this explicit estimate’ first."; return; }
        if (_editor is not null) RunEdit(_editor.ConvertAnchoredEstimate, "Anchored moves converted using 1 count = 1 pixel. These are estimates, not reconstructed cursor positions. Undo is available.");
        AcceptEstimate.IsChecked = false;
    }
    public bool Undo()
    {
        if (_editor is null || !TryCommitPendingEdits()) return false;
        StopPreview(); var undone = _editor.Undo(); RefreshEditor(resetDrafts: true);
        Status = undone ? "Edit undone; later captured events retained." : "Nothing safe to undo.";
        return undone;
    }
    private void AddMouse_Click(object sender, RoutedEventArgs e)
    {
        if (_macro is not null) AddRawEvent(_macro.CreateMouseEventManually, "Mouse event added. Edit it in Exact input.");
    }
    private void AddKeyboard_Click(object sender, RoutedEventArgs e)
    {
        if (_macro is not null) AddRawEvent(_macro.CreateKeyboardEventManually, "Keyboard event added. Edit it in Exact input.");
    }
    private void AddRawEvent(Action create, string message)
    {
        if (_macro is null || _editor is null) return;
        RunEdit(() =>
        {
            var before = _macro.Events.ToHashSet();
            create();
            var inserted = _macro.Events.Single(input => !before.Contains(input));
            _rawSelection = [inserted]; _rawEvent = inserted;
            _editor.SelectRawEvents(_rawSelection);
            RefreshEditor();
            SetRawOpen(true); ShowPane(true);
        }, message, resetDrafts: false);
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelection(_rawOpen && InspectorPanel.Visibility == Visibility.Visible
            && (ReferenceEquals(sender, DeleteButton) || _detailsPane));
    }
    private void DeleteActions_Click(object sender, RoutedEventArgs e) => DeleteSelection(raw: false);
    private void DeleteSelection(bool raw)
    {
        if (_macro is null || _editor is null) return;
        if (!raw && _leadingDelayAnchor is { } delay)
        {
            RunEdit(() => { _editor.SetLeadingDelay(delay, 0); _leadingDelayAnchor = null; }, "Delay deleted. Undo is available.");
            return;
        }
        var anchors = ActionsList.SelectedItems.OfType<RecordedAction>().Select(action => action.First).ToArray();
        var inputs = RawList.SelectedItems.OfType<RawEventRow>().Select(row => row.Input).ToArray();
        RunEdit(() =>
        {
            // View navigation retains both selections. Resolve only this command's
            // scope, after drafts commit and against the current action projection.
            RefreshEditor();
            if (raw) _editor.SelectRawEvents(inputs);
            else _editor.SelectActions(anchors.Select(input => _editor.Projection.ActionAt(_macro.Events.IndexOf(input)))
                .OfType<RecordedAction>().Distinct());
            _macro.RemoveSelectedEvents();
        }, raw ? "Selected raw input removed. Undo is available." : "Selected actions deleted. Undo is available.");
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_macro is not null) RunEdit(() => _editor!.Execute("Clear all", _macro.Clear), "All actions cleared. Undo is available.");
    }
    private void Actions_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (HandleDeleteKey(sender, e.Key, e.OriginalSource as DependencyObject)) e.Handled = true;
    }
    private bool HandleDeleteKey(object sender, VirtualKey key, DependencyObject? source)
    {
        if (key != VirtualKey.Delete || IsTextInput(source)) return false;
        DeleteSelection(ReferenceEquals(sender, RawList));
        return true;
    }
    private static bool IsTextInput(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBox or RichEditBox or PasswordBox or NumberBox) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private bool _rawOpen;
    private void RawToggle_Click(object sender, RoutedEventArgs e) { SetRawOpen(true); FocusDetail(); }
    private void BackToAction_Click(object sender, RoutedEventArgs e) { SetRawOpen(false); FocusDetail(); }
    private void SetRawOpen(bool open)
    {
        if (_rawOpen == open || _modalConditionEditor is not null || _authoringDialogOpen) return;
        _navigatingView = true;
        try
        {
            RememberPaneFocus();
            _conditionEditor?.CancelTest();
            _rawOpen = open;
            if (open) _detailsPane = true;
            RawContent.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            InspectorScroller.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            if (open) PopulateRaw(enterRaw: true);
            else _editor?.SelectActions(ActionsList.SelectedItems.OfType<RecordedAction>());
            UpdateSelectionScope(); ResizeWorkspace();
        }
        finally { _navigatingView = false; }
    }
    private void PopulateRaw(bool enterRaw = false)
    {
        if (!_rawOpen || _editor is null || _macro is null) return;
        _sync = true;
        try
        {
            var previous = _rawEvent;
            _rawRows.Refresh(_macro.Events, ActionsList.SelectedItems.OfType<RecordedAction>().OrderBy(a => a.Start).ToArray());
            var selection = enterRaw ? _rawSelection : _editor.RawSelection ? _macro.SelectedEvents.ToArray() : [];
            var available = _rawRows.Select(row => row.Input).ToHashSet();
            selection = selection.Where(available.Contains).ToArray();
            if (selection.Length == 0 && (enterRaw || !_editor.RawSelection))
                selection = _rawRows.FirstOrDefault(row => ReferenceEquals(row.Input, previous)) is { } retained
                    ? [retained.Input] : _rawRows.FirstOrDefault() is { } first ? [first.Input] : [];
            RestoreRawSelection(selection, previous);
            _rawSelection = RawList.SelectedItems.OfType<RawEventRow>().Select(row => row.Input).ToArray();
            _editor.SelectRawEvents(_rawSelection);
        }
        finally { _sync = false; }
        LoadRaw(); UpdateSelectionScope();
    }
    private void RestoreRawSelection(IEnumerable<InputEvent> selection, InputEvent? primary)
    {
        var selected = selection.ToHashSet();
        var rows = _rawRows.Where(row => selected.Contains(row.Input)).ToArray();
        var owner = rows.FirstOrDefault(row => ReferenceEquals(row.Input, primary)) ?? rows.FirstOrDefault();
        // SelectedItem owns the fields and can differ from the earliest selected
        // row. Restore it first so LoadRaw cannot discard another input's draft.
        if (!ReferenceEquals(RawList.SelectedItem, owner)) RawList.SelectedItem = owner;
        foreach (var row in RawList.SelectedItems.OfType<RawEventRow>().Where(row => !rows.Contains(row)).ToArray()) RawList.SelectedItems.Remove(row);
        foreach (var row in rows)
            if (!RawList.SelectedItems.Contains(row)) RawList.SelectedItems.Add(row);
    }
    private void Raw_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sync) return;
        if (!CanLeaveRawDraft())
        {
            _sync = true;
            try
            {
                RestoreRawSelection(_rawSelection, _rawEvent);
            }
            finally { _sync = false; }
            return;
        }
        _rawSelection = RawList.SelectedItems.OfType<RawEventRow>().Select(row => row.Input).ToArray();
        LoadRaw();
        _editor?.SelectRawEvents(RawList.SelectedItems.OfType<RawEventRow>().Select(row => row.Input));
        UpdateSelectionScope();
    }
    private void LoadRaw()
    {
        _rawEvent = (RawList.SelectedItem as RawEventRow)?.Input; RawApply.IsEnabled = _rawEvent is not null;
        RawDelay.IsEnabled = _rawEvent is not null;
        RawMouseFields.Visibility = _rawEvent is MouseEvent ? Visibility.Visible : Visibility.Collapsed;
        RawKeyFields.Visibility = _rawEvent is KeyboardEvent ? Visibility.Visible : Visibility.Collapsed;
        RawFixedDuration.Visibility = _rawEvent is DelayEvent ? Visibility.Visible : Visibility.Collapsed;
        // Projection regrouping can change an action's first input without changing
        // the exact raw event being edited. Only that event owns these drafts.
        _rawDrafts.Select(_rawEvent);
        if (_rawEvent is null) return;
        Field(RawDelay, _rawEvent.TimeSinceLastEvent.ToString(CultureInfo.InvariantCulture));
        if (_rawEvent is DelayEvent delay) Field(RawFixedDuration, delay.DurationMicroseconds.ToString(CultureInfo.InvariantCulture));
        RawMouseFields.Visibility = _rawEvent is MouseEvent ? Visibility.Visible : Visibility.Collapsed;
        RawKeyFields.Visibility = _rawEvent is KeyboardEvent ? Visibility.Visible : Visibility.Collapsed;
        if (_rawEvent is MouseEvent m)
        {
            Field(RawX, m.X.ToString(CultureInfo.InvariantCulture)); Field(RawY, m.Y.ToString(CultureInfo.InvariantCulture));
            Field(RawFlags, ((uint)m.ActionType).ToString(CultureInfo.InvariantCulture)); Field(RawData, m.MouseData.ToString(CultureInfo.InvariantCulture));
            Field(RawRelative, m.RelativePosition); Field(RawDesktop, m.MappedToVirtualDesktop);
        }
        else if (_rawEvent is KeyboardEvent k) { Field(RawKey, k.VirtualKeyCode.ToString(CultureInfo.InvariantCulture)); Field(RawKeyUp, k.KeyUp); }
    }
    private void RawApply_Click(object sender, RoutedEventArgs e)
    {
        if (_rawEvent is not { } input || _editor is null || _modalConditionEditor is not null || _authoringDialogOpen) return;
        try
        {
            // Validate all explicit raw fields before any other draft can change the recording.
            _ = ReadRawDraft();
            if (!CommitCondition() || !CommitActionFields()) return;
            StopPreview(); _editor.EditRaw(input, ReadRawDraft());
            _rawDrafts.Clear(); RefreshEditor();
            Status = "Exact input applied. Undo is available.";
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or FormatException)
        { Status = error.Message; }
    }
    private void RawEach_Click(object sender, RoutedEventArgs e)
    {
        if (_macro is null || _editor is null || _modalConditionEditor is not null || _authoringDialogOpen) return;
        if (HasRawDraft) { CanLeaveRawDraft(); return; }
        try
        {
            var delay = ReadRawTime(RawEachDelay.Text);
            if (!CommitCondition() || !CommitActionFields()) return;
            _editor.SelectRawEvents(RawList.SelectedItems.OfType<RawEventRow>().Select(row => row.Input));
            _macro.ChangeDelaysOnSelected(delay); _rawEachEdited = false;
            RefreshEditor(); Status = "Each selected raw delay changed, including execution time. Undo is available.";
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or FormatException) { Status = error.Message; }
    }

    private Brush BrushResource(string name)
    {
        var dictionary = Resources.MergedDictionaries[0];
        var theme = _accessibility.HighContrast ? "HighContrast" : ActualTheme == ElementTheme.Dark ? "Default" : "Light";
        return (Brush)((ResourceDictionary)dictionary.ThemeDictionaries[theme])["DesignedEditor" + name];
    }
    private Point Map(PathPosition p) { var point = _viewport.Map(p); return new(point.X, point.Y); }
    private void DrawPath()
    {
        if (PathCanvas is null || _editor is null || _presentation is null) return;
        PathCanvas.Children.Clear(); _cursor = null; _handle = null;
        PathCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, PathCanvas.ActualWidth, PathCanvas.ActualHeight) };
        var action = IsPreviewMode ? PreviewPathAction() : Selected;
        _pathAction = action;
        if (!_presentation.TryGetFrame(action, out var frame, IsPreviewMode ? PreviewCoordinateSpace : null))
        {
            if (_loaded) _refreshTimer.Start();
            return;
        }
        _display = frame.Overview; _selectedDisplay = frame.SelectedSamples; _space = frame.Space;
        PathSelectionNote.Text = action is null ? "Select an action to see its movement"
            : frame.HasSelectedPosition ? $"Action {action.Number:D2} · {frame.SelectionLabel}" : frame.SelectionLabel;
        SelectedPathLegend.Visibility = frame.HasSelectedPosition ? Visibility.Visible : Visibility.Collapsed;
        PathNote.Text = _space switch
        {
            CoordinateSpace.RelativeCounts => "Device counts · not screen pixels",
            CoordinateSpace.AbsoluteDesktop => "Screen pixels · virtual desktop",
            CoordinateSpace.AbsolutePrimary => "Screen pixels · primary screen",
            _ => "No recorded position for this action"
        };
        ToolTipService.SetToolTip(PathNote, _space == CoordinateSpace.RelativeCounts
            ? "Device-count trace only, not a cursor-position estimate. Separate count segments start at zero."
            : "Only this coordinate frame is shown. Button-only input cannot supply a screen location.");
        PathEmpty.Visibility = frame.Bounds is null ? Visibility.Visible : Visibility.Collapsed;
        AddGridDots();
        if (frame.Bounds is not { } bounds) { UpdateCursor(); return; }
        _viewport = PathViewport.Fit(bounds, PathCanvas.ActualWidth, PathCanvas.ActualHeight);
        AddPath(_display, BrushResource("Muted"), 1.5, dashed: true);
        AddPath(_selectedDisplay, BrushResource("Path"), 3);
        AddDirectionCues();
        if (ShowSamples.IsChecked == true)
            foreach (var s in _selectedDisplay.Where(s => s.Position?.Space == _space)) AddDot(s.Position!.Value, 4, "Path");
        foreach (var landmark in frame.Landmarks) AddLandmark(landmark);
        if (frame.HasSelectedPath) AddEndpoints();
        else if (frame.HasSelectedPosition)
        {
            var position = _selectedDisplay.Last(s => s.Position?.Space == _space).Position!.Value;
            var marker = AddDot(position, 14, "Paper");
            marker.Stroke = BrushResource("Path"); marker.StrokeThickness = 3;
            AddPathLabel(position, "Recorded position", below: false);
        }
        if (!IsPreviewMode && frame.GeometryBlockReason is null && frame.Destination is { } end)
        {
            var halo = AddDot(end, 42, "Blue"); halo.Opacity = .09;
            _handle = AddDot(end, 18, "Paper");
            _handle.Stroke = BrushResource("Path"); _handle.StrokeThickness = 3;
        }
        _cursor = new Polygon
        {
            Points = new PointCollection { new(0, 0), new(5, 22), new(10, 15), new(18, 14) },
            Fill = BrushResource("Ink"), Stroke = BrushResource("Paper"), StrokeThickness = 1.5,
            IsHitTestVisible = false
        };
        PathCanvas.Children.Add(_cursor);
        UpdateCursor();
    }
    private void AddLandmark(RecordedAction action)
    {
        var sample = _editor!.Projection.Samples[action.Start];
        if (sample.Position is not { } p || p.Space != _space) return;
        var marker = new Rectangle();
        marker.Width = marker.Height = 12; marker.StrokeThickness = 2;
        marker.Stroke = BrushResource(ReferenceEquals(action, _pathAction) ? "Path" : "Muted");
        marker.Fill = BrushResource("Paper");
        Place(marker, Map(p)); ToolTipService.SetToolTip(marker, $"Action {action.Number:D2} · {action.Detail} · last recorded position before drag");
        marker.Tapped += (_, args) => { if (IsPreviewMode) SeekPreview(action.StartTime); else ActionsList.SelectedItem = action; args.Handled = true; };
        PathCanvas.Children.Add(marker);
    }
    private void AddPath(IReadOnlyList<PathSample> samples, Brush brush, double thickness, bool dashed = false)
    {
        var geometry = new PathGeometry(); PathFigure? figure = null; PolyLineSegment? segment = null;
        foreach (var sample in samples)
        {
            if (sample.Position is not { } p || p.Space != _space) { figure = null; continue; }
            var point = Map(p);
            if (figure is null || sample.StartsSegment)
            {
                figure = new PathFigure { StartPoint = point, IsClosed = false, IsFilled = false };
                segment = new PolyLineSegment(); figure.Segments.Add(segment); geometry.Figures.Add(figure);
            }
            else segment!.Points.Add(point);
        }
        var path = new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false };
        if (dashed) path.StrokeDashArray = new DoubleCollection { 4, 3 };
        PathCanvas.Children.Add(path);
    }
    private void AddDirectionCues()
    {
        foreach (var (from, to) in PathDisplay.Directions(_selectedDisplay, _space, _viewport.Scale))
        {
            var a = Map(from); var b = Map(to);
            var dx = b.X - a.X; var dy = b.Y - a.Y; var length = Math.Sqrt(dx * dx + dy * dy);
            var ux = dx / length; var uy = dy / length;
            var x = (a.X + b.X) / 2; var y = (a.Y + b.Y) / 2;
            var arrow = new Polyline { Stroke = BrushResource("Path"), StrokeThickness = 2,
                IsHitTestVisible = false, Points = new PointCollection
                {
                    new(x - ux * 6 - uy * 4, y - uy * 6 + ux * 4), new(x, y),
                    new(x - ux * 6 + uy * 4, y - uy * 6 - ux * 4)
                } };
            PathCanvas.Children.Add(arrow);
        }
    }
    private void AddEndpoints()
    {
        // Mark each displayed segment independently; never invent an origin or connect separate frames.
        var samples = _selectedDisplay;
        for (var i = 0; i < samples.Count; i++)
        {
            if (samples[i].Position is not { } p || p.Space != _space) continue;
            var starts = i == 0 || samples[i].StartsSegment || samples[i - 1].Position?.Space != _space;
            var ends = i == samples.Count - 1 || samples[i + 1].StartsSegment || samples[i + 1].Position?.Space != _space;
            if (starts)
            {
                var start = AddDot(p, 12, "Paper");
                start.Stroke = BrushResource("Path"); start.StrokeThickness = 2;
                if (i == 0) AddPathLabel(p, "Start", below: true);
            }
            if (ends)
            {
                var end = AddDot(p, 14, "Paper");
                end.Stroke = BrushResource("Path"); end.StrokeThickness = 2.5;
                if (!IsPreviewMode && i == samples.Count - 1 && _pathAction is { } action) AddPathLabel(p, $"Action {action.Number:D2} end", below: false);
            }
        }
    }
    private Ellipse AddDot(PathPosition position, double size, string brush)
    {
        var dot = new Ellipse { Width = size, Height = size, Fill = BrushResource(brush), IsHitTestVisible = false };
        Place(dot, Map(position)); PathCanvas.Children.Add(dot); return dot;
    }
    private static void Place(FrameworkElement element, Point p) { Canvas.SetLeft(element, p.X - element.Width / 2); Canvas.SetTop(element, p.Y - element.Height / 2); }
    private void UpdateCursor()
    {
        if (_editor is null) return;
        var p = _previewFrame?.Pointer?.Position;
        if (_cursor is not null)
        {
            _cursor.Visibility = IsPreviewMode && p?.Space == _space ? Visibility.Visible : Visibility.Collapsed;
            if (p?.Space == _space)
            {
                var point = Map(p.Value);
                Canvas.SetLeft(_cursor, point.X); Canvas.SetTop(_cursor, point.Y);
            }
        }
        PreviewClock.Text = $"{TimeText.Human(_previewPosition.Time)} / {TimeText.Human(_editor.Projection.TotalTime)}";
        ToolTipService.SetToolTip(PreviewClock, $"{TimeText.Seconds(_previewPosition.Time)}s / {TimeText.Seconds(_editor.Projection.TotalTime)}s");
        _sync = true;
        Scrubber.Value = _previewPosition.Value;
        _sync = false;
    }
    public void TogglePreview()
    {
        if (!TryCommitPendingEdits()) return;
        if (!IsPreviewMode) IsPreviewMode = true;
        if (_previewTimer.IsEnabled) { StopPreview(); return; }
        if (_editor is null) return;
        if (_editor.Projection.TotalTime == 0)
        {
            if (_preview?.Checkpoint(_previewPosition.Time) is null) _preview?.ResetCheckpoints();
            UpdatePreviewFrame(); DrawPath();
            return;
        }
        if (_previewPosition.Time >= _editor.Projection.TotalTime) { _previewPosition.SeekTime(0, _editor.Projection.TotalTime); _preview?.ResetCheckpoints(); }
        _previewStart = _previewPosition.Time; _previewWatch.Restart(); _previewTimer.Start();
        UpdatePreviewFrame(); DrawPath();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Preview_Tick(object? sender, object e)
    {
        if (_editor is null) { StopPreview(); return; }
        _previewPosition.SeekTime(_previewStart + (BigInteger)_previewWatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency, _editor.Projection.TotalTime);
        UpdatePreviewFrame();
        if (_preview?.Checkpoint(_previewPosition.Time) is { } checkpoint)
        {
            _previewPosition.SeekTime(checkpoint.StartTime + checkpoint.Wait, _editor.Projection.TotalTime);
            StopPreview(); UpdatePreviewFrame();
        }
        if (!ReferenceEquals(_pathAction, PreviewPathAction()) || _space != PreviewCoordinateSpace) DrawPath(); else UpdateCursor();
        if (_previewPosition.Time >= _editor.Projection.TotalTime) StopPreview();
    }
    private void StopPreview()
    {
        var wasPlaying = _previewTimer.IsEnabled;
        _previewTimer.Stop(); _previewWatch.Stop();
        if (wasPlaying) StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Scrub_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_sync || _editor is null) return;
        StopPreview(); _previewPosition.Scrub(e.NewValue, _editor.Projection.TotalTime);
        _preview?.ResetCheckpoints();
        UpdatePreviewFrame(); DrawPath();
    }
    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsPreviewMode || _editor is null || !e.GetCurrentPoint(PathCanvas).Properties.IsLeftButtonPressed) return;
        // This handler runs before the parent background handler and before pointer capture.
        if (!TryCommitPendingEdits()) { ClickAwayFocus.LeaveTextInput(this, this); e.Handled = true; return; }
        ClickAwayFocus.LeaveTextInput(this, this);
        if (_editor.IsDirty) return;
        var point = e.GetCurrentPoint(PathCanvas).Position;
        if (_handle is not null && Math.Abs(point.X - Canvas.GetLeft(_handle) - 9) < 14 && Math.Abs(point.Y - Canvas.GetTop(_handle) - 9) < 14)
        { StopPreview(); _dragging = PathCanvas.CapturePointer(e.Pointer); e.Handled = true; return; }
        SelectPathAction(point);
    }
    private void SelectPathAction(Point point)
    {
        var nearest = _display.Concat(_selectedDisplay).Where(s => s.Position?.Space == _space)
            .Select(s => (Sample: s, P: Map(s.Position!.Value))).OrderBy(s => Math.Pow(s.P.X - point.X, 2) + Math.Pow(s.P.Y - point.Y, 2)).FirstOrDefault();
        if (nearest.Sample.Position is not null && _editor?.Projection.ActionForSample(nearest.Sample) is { } action)
            ActionsList.SelectedItem = action;
    }
    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var point = e.GetCurrentPoint(PathCanvas).Position;
        var position = _viewport.Unmap(point.X, point.Y);
        DestinationX.Text = Math.Clamp(Math.Round(position.X), int.MinValue, int.MaxValue).ToString("0", CultureInfo.InvariantCulture);
        DestinationY.Text = Math.Clamp(Math.Round(position.Y), int.MinValue, int.MaxValue).ToString("0", CultureInfo.InvariantCulture);
        if (_handle is not null) Place(_handle, point);
    }
    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false; PathCanvas.ReleasePointerCaptures(); Destination_Click(sender, e);
    }
    private void Canvas_PointerCanceled(object sender, PointerRoutedEventArgs e) { if (_dragging) { CancelDrag(); UpdateInspector(); DrawPath(); } }
    private void CancelDrag()
    {
        if (_dragging) _actionEdits?.CancelDestination();
        _dragging = false; PathCanvas?.ReleasePointerCaptures();
    }
    private void Samples_Changed(object sender, RoutedEventArgs e) => DrawPath();
    private void Editor_ThemeChanged(FrameworkElement sender, object args) => RefreshTheme();
    /// <summary>Call on the UI thread from the host's desktop-safe theme monitor.</summary>
    public void RefreshTheme()
    {
        if (_disposed) return;
        RefreshRowColors(); ResizeWorkspace(); if (IsPreviewMode) BuildTimeline(); DrawPath();
    }
    private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawPath();
    private void Workspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResizeWorkspace();
    }
}
