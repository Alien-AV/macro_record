using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

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
            var three = await vm.CreateDraftAsync("Alpine");
            var library = new LibraryView();
            window = new Window { Content = library };
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            Assert.IsFalse(IsWindowVisible(hwnd));
            var cards = Field<GridView>(library, "Cards");
            // A hidden HWND has no effective onscreen viewport. Realize every test card
            // while retaining the compiled item template and native selection control.
            cards.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel/></ItemsPanelTemplate>");
            var cardsPeer = (GridViewAutomationPeer)FrameworkElementAutomationPeer.CreatePeerForElement(cards);
            var select = Field<ToggleButton>(library, "SelectToggle");
            var trash = Field<ToggleButton>(library, "TrashToggle");
            var search = Field<TextBox>(library, "SearchBox");
            var count = Field<TextBlock>(library, "SelectionCount");
            var batch = Field<Button>(library, "BatchActionButton");
            LibraryCard[] LibraryCards() => vm.Library.Select(item => new LibraryCard(item.Id, item.Name, item.Summary, "Saved", new([], "", ""))).ToArray();
            library.SetCards(LibraryCards());
            LayoutControl(library, 1100, 800);
            Assert.AreEqual(ListViewSelectionMode.None, cards.SelectionMode);
            Assert.IsTrue(cards.IsItemClickEnabled, "Normal card activation must still open with one click.");
            Assert.AreEqual("Options for Alpha", LibraryCards().Single(item => (Guid)item.Key == one.RecordingId).OptionsLabel);
            var opened = new List<LibraryCard>();
            library.OpenRequested += (_, card) => opened.Add(card);
            var first = cards.Items[0];
            ((IInvokeProvider)CardPeer(0).GetPattern(PatternInterface.Invoke)).Invoke();
            Assert.AreSame(first, opened.Single(), "Native card activation opens in normal mode.");
            select.IsChecked = true;
            Assert.AreEqual(ListViewSelectionMode.Multiple, cards.SelectionMode,
                "Multiple toggles on ordinary clicks/Space; Extended replaces selection on ordinary clicks.");
            Assert.IsTrue(cards.IsMultiSelectCheckBoxEnabled);
            Assert.AreEqual(Visibility.Visible, Field<StackPanel>(library, "SelectionToolbar").Visibility);
            Assert.IsFalse(cards.IsItemClickEnabled);
            SelectionPeer(0).AddToSelection();
            SelectionPeer(1).AddToSelection();
            Assert.AreEqual("2 selected · 3 visible", count.Text);
            Assert.IsTrue(batch.IsEnabled);
            SelectionPeer(0).RemoveFromSelection();
            Assert.IsFalse(SelectionPeer(0).IsSelected);
            Assert.IsTrue(SelectionPeer(1).IsSelected);
            Assert.AreEqual("1 selected · 3 visible", count.Text);
            SelectionPeer(0).AddToSelection();
            Assert.HasCount(1, opened, "Selecting must not open a recording.");
            var selectionChanges = 0; cards.SelectionChanged += (_, _) => selectionChanges++;
            SelectionPeer(2).AddToSelection();
            Assert.AreEqual(1, selectionChanges, "Adding a card must not clear/rebuild other selected cards.");
            search.Text = "Al";
            Assert.AreEqual(2, cards.Items.Count); Assert.AreEqual(2, cards.SelectedItems.Count);
            Assert.IsTrue(cards.SelectedItems.Cast<LibraryCard>().All(item => item.Name.StartsWith("Al")));
            search.Text = "Beta";
            Assert.AreEqual(0, cards.SelectedItems.Count, "Hidden selections must be discarded, including when the query changes twice.");
            Assert.IsFalse(batch.IsEnabled);
            Call(library, "SelectAll_Click", library, new RoutedEventArgs());
            Assert.AreEqual(two.RecordingId, (Guid)((LibraryCard)cards.SelectedItems.Single()).Key);
            IReadOnlyList<LibraryCard>? requested = null;
            library.DeleteRequested += (_, values) => requested = values;
            Call(library, "BatchAction_Click", library, new RoutedEventArgs());
            Assert.AreEqual(two.RecordingId, (Guid)requested!.Single().Key);
            var result = await vm.DeleteRecordingsAsync(requested!.Select(item => (Guid)item.Key));
            Assert.AreEqual(1, result.Succeeded.Count);
            library.SetCards(LibraryCards());
            Assert.AreEqual(0, cards.SelectedItems.Count); Assert.IsFalse(batch.IsEnabled);
            await vm.RefreshTrashAsync();
            library.SetTrashCards(vm.Trash.Select(item => new LibraryCard(item.Metadata.Id, item.Metadata.Name, item.Metadata.Summary,
                "Deleted", new([], "In local trash", "Restore to open")) { IsDeleted = true }).ToArray());
            library.SetUndoDeleteCount(vm.LastDeleted.Count);
            Assert.AreEqual(Visibility.Visible, Field<Button>(library, "UndoDeleteButton").Visibility);
            trash.IsChecked = true;
            Assert.AreEqual("Deleted recordings", AutomationProperties.GetName(cards));
            Assert.AreEqual("Restore selected", batch.Content);
            Assert.IsFalse(cards.IsItemClickEnabled);
            Assert.AreEqual(Visibility.Collapsed, ((LibraryCard)cards.Items.Single()).RenameVisibility);
            Call(library, "SelectAll_Click", library, new RoutedEventArgs());
            IReadOnlyList<LibraryCard>? restore = null;
            library.RestoreRequested += (_, values) => restore = values;
            Call(library, "BatchAction_Click", library, new RoutedEventArgs());
            Assert.AreEqual(two.RecordingId, (Guid)restore!.Single().Key);
            await vm.RestoreRecordingsAsync(restore!.Select(item => (Guid)item.Key));
            trash.IsChecked = false;
            search.Text = "";
            library.SetCards(LibraryCards());
            Call(library, "SelectAll_Click", library, new RoutedEventArgs());
            Assert.AreEqual(3, cards.SelectedItems.Count);
            Call(library, "ClearSelection_Click", library, new RoutedEventArgs());
            Assert.AreEqual(0, cards.SelectedItems.Count);
            select.IsChecked = false;
            Assert.IsTrue(cards.IsItemClickEnabled); Assert.AreEqual(ListViewSelectionMode.None, cards.SelectionMode);
            Call(library, "ResizeCards", 420d);
            Assert.AreEqual(Orientation.Vertical, Field<StackPanel>(library, "LibraryActions").Orientation);
            Assert.AreEqual(Orientation.Vertical, Field<StackPanel>(library, "SelectionActions").Orientation);
            Call(library, "ResizeCards", 900d);
            Assert.AreEqual(Orientation.Horizontal, Field<StackPanel>(library, "LibraryActions").Orientation);
            Assert.IsFalse(IsWindowVisible(hwnd));

            AutomationPeer CardPeer(int index)
            {
                LayoutControl(library, 1100, 800);
                var container = (GridViewItem)cards.ContainerFromIndex(index);
                Assert.IsNotNull(container, "Test must exercise a realized compiled card.");
                return FrameworkElementAutomationPeer.CreatePeerForElement(container);
            }
            ISelectionItemProvider SelectionPeer(int index)
            {
                LayoutControl(library, 1100, 800);
                var peers = cardsPeer.GetChildren().OfType<GridViewItemDataAutomationPeer>().ToArray();
                Assert.HasCount(cards.Items.Count, peers);
                return (ISelectionItemProvider)peers[index].GetPattern(PatternInterface.SelectionItem);
            }
        }
        finally { window?.Close(); temporary.Delete(recursive: true); }
    }
}
