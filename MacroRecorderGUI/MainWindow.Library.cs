using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;

namespace MacroRecorderGUI;

public sealed partial class MainWindow
{
    private async void Library_DeleteRequested(object? sender, IReadOnlyList<LibraryCard> cards)
        => await OperationAsync(() => ChangeLibraryTrashAsync(cards.Select(card => (Guid)card.Key).ToArray(), restore: false));

    private async void Library_RestoreRequested(object? sender, IReadOnlyList<LibraryCard> cards)
        => await OperationAsync(() => ChangeLibraryTrashAsync(cards.Select(card => (Guid)card.Key).ToArray(), restore: true));

    private async void Library_UndoDeleteRequested(object? sender, EventArgs e)
        => await OperationAsync(() => ChangeLibraryTrashAsync(ViewModel.LastDeleted, restore: true));

    private async Task ChangeLibraryTrashAsync(IReadOnlyList<Guid> ids, bool restore)
    {
        if (RunActive) throw new InvalidOperationException("Wait for recording or playback to finish before changing local trash.");
        ThrowIfClosing();
        var result = restore ? await ViewModel.RestoreRecordingsAsync(ids) : await ViewModel.DeleteRecordingsAsync(ids);
        foreach (var id in result.Succeeded) _thumbnails.Remove(id);
        var message = DescribeTrashResult(result, restore);
        Library.SetOperationMessage(message);
        SetMessage(message);
        await RefreshLibraryAsync();
    }

    internal static string DescribeTrashResult(RecordingBatchResult result, bool restore)
    {
        var count = result.Succeeded.Count;
        var message = restore ? $"Restored {count} recording(s)." : $"Moved {count} recording(s) to local trash. Restore them from Local trash at any time.";
        if (result.Failed.Count > 0)
            message += $" {result.Failed.Count} could not be {(restore ? "restored" : "deleted")}: "
                + string.Join(" ", result.Failed.Select(item => $"{item.Name}: {item.Message}"));
        if (result.Warnings.Count > 0) message += " " + string.Join(" ", result.Warnings);
        return message;
    }
}
