using System.ComponentModel;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace MacroRecorderGUI.Views;

public sealed record LibraryCard(object Key, string Name, string Summary, string LastOpened, LibraryThumbnail Thumbnail) : INotifyPropertyChanged
{
    public static string DescribeTiming(ActionProjection projection)
    {
        var timing = $"{EditorText.Count(projection.Actions.Count, "action")} · {TimeText.Human(projection.TotalTime)}";
        var waits = projection.Actions.Count(action => action.Kind == ActionKind.Wait);
        return waits == 0 ? timing : $"{EditorText.Count(waits, "conditional wait")} · duration varies · {timing} recorded timing";
    }

    public bool IsDeleted { get; init; }
    private bool _preferencesLoaded;
    private PlaybackOptions _playback = new();
    public bool PreferencesLoaded { get => _preferencesLoaded; init => _preferencesLoaded = value; }
    public PlaybackOptions Playback { get => _playback; init => _playback = value; }
    public bool IsCompact { get; init; }
    public bool InlineActions { get; init; }
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void UpdatePlaybackPreferences(bool loaded, PlaybackOptions options)
    {
        if (_preferencesLoaded == loaded && _playback == options) return;
        _preferencesLoaded = loaded;
        _playback = options;
        foreach (var property in new[] { nameof(PreferencesLoaded), nameof(Playback), nameof(PlaybackSummary), nameof(AccessibleName), nameof(PlayLabel) })
            PropertyChanged?.Invoke(this, new(property));
    }
    public string PlaybackSummary => RunSettingsPresentation.PlaybackSummary(PreferencesLoaded, Playback);
    public string AccessibleName => $"{Name}, {Summary}, {LastOpened}, {Thumbnail.Label}, {Thumbnail.InputSummary}"
        + (IsDeleted ? ", In local trash" : $", {PlaybackSummary}. Open to edit.");
    public string TraceLabel => Thumbnail.Label;
    public string RenameLabel => $"Rename {Name}";
    public string SelectLabel => $"Select {Name}";
    public string PlayLabel => $"Play {Name}, sends real input. {PlaybackSummary}";
    public string PlaybackOptionsLabel => $"Playback options for {Name}, does not start playback";
    public string OptionsLabel => $"Options for {Name}";
    public Visibility RenameVisibility => IsDeleted ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PlaybackVisibility => IsDeleted ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ArtVisibility => IsCompact ? Visibility.Collapsed : Visibility.Visible;
    public Thickness BodyPadding => IsCompact ? new(12, 10, 12, 10) : new(18, 16, 18, 8);
    public int ActionsRow => InlineActions ? 1 : 2;
    public int ActionsColumn => InlineActions ? 1 : 0;
    public int ActionsColumnSpan => InlineActions ? 1 : 2;
    public double ArtHeight => Thumbnail.HasTrace ? 132 : 88;
    public Visibility TraceVisibility => Thumbnail.HasTrace ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InputVisibility => Thumbnail.HasTrace ? Visibility.Collapsed : Visibility.Visible;
}

