using System.Numerics;
using MacroRecorderGUI.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace MacroRecorderGUI.Views;

public sealed partial class MacroTabContent
{
    private IReadOnlyList<PreviewSegment> _timelineSegments = [];
    private string[] _heldLabels = [];
    private CoordinateSpace _previewInitialSpace;

    private void Wait_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_sync && !_dragging && _drafts.IsEdited(WaitInput)) Wait_Click(sender, e);
    }
    private void Wait_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        Wait_Click(sender, e); e.Handled = true;
    }
    private void Duration_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_sync && !_dragging && _drafts.IsEdited(DurationInput)) Duration_Click(sender, e);
    }
    private void Duration_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        Duration_Click(sender, e); e.Handled = true;
    }
    private void Destination_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_sync && !_dragging && (_drafts.IsEdited(DestinationX) || _drafts.IsEdited(DestinationY))) Destination_Click(sender, e);
    }
    private void Destination_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        Destination_Click(sender, e); e.Handled = true;
    }

    public void StepPreview()
    {
        if (!IsPreviewMode) IsPreviewMode = true;
        if (_preview is not null) SeekPreview(_preview.NextActionTime(_previewPosition.Time));
    }
    private void SeekPreview(BigInteger time)
    {
        StopPreview();
        if (_editor is null) return;
        _previewPosition.SeekTime(time, _editor.Projection.TotalTime);
        UpdatePreviewFrame(); DrawPath();
    }
    private RecordedAction? PreviewPathAction() => _previewFrame?.Current;
    private CoordinateSpace PreviewCoordinateSpace => _previewFrame?.Pointer?.Position?.Space ?? _previewInitialSpace;

    private void UpdatePreviewFrame()
    {
        if (_preview is null || _editor is null || _editor.IsDirty) return;
        _previewFrame = _preview.Seek(_previewPosition.Time);
        var a = _previewFrame.Current;
        PreviewStep.Text = a is null ? "NO ACTIONS" : $"ACTION {a.Number:D2} OF {_editor.Projection.Actions.Count}";
        PreviewTitle.Text = a is null ? "Empty recording" : _previewFrame.Waiting ? "Wait before action" : a.Name;
        PreviewDescription.Text = a is null ? "Record or add input to preview a sequence."
            : _previewFrame.Waiting ? $"{TimeText.Human(a.StartTime + a.Wait - _previewPosition.Time)} until {a.Name}"
            : a.Description + (a.Complete ? "" : " · incomplete sequence");
        PreviewNext.Text = _previewFrame.Waiting && a is not null ? "Next · " + a.Name
            : _previewFrame.Next is { } next ? "Next · " + next.Name : "End of sequence";
        var held = _previewFrame.HeldKeys.Select(ActionProjection.KeyName).Concat(_previewFrame.HeldButtons).ToArray();
        if (!_heldLabels.SequenceEqual(held)) { _heldLabels = held; HeldInputs.ItemsSource = held; }
        HeldNote.Text = held.Length == 0 ? "No recorded keys or buttons held" : "Held in the recorded stream";
        PreviewTiming.Text = $"Original timing · {EditorText.Count(_editor.Projection.Actions.Count, "action")}";
        for (var i = 0; i < _timelineSegments.Count; i++)
        {
            var segment = _timelineSegments[i];
            var active = segment.Start <= _previewPosition.Time && _previewPosition.Time < segment.End;
            if (segment.End == _editor.Projection.TotalTime && _previewPosition.Time == segment.End) active = i == _timelineSegments.Count - 1;
            ((Button)Timeline.Children[i]).Background = BrushResource(active ? "Blue" : segment.Waiting ? "WaitTrack" : "Track");
        }
        UpdateCursor();
    }

    private void BuildTimeline()
    {
        if (Timeline is null) return;
        Timeline.Children.Clear(); Timeline.ColumnDefinitions.Clear();
        _timelineSegments = _preview?.Segments() ?? [];
        if (_editor is null) return;
        _previewInitialSpace = _editor.Projection.Samples.FirstOrDefault(s => s.Position is not null).Position?.Space ?? CoordinateSpace.Unknown;
        // Grouping bounds both the visual tree and inter-segment gaps for very long recordings.
        Timeline.ColumnSpacing = _timelineSegments.Count > 100 ? 1 : 4;
        for (var i = 0; i < _timelineSegments.Count; i++)
        {
            var segment = _timelineSegments[i];
            var duration = segment.End - segment.Start;
            var share = _editor.Projection.TotalTime == 0 ? 1 : (double)(duration * 1_000_000 / _editor.Projection.TotalTime);
            Timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, share), GridUnitType.Star) });
            var button = new Button
            {
                Style = (Style)Resources.MergedDictionaries[0]["DesignedEditorButton"],
                Padding = new Thickness(0), MinWidth = 0, MinHeight = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                Background = BrushResource(segment.Waiting ? "WaitTrack" : "Track"),
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(3)
            };
            var name = segment.Waiting ? $"Wait before action {segment.FirstAction + 1}"
                : segment.FirstAction == segment.LastAction ? $"Action {segment.FirstAction + 1} · {_editor.Projection.Actions[segment.FirstAction].Name}"
                : $"Actions {segment.FirstAction + 1}–{segment.LastAction + 1}";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
            ToolTipService.SetToolTip(button, $"{name} · {TimeText.Seconds(segment.Start)}–{TimeText.Seconds(segment.End)} s");
            button.Click += (_, _) => SeekPreview(segment.Start);
            Grid.SetColumn(button, i); Timeline.Children.Add(button);
        }
        UpdatePreviewFrame();
    }

    private void AddGridDots()
    {
        var dots = new GeometryGroup();
        // Decorative grid; these are not screen coordinates or monitor dimensions.
        for (var x = 24d; x < PathCanvas.ActualWidth; x += 40)
            for (var y = 24d; y < PathCanvas.ActualHeight; y += 40)
                dots.Children.Add(new EllipseGeometry { Center = new(x, y), RadiusX = 1, RadiusY = 1 });
        PathCanvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = dots, Fill = BrushResource("Line"), IsHitTestVisible = false });
    }

    private void AddPathLabel(PathPosition position, string text, bool below)
    {
        var point = Map(position);
        var label = new Border
        {
            Padding = below ? new Thickness(0) : new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(5),
            Background = BrushResource("Paper"), BorderBrush = BrushResource("Line"),
            BorderThickness = new Thickness(below ? 0 : 1), IsHitTestVisible = false,
            Child = new TextBlock { Text = text, FontSize = 13, Foreground = BrushResource("Muted"), TextTrimming = TextTrimming.CharacterEllipsis }
        };
        label.Measure(new Windows.Foundation.Size(Math.Max(0, PathCanvas.ActualWidth - 16), 40));
        Canvas.SetLeft(label, Math.Clamp(point.X + (below ? -12 : 20), 8, Math.Max(8, PathCanvas.ActualWidth - label.DesiredSize.Width - 8)));
        Canvas.SetTop(label, Math.Clamp(point.Y + (below ? 16 : -30), 8, Math.Max(8, PathCanvas.ActualHeight - label.DesiredSize.Height - 8)));
        PathCanvas.Children.Add(label);
    }

    private void ActionRow_Changing(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.Phase == 0) { args.RegisterUpdateCallback(1, ActionRow_Changing); return; }
        StyleRow(args.ItemContainer);
    }
    private void StyleRow(SelectorItem container)
    {
        if (container.ContentTemplateRoot is not FrameworkElement root || container.Content is not RecordedAction a) return;
        var family = a.Kind == ActionKind.Keys ? "Key" : a.Kind == ActionKind.Scroll ? "Scroll"
            : a.Kind is ActionKind.Move or ActionKind.Click or ActionKind.Drag ? "Mouse" : "Raw";
        if (root.FindName("ActionIconTile") is Border tile) tile.Background = BrushResource(family + "Fill");
        if (root.FindName("ActionIcon") is FontIcon icon) icon.Foreground = BrushResource(family == "Raw" ? "Muted" : family + "Ink");
    }
    private void RefreshRowColors()
    {
        if (ActionsList?.ItemsPanelRoot is not { } panel) return;
        foreach (var child in panel.Children.OfType<SelectorItem>()) StyleRow(child);
    }

    private void ResizeWorkspace()
    {
        if (Workspace is null) return;
        var layout = EditorLayout.Fit(WorkspaceScroller.ActualWidth, WorkspaceScroller.ActualHeight);
        Workspace.ColumnDefinitions[0].Width = new GridLength(layout.Stacked ? 1 : 38, GridUnitType.Star);
        Workspace.ColumnDefinitions[1].Width = layout.Stacked ? new GridLength(0) : new GridLength(62, GridUnitType.Star);
        Grid.SetColumn(InspectorPanel, layout.Stacked ? 0 : 1); Grid.SetRow(InspectorPanel, layout.Stacked ? 1 : 0);
        ListPanel.Height = layout.ListHeight; InspectorPanel.Height = layout.InspectorHeight;
        ListPanel.BorderThickness = layout.Stacked ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0);
        InspectorPanel.Padding = new Thickness(layout.Stacked ? 20 : 26, 22, layout.Stacked ? 20 : 26, 22);
        DetailGrid.RowDefinitions[1].Height = new GridLength(Math.Max(240, layout.InspectorHeight - (_rawOpen ? 510 : 360)));
    }
    private void PreviewBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PreviewBody is null) return;
        var stacked = e.NewSize.Width < 720;
        PreviewBody.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 215);
        Grid.SetRow(PreviewCurrent, stacked ? 1 : 0); Grid.SetColumn(PreviewCurrent, stacked ? 0 : 1);
        PreviewPathHost.Height = Math.Max(200, e.NewSize.Height - (stacked ? 235 : 105));
        VirtualLabel.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
    }
}
