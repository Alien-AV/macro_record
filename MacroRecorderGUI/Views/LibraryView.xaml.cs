using MacroRecorderGUI.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;

namespace MacroRecorderGUI.Views;

public sealed record LibraryCard(object Key, string Name, string Summary, string LastOpened, LibraryThumbnail Thumbnail)
{
    public string AccessibleName => $"{Name}, {Summary}, {LastOpened}, {Thumbnail.Label}, {Thumbnail.InputSummary}";
    public string TraceLabel => Thumbnail.Label;
    public string RenameLabel => $"Rename {Name}";
    public double ArtHeight => Thumbnail.HasTrace ? 132 : 88;
    public Visibility TraceVisibility => Thumbnail.HasTrace ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InputVisibility => Thumbnail.HasTrace ? Visibility.Collapsed : Visibility.Visible;
}

public sealed partial class LibraryView : UserControl
{
    private IReadOnlyList<LibraryCard> _cards = [];
    public event EventHandler<LibraryCard>? OpenRequested;
    public Func<LibraryCard, string, Task>? RenameAsync { get; set; }
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
        RecordingHint.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        Add("Export .macro…", () => ExportRequested?.Invoke(this, card));
        menu.ShowAt(button);
    }
    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LibraryCard card } button || RenameAsync is not { } rename) return;
        var draft = new LibraryRenameDraft(card.Name);
        var name = new TextBox { Header = "Recording name", Text = card.Name, MaxLength = 200, MinWidth = 220, MaxWidth = 320 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 320,
            Foreground = DesignResources.Brush(this, "MacroRedBrush"), Visibility = Visibility.Collapsed };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var save = new Button { Content = "Save", Style = (Style)Application.Current.Resources["MacroPrimaryButtonStyle"] };
        var cancel = new Button { Content = "Cancel" };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(cancel); actions.Children.Add(save);
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(name); body.Children.Add(error); body.Children.Add(actions);
        var flyout = new Flyout { Content = body, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        flyout.Closing += (_, args) => args.Cancel = !draft.Cancel();
        flyout.Opened += (_, _) => { name.Focus(FocusState.Programmatic); name.SelectAll(); };
        cancel.Click += (_, _) => flyout.Hide();
        async Task SaveAsync()
        {
            if (draft.IsSaving || draft.IsClosed) return;
            draft.Name = name.Text;
            name.IsEnabled = save.IsEnabled = cancel.IsEnabled = false;
            save.Content = "Saving…";
            if (await draft.SaveAsync(value => rename(card, value))) flyout.Hide();
            else
            {
                error.Text = draft.Error ?? "Could not save the name. Try again.";
                error.Visibility = Visibility.Visible;
                name.IsEnabled = save.IsEnabled = cancel.IsEnabled = true;
                save.Content = "Save";
                name.Focus(FocusState.Programmatic);
            }
        }
        save.Click += async (_, _) => await SaveAsync();
        name.KeyDown += async (_, args) =>
        {
            if (args.Key == VirtualKey.Enter) { args.Handled = true; await SaveAsync(); }
            else if (args.Key == VirtualKey.Escape) { args.Handled = true; flyout.Hide(); }
        };
        flyout.ShowAt(button);
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
        if (!card.Thumbnail.HasTrace) return;
        var samples = card.Thumbnail.Samples;
        var origin = samples[0].Position!.Value;
        var bounds = new PathBounds(origin.X, origin.Y, origin.X, origin.Y);
        foreach (var sample in samples) bounds = bounds.Include(sample.Position!.Value);
        var spanX = bounds.MaxX - bounds.MinX; var spanY = bounds.MaxY - bounds.MinY;
        var scale = Math.Min(Math.Max(1, canvas.ActualWidth - 16) / Math.Max(1, spanX), Math.Max(1, canvas.ActualHeight - 16) / Math.Max(1, spanY));
        var viewport = new PathViewport(bounds.MinX, bounds.MinY, scale,
            (canvas.ActualWidth - spanX * scale) / 2, (canvas.ActualHeight - spanY * scale) / 2);
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
