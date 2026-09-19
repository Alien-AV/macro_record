namespace MacroRecorderGUI.Editor;

public sealed record PresentationRefresh(IReadOnlyList<RecordedAction> Selection, int ProjectedEvents, int SelectionEventsVisited);
public sealed record PathFrame(IReadOnlyList<PathSample> Overview, IReadOnlyList<PathSample> SelectedSamples,
    CoordinateSpace Space, PathBounds? Bounds, string? GeometryBlockReason, PathPosition? Destination,
    IReadOnlyList<RecordedAction> Landmarks);

/// <summary>The view's refresh/redraw boundary, independent of XAML dispatch and layout events.</summary>
public sealed class EditorPresentation(ActionEditor editor)
{
    private PathFrame? _frame;
    private RecordedAction? _frameSelection;
    private int _frameCount = -1;

    public PresentationRefresh Refresh(RecordedAction? primary, IReadOnlyList<RecordedAction> selection)
    {
        var processed = editor.Refresh();
        if (primary is null || !editor.Projection.IsCurrent(primary) || selection.Any(a => !editor.Projection.IsCurrent(a)))
        {
            selection = editor.SelectedActions();
            if (selection.Count == 0 && editor.Projection.Actions.Count > 0) selection = [editor.Projection.Actions[0]];
        }
        var selectedEvents = editor.RefreshSelection(selection);
        return new(selection, processed, selectedEvents);
    }

    public bool TryGetFrame(RecordedAction? selected, out PathFrame frame)
    {
        frame = null!;
        // Resize/theme/sample-toggle can precede the deferred refresh after any toolbar mutation.
        // Never let those passive handlers replace the projection or consume stale action ranges.
        if (editor.IsDirty || selected is not null && !editor.Projection.IsCurrent(selected)) return false;
        var projection = editor.Projection;
        if (_frame is not null && ReferenceEquals(_frameSelection, selected) && _frameCount == projection.ProcessedCount)
        { frame = _frame; return true; }
        var destination = selected is null ? null : projection.Samples[selected.End - 1].Position;
        var space = destination?.Space ?? CoordinateSpace.Unknown;
        var overview = PathDisplay.Decimate(projection.Samples, 0, projection.Samples.Count);
        var detail = selected is null ? [] : PathDisplay.Decimate(projection.Samples, Math.Max(0, selected.Start - 1), selected.Count + (selected.Start > 0 ? 1 : 0), 512);
        var landmarks = projection.MouseLandmarks;
        var visible = new HashSet<RecordedAction>();
        var count = Math.Min(256, landmarks.Count);
        for (var i = 0; i < count; i++) visible.Add(landmarks[count == 1 ? 0 : (int)((long)i * (landmarks.Count - 1) / (count - 1))]);
        if (selected is { Kind: ActionKind.Click or ActionKind.Drag }) visible.Add(selected);
        frame = new(overview, detail, space, projection.BoundsFor(space), selected is null ? "Select an action." : editor.GeometryBlockReason(selected), destination, visible.ToArray());
        _frame = frame; _frameSelection = selected; _frameCount = projection.ProcessedCount;
        return true;
    }
}

public readonly record struct PathViewport(double MinX, double MinY, double Scale)
{
    public static PathViewport Fit(PathBounds bounds, double width, double height) => new(bounds.MinX, bounds.MinY,
        Math.Min(Math.Max(1, width - 36) / Math.Max(1, bounds.MaxX - bounds.MinX), Math.Max(1, height - 36) / Math.Max(1, bounds.MaxY - bounds.MinY)));
    public (double X, double Y) Map(PathPosition position) => (18 + (position.X - MinX) * Scale, 18 + (position.Y - MinY) * Scale);
}
