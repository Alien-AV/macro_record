using MacroRecorderGUI.Models;

namespace MacroRecorderGUI.ViewModels;

public sealed record RecordingBatchFailure(Guid Id, string Name, string Message);
public sealed record RecordingBatchResult(IReadOnlyList<Guid> Succeeded, IReadOnlyList<RecordingBatchFailure> Failed,
    IReadOnlyList<string> Warnings);

public partial class MainWindowViewModel
{
    private readonly HashSet<Guid> _deletingRecordings = [];
    private readonly HashSet<Guid> _deletedRecordings = [];
    private readonly List<Guid> _lastDeleted = [];
    public IReadOnlyList<DeletedRecording> Trash { get; private set; } = [];
    public IReadOnlyList<string> TrashWarnings { get; private set; } = [];
    public IReadOnlyList<Guid> LastDeleted => _lastDeleted.ToArray();
    private IRecordingTrashStore TrashStore => _libraryStore as IRecordingTrashStore
        ?? throw new NotSupportedException("This library does not support recoverable deletion.");

    private bool IsRecordingUnavailable(Guid id) => _deletingRecordings.Contains(id) || _deletedRecordings.Contains(id);
    private void EnsureRecordingAvailable(Guid id)
    {
        if (IsRecordingUnavailable(id)) throw new RecordingDeletedException(id);
    }

    public async Task RefreshTrashAsync()
    {
        EnsureLibraryWritable();
        await _libraryGate.WaitAsync();
        try { await RefreshTrashCoreAsync(); }
        finally { _libraryGate.Release(); }
    }

    private async Task RefreshTrashCoreAsync()
    {
        var read = await TrashStore.ListTrashAsync();
        Trash = read.Items;
        TrashWarnings = read.Warnings;
        OnPropertyChanged(nameof(Trash));
        OnPropertyChanged(nameof(TrashWarnings));
    }

    public async Task<RecordingBatchResult> DeleteRecordingsAsync(IEnumerable<Guid> ids)
    {
        EnsureLibraryWritable();
        var requested = ids.Distinct().ToArray();
        await _libraryGate.WaitAsync();
        var succeeded = new List<Guid>();
        var failed = new List<RecordingBatchFailure>();
        var warnings = new List<string>();
        try
        {
            EnsureLibraryWritable();
            var trash = TrashStore;
            foreach (var id in requested)
            {
                var macro = MacroTabs.FirstOrDefault(item => item.RecordingId == id);
                var name = macro?.Name ?? _library.FirstOrDefault(item => item.Id == id)?.Name ?? id.ToString();
                var deleted = false;
                try
                {
                    EnsureLibraryWritable();
                    EnsureRecordingAvailable(id);
                    if (macro is not null && (HasRecordingDrain(macro) || ReferenceEquals(PlayingMacro, macro)))
                        throw new InvalidOperationException("Stop this recording and wait for captured input to finish before deleting it.");
                    _deletingRecordings.Add(id);
                    if (macro?.IsDirty == true)
                    {
                        try { await SaveRecordingCoreAsync(macro); }
                        catch (Exception error) { macro.SaveFailed(error); throw; }
                        if (macro.IsDirty)
                            throw new InvalidOperationException("This recording changed while saving. It remains open; save again before deleting.");
                    }
                    var version = macro?.ChangeVersion;
                    var change = await trash.DeleteAsync(id);
                    deleted = true;
                    if (macro is not null && macro.ChangeVersion != version)
                    {
                        await trash.RestoreAsync(id);
                        deleted = false;
                        throw new InvalidOperationException("This recording changed while deletion was pending. It was restored and remains open with your edits.");
                    }
                    _deletedRecordings.Add(id);
                    _deletingRecordings.Remove(id);
                    if (macro is not null) CloseTabCore(macro);
                    RemoveLibraryItem(id);
                    warnings.AddRange(change.Warnings);
                    succeeded.Add(id);
                }
                catch (Exception error)
                {
                    if (deleted)
                    {
                        _deletedRecordings.Add(id);
                        RemoveLibraryItem(id);
                        warnings.Add($"{name} remains in local trash. Its newer unsaved edits remain open; restore it before saving or closing.");
                    }
                    failed.Add(new(id, name, error.Message));
                }
                finally { _deletingRecordings.Remove(id); }
            }
            if (succeeded.Count > 0) { _lastDeleted.Clear(); _lastDeleted.AddRange(succeeded); }
            return CompleteLibraryBatch(succeeded, failed, warnings);
        }
        finally { _libraryGate.Release(); }
    }

    public async Task<RecordingBatchResult> RestoreRecordingsAsync(IEnumerable<Guid> ids)
    {
        EnsureLibraryWritable();
        var requested = ids.Distinct().ToArray();
        await _libraryGate.WaitAsync();
        var succeeded = new List<Guid>();
        var failed = new List<RecordingBatchFailure>();
        var warnings = new List<string>();
        try
        {
            EnsureLibraryWritable();
            var trash = TrashStore;
            foreach (var id in requested)
            {
                var name = Trash.FirstOrDefault(item => item.Metadata.Id == id)?.Metadata.Name ?? id.ToString();
                try
                {
                    var change = await trash.RestoreAsync(id);
                    _deletedRecordings.Remove(id);
                    RemoveLibraryItem(id);
                    _library.Insert(0, change.Metadata);
                    _lastDeleted.Remove(id);
                    warnings.AddRange(change.Warnings);
                    succeeded.Add(id);
                }
                catch (Exception error) { failed.Add(new(id, name, error.Message)); }
            }
            return CompleteLibraryBatch(succeeded, failed, warnings);
        }
        finally { _libraryGate.Release(); }
    }

    private RecordingBatchResult CompleteLibraryBatch(List<Guid> succeeded, List<RecordingBatchFailure> failed, List<string> warnings)
    {
        LibraryError = failed.Count == 0 ? null : string.Join(" ", failed.Select(item => $"{item.Name}: {item.Message}"));
        OnPropertyChanged(nameof(LibraryError));
        OnPropertyChanged(nameof(LastDeleted));
        return new(succeeded, failed, warnings);
    }

    private void RemoveLibraryItem(Guid id)
    {
        var previous = _library.FirstOrDefault(item => item.Id == id);
        if (previous is not null) _library.Remove(previous);
    }
}
