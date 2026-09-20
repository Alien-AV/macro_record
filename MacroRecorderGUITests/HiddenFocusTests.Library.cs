using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MacroRecorderGUITests;

public sealed partial class HiddenFocusTests
{
    private static async Task CheckLibraryControls()
    {
        var temporary = Directory.CreateTempSubdirectory("macro-hidden-library-");
        try
        {
            using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), new RecordingLibraryStore(temporary.FullName));
            var one = await vm.CreateDraftAsync("Alpha");
            var two = await vm.CreateDraftAsync("Beta");
            var three = await vm.CreateDraftAsync("Alpine");
            var library = new LibraryView();
            var cards = Field<GridView>(library, "Cards");
            var select = Field<ToggleButton>(library, "SelectToggle");
            var trash = Field<ToggleButton>(library, "TrashToggle");
            var search = Field<TextBox>(library, "SearchBox");
            var count = Field<TextBlock>(library, "SelectionCount");
            var batch = Field<Button>(library, "BatchActionButton");
            LibraryCard[] LibraryCards() => vm.Library.Select(item => new LibraryCard(item.Id, item.Name, item.Summary, "Saved", new([], "", ""))).ToArray();
            library.SetCards(LibraryCards());
            Assert.AreEqual(ListViewSelectionMode.None, cards.SelectionMode);
            Assert.IsTrue(cards.IsItemClickEnabled, "Normal card activation must still open with one click.");
            Assert.AreEqual("Options for Alpha", LibraryCards().Single(item => (Guid)item.Key == one.RecordingId).OptionsLabel);
            select.IsChecked = true;
            Assert.AreEqual(ListViewSelectionMode.Extended, cards.SelectionMode, "Use native keyboard, Ctrl, and Shift selection.");
            Assert.IsFalse(cards.IsItemClickEnabled);
            cards.SelectedItems.Add(cards.Items[0]);
            cards.SelectedItems.Add(cards.Items[1]);
            Assert.AreEqual("2 selected · 3 visible", count.Text);
            Assert.IsTrue(batch.IsEnabled);
            var selectionChanges = 0; cards.SelectionChanged += (_, _) => selectionChanges++;
            cards.SelectedItems.Add(cards.Items[2]);
            Assert.AreEqual(1, selectionChanges, "Ordinary selection must preserve the native range anchor.");
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
        }
        finally { temporary.Delete(recursive: true); }
    }
}
