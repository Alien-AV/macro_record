using MacroRecorderGUI.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace MacroRecorderGUI.Views;

public sealed record LibraryCard(object Key, string Name, string Summary, string LastOpened, IReadOnlyList<PathSample> Samples, string? ThumbnailError = null)
{
    public string AccessibleName => $"{Name}, {Summary}, {LastOpened}";
    public string TraceLabel => ThumbnailError ?? (Samples.FirstOrDefault(s => s.Position is not null).Position?.Space switch
    {
        CoordinateSpace.RelativeCounts => "Relative device counts · starting position unknown",
        CoordinateSpace.AbsoluteDesktop => "Recorded path · virtual desktop pixels",
        CoordinateSpace.AbsolutePrimary => "Recorded path · primary screen pixels",
        _ => "Keyboard / raw input · no pointer path"
    });
}

public sealed partial class LibraryView : UserControl
{
    private IReadOnlyList<LibraryCard> _cards = [];
    public event EventHandler<LibraryCard>? OpenRequested;
    public event EventHandler<LibraryCard>? RenameRequested;
    public event EventHandler<LibraryCard>? ExportRequested;
    public LibraryView() => InitializeComponent();
    public void SetCards(IReadOnlyList<LibraryCard> cards) { _cards = cards; Filter(); }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (Cards is not null) Filter(); }
    private void Filter()
    {
        var query = SearchBox.Text.Trim();
        var visible = _cards.Where(card => card.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        Cards.ItemsSource = visible;
        CountLabel.Text = $"{_cards.Count} {(_cards.Count == 1 ? "recording" : "recordings")}";
        EmptyState.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = _cards.Count == 0 ? "Your library starts here" : "No matching recordings";
        EmptyDescription.Text = _cards.Count == 0 ? "Record a task or import an existing .macro file." : "Try another name.";
    }
    private void Cards_ItemClick(object sender, ItemClickEventArgs e) => OpenRequested?.Invoke(this, (LibraryCard)e.ClickedItem);
    private void CardOptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LibraryCard card } button) return;
        var menu = new MenuFlyout();
        void Add(string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("Open recording", () => OpenRequested?.Invoke(this, card));
        Add("Rename…", () => RenameRequested?.Invoke(this, card));
        Add("Export .macro…", () => ExportRequested?.Invoke(this, card));
        menu.ShowAt(button);
    }
    private void View_SizeChanged(object sender, SizeChangedEventArgs e)
        => ResizeCards(e.NewSize.Width);
    private void ResizeCards(double width)
    {
        if (Cards is null) return;
        var narrow = width < 600;
        LibraryLayout.Padding = narrow ? new Thickness(18, 22, 18, 22) : new Thickness(30, 27, 30, 27);
        Grid.SetRow(SearchHost, narrow ? 1 : 0); Grid.SetColumn(SearchHost, narrow ? 0 : 1);
        Grid.SetColumnSpan(SearchHost, narrow ? 2 : 1);
        SearchHost.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        if (Cards.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            var available = Math.Max(240, width - LibraryLayout.Padding.Left - LibraryLayout.Padding.Right + 20);
            var columns = Math.Clamp((int)(available / 280), 1, 3);
            panel.ItemWidth = available / columns;
        }
    }
    private void View_ThemeChanged(FrameworkElement sender, object args)
    {
        RefreshTheme();
    }
    public void RefreshTheme() { if (Cards is not null) Redraw(Cards); }
    private void Redraw(DependencyObject parent)
    {
        if (parent is Canvas canvas && canvas.DataContext is LibraryCard) Draw(canvas);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) Redraw(VisualTreeHelper.GetChild(parent, i));
    }
    private void Thumbnail_Loaded(object sender, RoutedEventArgs e) { Draw((Canvas)sender); ResizeCards(ActualWidth); }
    private void Thumbnail_SizeChanged(object sender, SizeChangedEventArgs e) => Draw((Canvas)sender);
    private void Draw(Canvas canvas)
    {
        canvas.Children.Clear();
        if (canvas.DataContext is not LibraryCard card || canvas.ActualWidth < 1 || canvas.ActualHeight < 1) return;
        // Each coordinate frame and capture segment stays separate. A thumbnail never implies a screen origin.
        var first = card.Samples.FirstOrDefault(s => s.Position is not null);
        if (first.Position is not { } origin) return;
        var samples = card.Samples.Where(s => s.Position?.Space == origin.Space).ToArray();
        var bounds = new PathBounds(origin.X, origin.Y, origin.X, origin.Y);
        foreach (var sample in samples) bounds = bounds.Include(sample.Position!.Value);
        var viewport = PathViewport.Fit(bounds, canvas.ActualWidth, canvas.ActualHeight);
        var geometry = new PathGeometry();
        PathFigure? figure = null; PolyLineSegment? segment = null;
        var previousSegment = -1;
        foreach (var sample in samples)
        {
            var mapped = viewport.Map(sample.Position!.Value); var point = new Point(mapped.X, mapped.Y);
            if (figure is null || sample.StartsSegment || previousSegment != sample.Segment)
            {
                figure = new PathFigure { StartPoint = point, IsFilled = false, IsClosed = false };
                segment = new PolyLineSegment(); figure.Segments.Add(segment); geometry.Figures.Add(figure);
            }
            else segment!.Points.Add(point);
            previousSegment = sample.Segment;
        }
        var brush = DesignResources.Brush(this, "MacroPathBrush");
        canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = brush, StrokeThickness = 2.5 });
        foreach (var sample in new[] { samples[0], samples[^1] })
        {
            var p = viewport.Map(sample.Position!.Value);
            var dot = new Ellipse { Width = 8, Height = 8, Stroke = brush, StrokeThickness = 1.5, Fill = DesignResources.Brush(this, "MacroPaperBrush") };
            Canvas.SetLeft(dot, p.X - 4); Canvas.SetTop(dot, p.Y - 4); canvas.Children.Add(dot);
        }
    }
}
