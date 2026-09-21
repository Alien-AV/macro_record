using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private static async Task CheckLibraryControls()
    {
        var temporary = Directory.CreateTempSubdirectory("macro-hidden-library-");
        Window? window = null;
        try
        {
            using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), new RecordingLibraryStore(temporary.FullName));
            var one = await vm.CreateDraftAsync("Alpha");
            var two = await vm.CreateDraftAsync("Beta");
            await vm.CreateDraftAsync("Alpine");
            var library = new LibraryView();
            window = new Window { Content = library };
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            Assert.IsFalse(IsWindowVisible(hwnd));
            var cards = Field<GridView>(library, "Cards");
            // Realize the compiled template in an unactivated HWND; no desktop input is sent.
            cards.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel/></ItemsPanelTemplate>");
            var compact = Field<ToggleButton>(library, "CompactToggle");
            var trash = Field<ToggleButton>(library, "TrashToggle");
            var search = Field<TextBox>(library, "SearchBox");
            var count = Field<TextBlock>(library, "SelectionCount");
            var batch = Field<Button>(library, "BatchActionButton");
            var toolbar = Field<StackPanel>(library, "SelectionToolbar");
            LibraryCard[] LibraryCards() => vm.Library.Select(item => new LibraryCard(item.Id, item.Name, item.Summary, "Saved", new([], "", ""))
                { PreferencesLoaded = true, Playback = new() { RepeatUntilStopped = item.Id == one.RecordingId } }).ToArray();
            library.SetCards(LibraryCards());
            LayoutControl(library, 1100, 800);
            await Task.Delay(1);
            Assert.AreEqual(ListViewSelectionMode.None, cards.SelectionMode);
            Assert.IsTrue(cards.IsItemClickEnabled);
            Assert.IsFalse(cards.IsMultiSelectCheckBoxEnabled, "Selection is owned by explicit checkboxes, never card bodies.");
            Assert.AreEqual(Visibility.Collapsed, toolbar.Visibility);
            var opened = new List<LibraryCard>();
            var played = new List<LibraryCard>();
            var options = new List<LibraryCard>();
            library.OpenRequested += (_, card) => opened.Add(card);
            library.PlayRequested += (_, card) => played.Add(card);
            library.PlaybackOptionsRequested += (_, card) => options.Add(card);
            foreach (var dense in new[] { false, true })
            {
                compact.IsChecked = dense;
                LayoutControl(library, 1100, 800);
                await Task.Delay(1);
                var first = (LibraryCard)cards.Items[0];
                Assert.AreEqual(dense ? Visibility.Collapsed : Visibility.Visible, first.ArtVisibility);
                var check = CardControl<CheckBox>(0, "SelectionCheckBox");
                Assert.AreEqual(Visibility.Visible, check.Visibility);
                Assert.AreEqual("Select " + first.Name, AutomationProperties.GetName(check));
                Toggle(0); Toggle(1);
                Assert.AreEqual("2 selected · 3 visible", count.Text);
                Assert.AreEqual(Visibility.Visible, toolbar.Visibility);
                Assert.IsTrue(batch.IsEnabled);
                Toggle(0);
                Assert.AreEqual(1, library.SelectedCards.Count);
                Assert.AreEqual(((LibraryCard)cards.Items[1]).Key, library.SelectedCards.Single().Key);
                Toggle(0);
                var openedBefore = opened.Count;
                var playedBefore = played.Count;
                var optionsBefore = options.Count;
                Invoke(CardControl<Button>(0, "CardPlayButton"));
                Assert.AreSame(first, played.Last());
                Assert.HasCount(openedBefore, opened, "Explicit Play must not open the card.");
                Invoke(CardControl<Button>(0, "CardPlaybackOptionsButton"));
                Assert.AreSame(first, options.Last());
                Assert.HasCount(playedBefore + 1, played, "Changing options must not play.");
                Assert.HasCount(optionsBefore + 1, options);
                Invoke((GridViewItem)cards.ContainerFromIndex(0));
                Assert.AreSame(first, opened.Last(), "A card body still opens while checkboxes are selected.");
                Assert.AreEqual(2, library.SelectedCards.Count, "Body, Play and options never change the batch.");
                Toggle(2);
                Assert.HasCount(openedBefore + 1, opened, "Checkbox activation never opens a card.");
                library.ClearSelection();
                Assert.AreEqual(Visibility.Collapsed, toolbar.Visibility);
                Assert.AreEqual(ToggleState.Off, TogglePeer(0).ToggleState);
                var looping = cards.Items.Cast<LibraryCard>().Single(card => (Guid)card.Key == one.RecordingId);
                StringAssert.Contains(looping.PlaybackSummary, "Until stopped");
                StringAssert.Contains(looping.PlaybackSummary, "3s");
                StringAssert.Contains(looping.PlayLabel, "real input");
            }

            Call(library, "SelectAll_Click", library, new RoutedEventArgs());
            search.Text = "Al";
            Assert.AreEqual(2, cards.Items.Count); Assert.AreEqual(2, library.SelectedCards.Count);
            Assert.IsTrue(library.SelectedCards.All(item => item.Name.StartsWith("Al")));
            StringAssert.Contains(Field<TextBlock>(library, "SelectionScope").Text, "Hidden recordings are deselected");
            search.Text = "Beta";
            Assert.AreEqual(0, library.SelectedCards.Count, "Hidden selections are discarded across repeated query changes.");
            Assert.IsFalse(batch.IsEnabled);
            Toggle(0);
            IReadOnlyList<LibraryCard>? requested = null;
            library.DeleteRequested += (_, values) => requested = values;
            Invoke(batch);
            Assert.AreEqual(two.RecordingId, (Guid)requested!.Single().Key);
            var result = await vm.DeleteRecordingsAsync(requested!.Select(item => (Guid)item.Key));
            Assert.AreEqual(1, result.Succeeded.Count);
            library.SetCards(LibraryCards());
            Assert.AreEqual(0, library.SelectedCards.Count); Assert.IsFalse(batch.IsEnabled);
            await vm.RefreshTrashAsync();
            library.SetTrashCards(vm.Trash.Select(item => new LibraryCard(item.Metadata.Id, item.Metadata.Name, item.Metadata.Summary,
                "Deleted", new([], "In local trash", "Restore to open")) { IsDeleted = true }).ToArray());
            library.SetUndoDeleteCount(vm.LastDeleted.Count);
            Assert.AreEqual(Visibility.Visible, Field<Button>(library, "UndoDeleteButton").Visibility);
            trash.IsChecked = true;
            Assert.AreEqual("Deleted recordings", AutomationProperties.GetName(cards));
            Assert.AreEqual("Restore selected", batch.Content);
            Assert.IsFalse(cards.IsItemClickEnabled);
            var trashed = (LibraryCard)cards.Items.Single();
            Assert.AreEqual(Visibility.Collapsed, trashed.RenameVisibility);
            Assert.AreEqual(Visibility.Collapsed, CardControl<Button>(0, "CardPlayButton").Visibility);
            Assert.AreEqual(Visibility.Collapsed, CardControl<Button>(0, "CardPlaybackOptionsButton").Visibility);
            Toggle(0);
            IReadOnlyList<LibraryCard>? restore = null;
            library.RestoreRequested += (_, values) => restore = values;
            Invoke(batch);
            Assert.AreEqual(two.RecordingId, (Guid)restore!.Single().Key);
            await vm.RestoreRecordingsAsync(restore!.Select(item => (Guid)item.Key));
            trash.IsChecked = false;
            search.Text = "";
            library.SetCards(LibraryCards());
            Assert.AreEqual(0, library.SelectedCards.Count, "Changing between trash and the library clears the batch.");
            Call(library, "SelectAll_Click", library, new RoutedEventArgs());
            Assert.AreEqual(3, library.SelectedCards.Count);
            library.ClearSelection();
            Assert.IsTrue(cards.IsItemClickEnabled);
            Call(library, "ResizeCards", 420d);
            Assert.AreEqual(Orientation.Vertical, Field<StackPanel>(library, "LibraryActions").Orientation);
            Assert.AreEqual(Orientation.Vertical, Field<StackPanel>(library, "SelectionActions").Orientation);
            Assert.IsTrue(cards.Items.Cast<LibraryCard>().All(card => card.ActionsRow == 2));
            Call(library, "ResizeCards", 900d);
            Assert.AreEqual(Orientation.Horizontal, Field<StackPanel>(library, "LibraryActions").Orientation);
            Assert.IsTrue(cards.Items.Cast<LibraryCard>().All(card => card.ActionsRow == 1));
            Assert.IsFalse(IsWindowVisible(hwnd));

            T CardControl<T>(int index, string name) where T : FrameworkElement
            {
                LayoutControl(library, 1100, 800);
                var container = (GridViewItem)cards.ContainerFromIndex(index);
                Assert.IsNotNull(container, "Exercise a realized compiled card.");
                container.ApplyTemplate();
                LayoutControl(library, 1100, 800);
                var found = LibraryDescendants(container).OfType<T>().SingleOrDefault(element => element.Name == name);
                Assert.IsNotNull(found, $"Missing {name}: " + string.Join(", ", LibraryDescendants(container).OfType<FrameworkElement>().Select(element => element.GetType().Name + ":" + element.Name)));
                return found;
            }
            IToggleProvider TogglePeer(int index) => (IToggleProvider)FrameworkElementAutomationPeer
                .CreatePeerForElement(CardControl<CheckBox>(index, "SelectionCheckBox")).GetPattern(PatternInterface.Toggle);
            void Toggle(int index) => TogglePeer(index).Toggle();
            static void Invoke(FrameworkElement control) => ((IInvokeProvider)FrameworkElementAutomationPeer
                .CreatePeerForElement(control).GetPattern(PatternInterface.Invoke)).Invoke();
        }
        finally { window?.Close(); temporary.Delete(recursive: true); }
    }

    private static IEnumerable<DependencyObject> LibraryDescendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in LibraryDescendants(child)) yield return descendant;
        }
    }
}
