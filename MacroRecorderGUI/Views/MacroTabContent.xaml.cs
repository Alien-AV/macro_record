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
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _previewWatch = new();
    private bool _loaded, _sync, _disposed, _dragging;
    private readonly PreviewPosition _previewPosition = new();
    private BigInteger _previewStart;
    private InputEvent? _rawEvent;
    private readonly RawEventRows _rawRows = [];
    private readonly HashSet<TextBox> _drafts = [];
    private bool _settingFields;
    private readonly HashSet<CheckBox> _checkDrafts = [];
    private InputEvent? _inspectorEvent;
    private RecordedAction? Selected => ActionsList.SelectedItem as RecordedAction;
    private IReadOnlyList<PathSample> _display = [];
    private IReadOnlyList<PathSample> _selectedDisplay = [];
    private CoordinateSpace _space;
    private double _minX, _minY, _scale = 1;
    private Ellipse? _cursor, _handle;

    public MacroTabContent()
    {
        InitializeComponent();
        DataContextChanged += Context_Changed;
        _refreshTimer.Tick += Refresh_Tick;
        _previewTimer.Tick += Preview_Tick;
        foreach (var field in new[] { WaitInput, DurationInput, DestinationX, DestinationY, RawDelay, RawX, RawY, RawFlags, RawData, RawKey })
            field.TextChanged += Draft_Changed;
        foreach (var field in new[] { RawRelative, RawDesktop, RawKeyUp }) { field.Checked += CheckDraft_Changed; field.Unchecked += CheckDraft_Changed; }
    }

    private void Editor_Loaded(object sender, RoutedEventArgs e) { _loaded = true; Attach(); }
    private void Editor_Unloaded(object sender, RoutedEventArgs e) { _loaded = false; Detach(); }
    private void Context_Changed(FrameworkElement sender, DataContextChangedEventArgs args) { if (_loaded) Attach(); }
    private void Attach()
    {
        Detach();
        if (_disposed || DataContext is not MacroViewModel macro) return;
        _macro = macro; _editor = macro.Editor;
        _editor.Invalidated += Editor_Invalidated;
        ActionsList.ItemsSource = _editor.Projection.Actions;
        RawList.ItemsSource = _rawRows;
        RefreshEditor();
    }
    private void Detach()
    {
        StopPreview(); _refreshTimer.Stop(); CancelDrag();
        if (_editor is not null) _editor.Invalidated -= Editor_Invalidated;
        _sync = true;
        ActionsList.ItemsSource = null; RawList.ItemsSource = null;
        _rawRows.Close(); _inspectorEvent = null; _drafts.Clear(); _checkDrafts.Clear();
        _sync = false;
        _editor = null; _macro = null; _rawEvent = null;
        _display = []; _selectedDisplay = []; PathCanvas.Children.Clear();
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
        if (_editor is null || _macro is null) return;
        var primary = Selected;
        _sync = true;
        try
        {
            _editor.Refresh();
            if (primary is null || !_editor.Projection.Actions.Contains(primary))
            {
                ActionsList.SelectedItems.Clear();
                foreach (var a in _editor.SelectedActions()) ActionsList.SelectedItems.Add(a);
                if (ActionsList.SelectedItems.Count == 0 && _editor.Projection.Actions.Count > 0) ActionsList.SelectedIndex = 0;
            }
        }
        finally { _sync = false; }
        _editor.RefreshSelection(ActionsList.SelectedItems.OfType<RecordedAction>());
        if (_editor.RawSelection && !_rawOpen) RawExpander.IsExpanded = true;
        SummaryText.Text = _macro.Events.Count == 0 ? "Record a macro or add an event to get started."
            : $"{_editor.Projection.Actions.Count:N0} actions · {_macro.Events.Count:N0} original events · {TimeText.Human(_editor.Projection.TotalTime)}";
        UndoButton.IsEnabled = _editor.CanUndo;
        _previewPosition.RefreshDuration(_editor.Projection.TotalTime);
        UpdateInspector(resetDrafts); UpdateSelectionText(); PreparePath();
    }
    private void Actions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sync || _editor is null) return;
        // Preserve an explicit user's selection even if capture invalidated the old projection.
        if (_editor.IsDirty && _macro is not null)
        {
            var anchors = ActionsList.SelectedItems.OfType<RecordedAction>().Select(a => a.First).ToArray();
            _sync = true;
            _editor.Refresh(); ActionsList.SelectedItems.Clear();
            foreach (var anchor in anchors)
                if (_editor.Projection.ActionAt(_macro.Events.IndexOf(anchor)) is { } current && !ActionsList.SelectedItems.Contains(current)) ActionsList.SelectedItems.Add(current);
            _sync = false;
        }
        StopPreview(); CancelDrag();
        _editor.SelectActions(ActionsList.SelectedItems.OfType<RecordedAction>());
        if (Selected is { } a) _previewPosition.SeekTime(a.StartTime + a.Wait, _editor.Projection.TotalTime);
        UpdateInspector(resetDrafts: true); UpdateSelectionText(); PreparePath();
    }
    private void UpdateSelectionText()
    {
        if (_editor is not null && _macro is not null)
            SelectionText.Text = _editor.RawSelection ? $"Selection: {_macro.SelectedEvents.Count:N0} raw events · Remove selected uses this exact subset"
                : $"Selection: {ActionsList.SelectedItems.Count:N0} actions / {_macro.SelectedEvents.Count:N0} underlying events";
    }
    private void Draft_Changed(object sender, TextChangedEventArgs e) { if (!_settingFields) _drafts.Add((TextBox)sender); }
    private void CheckDraft_Changed(object sender, RoutedEventArgs e) { if (!_settingFields) _checkDrafts.Add((CheckBox)sender); }
    private void Field(CheckBox field, bool value)
    {
        if (_checkDrafts.Contains(field)) return;
        _settingFields = true; field.IsChecked = value; _settingFields = false;
    }
    private void Field(TextBox field, string text)
    {
        if (_drafts.Contains(field)) return;
        _settingFields = true; field.Text = text; _settingFields = false;
    }
    private void UpdateInspector(bool resetDrafts = false)
    {
        var a = Selected;
        if (resetDrafts || !ReferenceEquals(_inspectorEvent, a?.First)) { _drafts.Clear(); _checkDrafts.Clear(); }
        _inspectorEvent = a?.First;
        ActionFields.IsEnabled = a is not null;
        SelectedTitle.Text = a is null ? "Select an action to inspect" : a.Detail + (ActionsList.SelectedItems.Count > 1 ? " · inspector edits this action only" : "");
        if (a is null || _editor is null) { _sync = true; _rawRows.Close(); _sync = false; LoadRaw(); return; }
        Field(WaitInput, TimeText.Seconds(a.Wait)); Field(DurationInput, TimeText.Seconds(a.Duration));
        DurationButton.IsEnabled = a.Count > 1;
        var reason = _editor.GeometryBlockReason(a);
        GeometryNote.Text = reason ?? "Absolute pixels · drag the outlined endpoint or enter coordinates. The following path remains connected.";
        DestinationFields.IsEnabled = reason is null;
        var end = _editor.Projection.Samples[a.End - 1].Position;
        Field(DestinationX, end?.X.ToString("0", CultureInfo.InvariantCulture) ?? "");
        Field(DestinationY, end?.Y.ToString("0", CultureInfo.InvariantCulture) ?? "");
        if (_rawOpen) PopulateRaw();
    }

    private void RunEdit(Action operation, string message)
    {
        try { StopPreview(); operation(); RefreshEditor(resetDrafts: true); EditorStatus.Text = message; }
        catch (Exception error) when (error is ArgumentException or OverflowException or FormatException)
        { EditorStatus.Text = error.Message; RefreshEditor(); }
    }
    private void Wait_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } a) RunEdit(() => _editor!.SetWait(a, checked((ulong)TimeText.ParseSeconds(WaitInput.Text))), "Wait changed; internal duration is unchanged.");
    }
    private void Duration_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } a) RunEdit(() => _editor!.SetDuration(a, TimeText.ParseSeconds(DurationInput.Text)), "Duration changed; event count and order are preserved.");
    }
    private void Destination_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } a) RunEdit(() => _editor!.SetDestination(a, int.Parse(DestinationX.Text, CultureInfo.InvariantCulture), int.Parse(DestinationY.Text, CultureInfo.InvariantCulture)), "Destination changed; following movement remains connected.");
    }
    private void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (AcceptEstimate.IsChecked != true) { EditorStatus.Text = "Read the conversion assumptions and select ‘Use this explicit estimate’ first."; return; }
        if (_editor is not null) RunEdit(_editor.ConvertAnchoredEstimate, "Anchored moves converted using 1 count = 1 pixel. These are estimates, not reconstructed cursor positions. Undo is available.");
        AcceptEstimate.IsChecked = false;
    }
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        StopPreview(); var undone = _editor.Undo(); RefreshEditor(resetDrafts: true);
        EditorStatus.Text = undone ? "Edit undone; later captured events retained." : "Nothing safe to undo.";
    }
    private void Actions_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Delete || IsTextInput(e.OriginalSource as DependencyObject)) return;
        if (_macro is not null) RunEdit(_macro.RemoveSelectedEvents, "Selected raw input removed. Undo is available.");
        e.Handled = true;
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
    private void Raw_Expanding(Expander sender, ExpanderExpandingEventArgs args) { _rawOpen = true; PopulateRaw(enterRaw: _editor?.RawSelection != true); }
    private void Raw_Collapsed(Expander sender, ExpanderCollapsedEventArgs args)
    {
        _rawOpen = false; _sync = true; _rawRows.Close(); _sync = false; _rawEvent = null;
        _editor?.SelectActions(ActionsList.SelectedItems.OfType<RecordedAction>());
        UpdateSelectionText();
    }
    private void PopulateRaw(bool enterRaw = false)
    {
        if (!_rawOpen || _editor is null || _macro is null) return;
        _sync = true;
        var previous = _rawEvent;
        _rawRows.Refresh(_macro.Events, ActionsList.SelectedItems.OfType<RecordedAction>().OrderBy(a => a.Start).ToArray());
        if (enterRaw)
        {
            RawList.SelectedItems.Clear();
            RawList.SelectedItem = _rawRows.FirstOrDefault(row => ReferenceEquals(row.Input, previous)) ?? _rawRows.FirstOrDefault();
            _editor.SelectRawEvents(RawList.SelectedItems.OfType<RawEventRow>().Select(row => row.Input));
        }
        else if (_editor.RawSelection)
        {
            var selected = _macro.SelectedEvents.ToHashSet();
            foreach (var row in RawList.SelectedItems.OfType<RawEventRow>().Where(row => !selected.Contains(row.Input)).ToArray()) RawList.SelectedItems.Remove(row);
            foreach (var row in _rawRows.Where(row => selected.Contains(row.Input)))
                if (!RawList.SelectedItems.Contains(row)) RawList.SelectedItems.Add(row);
        }
        else RawList.SelectedItems.Clear();
        _sync = false; LoadRaw(); UpdateSelectionText();
    }
    private void Raw_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sync) return;
        LoadRaw();
        _editor?.SelectRawEvents(RawList.SelectedItems.OfType<RawEventRow>().Select(row => row.Input));
        UpdateSelectionText();
    }
    private void LoadRaw()
    {
        var previous = _rawEvent;
        _rawEvent = (RawList.SelectedItem as RawEventRow)?.Input; RawApply.IsEnabled = _rawEvent is not null;
        if (!ReferenceEquals(previous, _rawEvent))
        {
            _checkDrafts.Clear();
            foreach (var field in new[] { RawDelay, RawX, RawY, RawFlags, RawData, RawKey }) _drafts.Remove(field);
        }
        if (_rawEvent is null) return;
        Field(RawDelay, _rawEvent.TimeSinceLastEvent.ToString(CultureInfo.InvariantCulture));
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
        if (_rawEvent is not { } input) return;
        RunEdit(() =>
        {
            var value = input.OriginalProtobufInputEvent.Clone();
            value.TimeSinceLastEvent = ulong.Parse(RawDelay.Text, CultureInfo.InvariantCulture);
            if (value.MouseEvent is { } m)
            {
                m.X = int.Parse(RawX.Text, CultureInfo.InvariantCulture); m.Y = int.Parse(RawY.Text, CultureInfo.InvariantCulture);
                m.ActionType = uint.Parse(RawFlags.Text, CultureInfo.InvariantCulture); m.WheelRotation = uint.Parse(RawData.Text, CultureInfo.InvariantCulture);
                m.RelativePosition = RawRelative.IsChecked == true; m.MappedToVirtualDesktop = RawDesktop.IsChecked == true;
            }
            else { value.KeyboardEvent.VirtualKeyCode = uint.Parse(RawKey.Text, CultureInfo.InvariantCulture); value.KeyboardEvent.KeyUp = RawKeyUp.IsChecked == true; }
            _editor!.EditRaw(input, value);
        }, "Raw event updated. Grouping may change; undo is available.");
    }
    private void RawEach_Click(object sender, RoutedEventArgs e)
    {
        if (_macro is null) return;
        RunEdit(() =>
        {
            _editor!.SelectRawEvents(RawList.SelectedItems.OfType<RawEventRow>().Select(row => row.Input));
            _macro.ChangeDelaysOnSelected(ulong.Parse(RawEachDelay.Text, CultureInfo.InvariantCulture));
        }, "Each selected raw delay changed, including internal action timing.");
    }

    private void PreparePath()
    {
        if (_editor is null) return;
        var projection = _editor.Projection;
        _display = PathDisplay.Decimate(projection.Samples, 0, projection.Samples.Count);
        _selectedDisplay = Selected is { } a ? PathDisplay.Decimate(projection.Samples, Math.Max(0, a.Start - 1), a.Count + (a.Start > 0 ? 1 : 0), 512) : [];
        _space = Selected is { } selected ? projection.Samples[selected.End - 1].Position?.Space ?? CoordinateSpace.Unknown : CoordinateSpace.Unknown;
        PathNote.Text = _space switch
        {
            CoordinateSpace.RelativeCounts => "Device-count trace only · not a pixel path or cursor-position estimate. Separate count segments start at zero.",
            CoordinateSpace.AbsoluteDesktop => "Absolute pixels · virtual desktop. Only this coordinate frame is shown.",
            CoordinateSpace.AbsolutePrimary => "Absolute pixels · primary screen. Only this coordinate frame is shown.",
            _ => "No known position here. Button-only input cannot supply a screen location."
        };
        DrawPath();
    }
    private Brush BrushResource(string name) => (Brush)Application.Current.Resources[name];
    private Point Map(PathPosition p) => new(18 + (p.X - _minX) * _scale, 18 + (p.Y - _minY) * _scale);
    private void DrawPath()
    {
        if (PathCanvas is null || _editor is null) return;
        PathCanvas.Children.Clear(); _cursor = null; _handle = null;
        var points = _display.Concat(_selectedDisplay).Where(s => s.Position?.Space == _space).Select(s => s.Position!.Value).ToArray();
        if (points.Length == 0) { UpdateCursor(); return; }
        _minX = points.Min(p => p.X); _minY = points.Min(p => p.Y);
        _scale = Math.Max(.000000001, Math.Min(Math.Max(1, PathCanvas.ActualWidth - 36) / Math.Max(1, points.Max(p => p.X) - _minX), Math.Max(1, PathCanvas.ActualHeight - 36) / Math.Max(1, points.Max(p => p.Y) - _minY)));
        AddPath(_display, BrushResource("TextFillColorSecondaryBrush"), 1.5);
        AddPath(_display, BrushResource("TextFillColorPrimaryBrush"), 2, dashed: true, only: ActionKind.Drag);
        AddPath(_selectedDisplay, BrushResource("AccentFillColorDefaultBrush"), 3, dashed: Selected?.Kind == ActionKind.Drag);
        if (ShowSamples.IsChecked == true)
            foreach (var s in _selectedDisplay.Where(s => s.Position?.Space == _space)) AddDot(s.Position!.Value, 4, "AccentFillColorDefaultBrush");
        var landmarks = _editor.Projection.MouseLandmarks;
        var visibleLandmarks = new HashSet<RecordedAction>();
        var landmarkCount = Math.Min(256, landmarks.Count);
        for (var i = 0; i < landmarkCount; i++) visibleLandmarks.Add(landmarks[landmarkCount == 1 ? 0 : (int)((long)i * (landmarks.Count - 1) / (landmarkCount - 1))]);
        if (Selected is { Kind: ActionKind.Click or ActionKind.Drag } landmark) visibleLandmarks.Add(landmark);
        foreach (var action in visibleLandmarks) AddLandmark(action);
        if (Selected is { } a && _editor.GeometryBlockReason(a) is null && _editor.Projection.Samples[a.End - 1].Position is { } end)
        {
            _handle = AddDot(end, 18, "CardBackgroundFillColorDefaultBrush");
            _handle.Stroke = BrushResource("AccentFillColorDefaultBrush"); _handle.StrokeThickness = 3;
        }
        _cursor = AddDot(points[0], 10, "TextFillColorPrimaryBrush");
        UpdateCursor();
    }
    private void AddLandmark(RecordedAction action)
    {
        var sample = _editor!.Projection.Samples[action.Kind == ActionKind.Drag ? action.Start : action.End - 1];
        if (sample.Position is not { } p || p.Space != _space) return;
        Shape marker = action.Kind == ActionKind.Drag ? new Rectangle() : new Ellipse();
        marker.Width = marker.Height = 12; marker.StrokeThickness = 2;
        marker.Stroke = BrushResource("TextFillColorPrimaryBrush"); marker.Fill = BrushResource("CardBackgroundFillColorDefaultBrush");
        Place(marker, Map(p)); ToolTipService.SetToolTip(marker, action.Detail);
        marker.Tapped += (_, args) => { ActionsList.SelectedItem = action; args.Handled = true; };
        PathCanvas.Children.Add(marker);
    }
    private void AddPath(IReadOnlyList<PathSample> samples, Brush brush, double thickness, bool dashed = false, ActionKind? only = null)
    {
        var geometry = new PathGeometry(); PathFigure? figure = null; PolyLineSegment? segment = null;
        foreach (var sample in samples)
        {
            if (sample.Position is not { } p || p.Space != _space || only is { } kind && _editor!.Projection.ActionAt(sample.Index)?.Kind != kind) { figure = null; continue; }
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
    private Ellipse AddDot(PathPosition position, double size, string brush)
    {
        var dot = new Ellipse { Width = size, Height = size, Fill = BrushResource(brush), IsHitTestVisible = false };
        Place(dot, Map(position)); PathCanvas.Children.Add(dot); return dot;
    }
    private static void Place(FrameworkElement element, Point p) { Canvas.SetLeft(element, p.X - element.Width / 2); Canvas.SetTop(element, p.Y - element.Height / 2); }
    private void UpdateCursor()
    {
        if (_editor is null) return;
        var p = _editor.Projection.SampleAt(_previewPosition.Time)?.Position;
        if (_cursor is not null)
        {
            _cursor.Visibility = p?.Space == _space ? Visibility.Visible : Visibility.Collapsed;
            if (p?.Space == _space) Place(_cursor, Map(p.Value));
        }
        PreviewClock.Text = $"{TimeText.Human(_previewPosition.Time)} / {TimeText.Human(_editor.Projection.TotalTime)}";
        _sync = true;
        Scrubber.Value = _previewPosition.Value;
        _sync = false;
    }
    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewTimer.IsEnabled) { StopPreview(); return; }
        if (_editor is null || _editor.Projection.TotalTime == 0) return;
        if (_previewPosition.Time >= _editor.Projection.TotalTime) _previewPosition.SeekTime(0, _editor.Projection.TotalTime);
        _previewStart = _previewPosition.Time; _previewWatch.Restart(); _previewTimer.Start(); PreviewButton.Content = "Pause visual preview";
    }
    private void Preview_Tick(object? sender, object e)
    {
        if (_editor is null) { StopPreview(); return; }
        _previewPosition.SeekTime(_previewStart + (BigInteger)_previewWatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency, _editor.Projection.TotalTime);
        UpdateCursor();
        if (_previewPosition.Time >= _editor.Projection.TotalTime) StopPreview();
    }
    private void StopPreview() { _previewTimer.Stop(); _previewWatch.Stop(); if (PreviewButton is not null) PreviewButton.Content = "Preview · visual only"; }
    private void Scrub_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_sync || _editor is null) return;
        StopPreview(); _previewPosition.Scrub(e.NewValue, _editor.Projection.TotalTime); UpdateCursor();
    }
    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_editor is null || !e.GetCurrentPoint(PathCanvas).Properties.IsLeftButtonPressed) return;
        var point = e.GetCurrentPoint(PathCanvas).Position;
        if (_handle is not null && Math.Abs(point.X - Canvas.GetLeft(_handle) - 9) < 14 && Math.Abs(point.Y - Canvas.GetTop(_handle) - 9) < 14)
        { StopPreview(); _dragging = PathCanvas.CapturePointer(e.Pointer); e.Handled = true; return; }
        var nearest = _display.Concat(_selectedDisplay).Where(s => s.Position?.Space == _space)
            .Select(s => (Sample: s, P: Map(s.Position!.Value))).OrderBy(s => Math.Pow(s.P.X - point.X, 2) + Math.Pow(s.P.Y - point.Y, 2)).FirstOrDefault();
        if (nearest.Sample.Position is not null) ActionsList.SelectedItem = _editor.Projection.ActionAt(nearest.Sample.Index);
    }
    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var point = e.GetCurrentPoint(PathCanvas).Position;
        DestinationX.Text = Math.Clamp(Math.Round((point.X - 18) / _scale + _minX), int.MinValue, int.MaxValue).ToString("0", CultureInfo.InvariantCulture);
        DestinationY.Text = Math.Clamp(Math.Round((point.Y - 18) / _scale + _minY), int.MinValue, int.MaxValue).ToString("0", CultureInfo.InvariantCulture);
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
        if (_dragging) { _drafts.Remove(DestinationX); _drafts.Remove(DestinationY); }
        _dragging = false; PathCanvas?.ReleasePointerCaptures();
    }
    private void Samples_Changed(object sender, RoutedEventArgs e) => DrawPath();
    private void Editor_ThemeChanged(FrameworkElement sender, object args) => DrawPath();
    private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawPath();
    private void Editor_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Workspace is null) return;
        var narrow = ActualWidth < 700; var medium = ActualWidth < 1050;
        Workspace.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(260);
        Workspace.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Workspace.ColumnDefinitions[2].Width = medium ? new GridLength(0) : new GridLength(290);
        Grid.SetColumn(PathPanel, narrow ? 0 : 1); Grid.SetRow(PathPanel, narrow ? 1 : 0);
        Grid.SetColumn(InspectorPanel, medium ? 0 : 2); Grid.SetRow(InspectorPanel, narrow ? 2 : medium ? 1 : 0);
        Grid.SetColumnSpan(InspectorPanel, medium && !narrow ? 2 : 1);
        var height = Math.Max(330, ActualHeight - 95);
        ListPanel.Height = narrow ? 220 : height; PathPanel.Height = narrow ? 420 : height; InspectorPanel.Height = medium ? 440 : height;
    }
}
