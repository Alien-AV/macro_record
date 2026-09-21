using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using MacroRecorderGUI.Models;

namespace MacroRecorderGUI;

public sealed partial class MainWindow
{
    private async void Library_PlayRequested(object? sender, LibraryCard card)
        => await ExecuteLibraryCommandAsync((Guid)card.Key, playback: true, async (macro, options, lease) =>
        {
            if (!ViewModel.CanPlay || _globalHotkeys?.EmergencyStop is null)
            { SetMessage("An emergency stop shortcut must be registered before playback."); return; }
            if (macro.Events.Count == 0 && macro.PointerOrigins.Count == 0)
            { SetMessage("This recording has no captured input. Open it to add actions or record into it."); return; }
            var prepared = await DirectRunPreparation.PlayAsync(ViewModel, _preferences, lease, macro);
            await RunPreparedPlaybackAsync(prepared with { Options = options }, lease, showEditor: false);
            if (!_closed && !_closing) await lease.PrepareAsync(RefreshLibraryAsync);
        });

    private async void Library_PlaybackOptionsRequested(object? sender, LibraryCard card)
        => await ExecuteLibraryCommandAsync((Guid)card.Key, playback: false, async (macro, options, lease) =>
        {
            await DirectRunPreparation.EditPlaybackOptionsAsync(_preferences, lease, macro.RecordingId,
                _ => Dialogs.PlaybackOptionsAsync(options, macro.Name, EmergencyShortcut, macro));
            await lease.PrepareAsync(() => SaveTargetAsync(macro));
            await lease.PrepareAsync(RefreshLibraryAsync);
        });

    // The injected continuation is the only handoff to execution or a dialog. Hidden checks
    // use a fake continuation; preparation itself never shows a controller or sends input.
    internal Task ExecuteLibraryCommandAsync(Guid id, bool playback,
        Func<MacroViewModel, PlaybackOptions, RunLease, Task> command) => OperationAsync(async () =>
    {
        var lease = _runLifetime.Begin();
        var options = _preferences.IsLoaded ? _preferences.PlaybackFor(id) : null;
        _isRecordingRun = false;
        _preparingRun = playback;
        if (playback) { _activeRun = lease; _runError = null; }
        RefreshShell();
        try
        {
            await lease.PrepareAsync(() => _preferences.InitializeAsync(lease.Token));
            options ??= _preferences.PlaybackFor(id);
            var target = await LibraryCommandTarget.OpenAsync(ViewModel, id, lease);
            ThrowIfClosing();
            if (_editors.TryGetValue(target, out var editor) && editor.TryCommitPendingEdits() == false)
            { SetMessage(editor.Status); return; }
            _runPlaybackOptions = playback ? options : null;
            await command(target, options, lease);
        }
        catch (OperationCanceledException) { if (!_closed) SetMessage(playback ? "Playback cancelled" : "Playback options cancelled"); }
        finally
        {
            _preparingRun = false;
            if (playback) { if (!RunActive) FinishController(lease); }
            else _runLifetime.Complete(lease);
            RefreshShell();
        }
    });

    private async Task SaveTargetAsync(MacroViewModel macro)
    {
        var version = macro.ChangeVersion;
        if (macro.IsDirty && (version > 0 || macro.SavedAt is not null)) await ViewModel.SaveRecordingAsync(macro);
        ThrowIfClosing();
        if (!ViewModel.MacroTabs.Contains(macro) || macro.ChangeVersion != version)
            throw new InvalidOperationException("The recording changed or closed while saving. Try again when ready.");
    }

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