public sealed partial class LibraryView : UserControl
{
    private IReadOnlyList<LibraryCard> _cards = [];
    private IReadOnlyList<LibraryCard> _trashCards = [];
    private bool _filtering;
    private double _layoutWidth;
    public bool ShowingTrash => TrashToggle.IsChecked == true;
    public event EventHandler<IReadOnlyList<LibraryCard>>? DeleteRequested;
    public event EventHandler<IReadOnlyList<LibraryCard>>? RestoreRequested;
    public event EventHandler? UndoDeleteRequested;
    public event EventHandler<LibraryCard>? OpenRequested;
    public event EventHandler<LibraryCard>? PlayRequested;
    public event EventHandler<LibraryCard>? PlaybackOptionsRequested;
    public Func<LibraryCard, string, Task>? RenameAsync { get; set; }
    public event EventHandler<LibraryCard>? ExportRequested;
    public IReadOnlyList<LibraryCard> SelectedCards => Cards.Items.Cast<LibraryCard>().Where(card => card.IsSelected).ToArray();
    public LibraryView()
    {
        InitializeComponent();
        IsTabStop = false;
        ClickAwayFocus.Attach(this, this);
    }
    public void SetCards(IReadOnlyList<LibraryCard> cards) { _cards = cards; Filter(); }
    public void SetTrashCards(IReadOnlyList<LibraryCard> cards) { _trashCards = cards; Filter(); }
    internal void RefreshPlaybackPreferences(RunPreferences preferences)
    {
        foreach (var card in _cards.Concat(Cards.Items.Cast<LibraryCard>()))
            if (!card.IsDeleted && card.Key is Guid id) card.UpdatePlaybackPreferences(preferences.IsLoaded, preferences.PlaybackFor(id));
    }
    public void SetUndoDeleteCount(int count)
    {
        UndoDeleteButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UndoDeleteButton.Content = $"Undo delete ({count})";
    }
    public void SetOperationMessage(string message)
    {
        OperationMessage.Text = message;
        OperationMessage.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void Search_Changing(TextBox sender, TextBoxTextChangingEventArgs e) { if (Cards is not null) Filter(); }
    private void Filter()
    {
        var query = SearchBox.Text.Trim();
        var source = ShowingTrash ? _trashCards : _cards;
        var selected = SelectedCards.Select(card => card.Key).ToHashSet();
        var visible = source.Where(card => card.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(card => card with { IsSelected = selected.Contains(card.Key), IsCompact = CompactToggle.IsChecked == true,
                InlineActions = CompactToggle.IsChecked == true && _layoutWidth >= 600 }).ToArray();
        _filtering = true;
        try
        {
            Cards.ItemsSource = visible;
        }
        finally { _filtering = false; }
        CountLabel.Text = $"{visible.Length} of {source.Count} {(ShowingTrash ? "in trash" : "recordings")}";
        SelectionScope.Text = query.Length == 0 ? ShowingTrash ? "Checkboxes select recordings to restore. Escape clears selection."
            : "Checkboxes select recordings. Open a card to edit; Play runs it. Escape clears selection."
            : "Selection applies to visible search results only. Hidden recordings are deselected. Escape clears selection.";
        EmptyState.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = source.Count == 0 ? (ShowingTrash ? "Local trash is empty" : "Your library starts here") : "No matching recordings";
        EmptyDescription.Text = source.Count == 0 ? (ShowingTrash ? "Deleted recordings can be restored here." : "Record a task or import an existing .macro file.") : "Try another name.";
        RecordingHint.Visibility = !ShowingTrash && _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelection();
    }
    private void Cards_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (!ShowingTrash) OpenRequested?.Invoke(this, (LibraryCard)e.ClickedItem);
    }
    private void Selection_Changed(object sender, RoutedEventArgs e)
    {
        if (_filtering || Cards is null || sender is not CheckBox { DataContext: LibraryCard card } checkbox) return;
        card.IsSelected = checkbox.IsChecked == true;
        UpdateSelection();
    }
    private void Compact_Changed(object sender, RoutedEventArgs e) { if (Cards is not null) { Filter(); ResizeCards(ActualWidth); } }
    private void Trash_Changed(object sender, RoutedEventArgs e)
    {
        if (Cards is null) return;
        ClearSelection();
        Cards.IsItemClickEnabled = !ShowingTrash;
        TrashDescription.Visibility = ShowingTrash ? Visibility.Visible : Visibility.Collapsed;
        LibraryHeading.Text = ShowingTrash ? "Local trash" : "Your recordings";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Cards, ShowingTrash ? "Deleted recordings" : "Recordings");
        Filter();
    }
    private void UpdateSelection()
    {
        var count = SelectedCards.Count;
        SelectionToolbar.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectionCount.Text = $"{count} selected · {Cards.Items.Count} visible";
        BatchActionButton.Content = ShowingTrash ? "Restore selected" : "Delete selected";
        BatchActionButton.IsEnabled = count > 0;
        SelectAllButton.IsEnabled = Cards.Items.Count > 0;
        ClearSelectionButton.IsEnabled = count > 0;
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (LibraryCard card in Cards.Items) card.IsSelected = true;
        UpdateSelection();
    }
    public void ClearSelection()
    {
        foreach (LibraryCard card in Cards.Items) card.IsSelected = false;
        UpdateSelection();
    }
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => ClearSelection();
    private void UndoDelete_Click(object sender, RoutedEventArgs e) => UndoDeleteRequested?.Invoke(this, EventArgs.Empty);
    private void BatchAction_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedCards.ToArray();
        if (selected.Length == 0) return;
        if (ShowingTrash) RestoreRequested?.Invoke(this, selected);
        else DeleteRequested?.Invoke(this, selected);
    }
    private void Library_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key == VirtualKey.Escape && SelectedCards.Count > 0) { ClearSelection(); e.Handled = true; return; }
        for (var source = e.OriginalSource as DependencyObject; source is not null && source != this; source = VisualTreeHelper.GetParent(source))
            if (source is TextBox or PasswordBox or RichEditBox or NumberBox) return;
        if (e.Key == VirtualKey.Delete && !ShowingTrash) { BatchAction_Click(sender, e); e.Handled = true; }
        else if (e.Key == VirtualKey.A && (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0) { SelectAll_Click(sender, e); e.Handled = true; }
    }
    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LibraryCard { IsDeleted: false, PreferencesLoaded: true } card }) PlayRequested?.Invoke(this, card);
    }
    private void PlaybackOptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LibraryCard { IsDeleted: false } card }) PlaybackOptionsRequested?.Invoke(this, card);
    }
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
        if (card.IsDeleted) Add("Restore recording", () => RestoreRequested?.Invoke(this, [card]));
        else
        {
            Add("Open recording", () => OpenRequested?.Invoke(this, card));
            Add("Export .macro…", () => ExportRequested?.Invoke(this, card));
            Add("Delete recording", () => DeleteRequested?.Invoke(this, [card]));
        }
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
        var focusSurface = new UserControl { Content = body, IsTabStop = false };
        body.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ClickAwayFocus.Attach(focusSurface, focusSurface);
        var flyout = new Flyout { Content = focusSurface, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
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
        _layoutWidth = width;
        if (Cards.Items.Cast<LibraryCard>().Any(card => card.InlineActions != (CompactToggle.IsChecked == true && !narrow))) Filter();
        LibraryActions.Orientation = SelectionActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        LibraryLayout.Padding = narrow ? new Thickness(18, 22, 18, 22) : new Thickness(30, 27, 30, 27);
        Grid.SetRow(SearchHost, narrow ? 1 : 0); Grid.SetColumn(SearchHost, narrow ? 0 : 1);
        Grid.SetColumnSpan(SearchHost, narrow ? 2 : 1);
        SearchHost.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        if (Cards.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            var available = Math.Max(240, width - LibraryLayout.Padding.Left - LibraryLayout.Padding.Right + 20);
            var columns = CompactToggle.IsChecked == true ? 1 : Math.Clamp((int)(available / 280), 1, 3);
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
