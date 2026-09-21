using System.Collections.ObjectModel;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;

namespace MacroRecorderGUI.ViewModels;

public partial class MainWindowViewModel
{
    private readonly ObservableCollection<RecordingLibraryItem> _library = [];
    private readonly SemaphoreSlim _libraryGate = new(1, 1);
    private readonly IRecordingLibraryStore _libraryStore;
    private bool _libraryInitialized;
    private bool _shuttingDown;
    public ReadOnlyObservableCollection<RecordingLibraryItem> Library { get; }
    public IReadOnlyList<string> LibraryWarnings { get; private set; } = [];
    public string? LibraryError { get; private set; }

    public IEnumerable<RecordingLibraryItem> SearchLibrary(string? query) => Library.Where(item => item.Matches(query));

    public async Task InitializeLibraryAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _libraryGate.WaitAsync();
        try
        {
            if (_libraryInitialized) return;
            var read = await _libraryStore.ListAsync();
            _library.Clear();
            foreach (var item in read.Items) _library.Add(item);
            LibraryWarnings = read.Warnings;
            LibraryError = null;
            _libraryInitialized = true;
            OnPropertyChanged(nameof(LibraryWarnings));
            OnPropertyChanged(nameof(LibraryError));
        }
        catch (Exception error) { ReportLibraryError(error); throw; }
        finally { _libraryGate.Release(); }
    }

    public string NewRecordingName(DateTimeOffset? localTime = null) => RecordingNames.NewDefault(localTime ?? DateTimeOffset.Now,
        Library.Select(item => item.Name).Concat(MacroTabs.Select(macro => macro.Name)));

    public async Task<MacroViewModel> CreateDraftAsync(string? name = null, CancellationToken cancellationToken = default)
    {
        EnsureLibraryWritable();
        await InitializeLibraryAsync();
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLibraryWritable();
        var draftName = name is null ? NewRecordingName() : RecordingNames.Validate(name);
        var macro = AddNewTab();
        macro.Name = draftName;
        await SaveRecordingAsync(macro, cancellationToken);
        return macro;
    }

    public async Task RenameRecordingAsync(Guid id, string name)
    {
        EnsureLibraryWritable();
        name = RecordingNames.Validate(name);
        await _libraryGate.WaitAsync();
        try
        {
            EnsureLibraryWritable();
            EnsureRecordingAvailable(id);
            var macro = MacroTabs.FirstOrDefault(item => item.RecordingId == id);
            if (macro is not null && (HasRecordingDrain(macro) || ReferenceEquals(PlayingMacro, macro)))
                throw new InvalidOperationException("Stop this recording before renaming it.");
            var stored = macro is null ? await _libraryStore.LoadAsync(id) : null;
            EnsureLibraryWritable();
            var previousName = macro?.Name ?? stored!.Metadata.Name;
            if (name == previousName) return;
            var version = macro?.ChangeVersion ?? 0;
            var bytes = macro?.SnapshotBytes() ?? stored!.MacroBytes;
            var metadata = RecordingLibraryStore.Describe(id, name, macro?.IsDraft ?? stored!.Metadata.IsDraft,
                macro?.CreatedAt ?? stored!.Metadata.CreatedAt, DateTimeOffset.UtcNow, bytes);
            await _libraryStore.SaveAsync(new(metadata, bytes));
            // Publish the name only after persistence succeeds. A newer document edit must remain dirty.
            if (macro is not null)
            {
                if (macro.Name == previousName)
                {
                    macro.Name = name;
                    version++;
                }
                macro.Saved(version, metadata.UpdatedAt);
            }
            var previous = _library.FirstOrDefault(item => item.Id == id);
            if (previous is not null) _library.Remove(previous);
            _library.Insert(0, metadata);
            LibraryError = null;
            OnPropertyChanged(nameof(LibraryError));
        }
        catch (Exception error) { ReportLibraryError(error); throw; }
        finally { _libraryGate.Release(); }
    }

    public async Task<MacroViewModel> OpenRecordingAsync(Guid id, bool select = true, CancellationToken cancellationToken = default)
    {
        EnsureLibraryWritable();
        await _libraryGate.WaitAsync(cancellationToken);
        try
        {
            EnsureLibraryWritable();
            EnsureRecordingAvailable(id);
            if (MacroTabs.FirstOrDefault(macro => macro.RecordingId == id) is { } existing)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (select) SelectedTabIndex = MacroTabs.IndexOf(existing);
                return existing;
            }
            var record = await _libraryStore.LoadAsync(id, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureLibraryWritable();
            var result = new MacroViewModel(record.Metadata.Name, PlaybackEngine, PlayMacroAsync);
            result.Restore(record);
            result.ContentReplaced += MacroContentReplaced;
            MacroTabs.Add(result);
            if (select) SelectedTabIndex = MacroTabs.Count - 1;
            return result;
        }
        finally { _libraryGate.Release(); }
    }

    public async Task<MacroViewModel> ImportRecordingAsync(string path)
    {
        EnsureLibraryWritable();
        var bytes = await FileOperations.ReadMacroBytesAsync(path);
        EnsureLibraryWritable();
        var now = DateTimeOffset.UtcNow;
        var metadata = RecordingLibraryStore.Describe(Guid.NewGuid(), Path.GetFileNameWithoutExtension(path), false, now, now, bytes);
        var macro = AddNewTab();
        macro.Restore(new(metadata, bytes, Recovered: true));
        await SaveRecordingAsync(macro);
        return macro;
    }

    public Task ExportRecordingAsync(MacroViewModel macro, string path)
    {
        EnsureLibraryWritable();
        return FileOperations.WriteMacroBytesAsync(path, macro.SnapshotBytes());
    }

    public async Task SaveRecordingAsync(MacroViewModel macro, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _libraryGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureRecordingAvailable(macro.RecordingId);
            if (!MacroTabs.Contains(macro)) throw new InvalidOperationException("This recording is no longer open. Open it again before saving.");
            await SaveRecordingCoreAsync(macro, cancellationToken);
            LibraryError = null;
            OnPropertyChanged(nameof(LibraryError));
        }
        catch (Exception error) { macro.SaveFailed(error); ReportLibraryError(error); throw; }
        finally { _libraryGate.Release(); }
    }

    private async Task SaveRecordingCoreAsync(MacroViewModel macro, CancellationToken cancellationToken = default)
    {
        var version = macro.ChangeVersion;
        macro.BeginSave();
        var bytes = macro.SnapshotBytes();
        var metadata = RecordingLibraryStore.Describe(macro.RecordingId, macro.Name, macro.IsDraft,
            macro.CreatedAt, DateTimeOffset.UtcNow, bytes);
        await _libraryStore.SaveAsync(new(metadata, bytes), cancellationToken);
        macro.Saved(version, metadata.UpdatedAt);
        var previous = _library.FirstOrDefault(item => item.Id == metadata.Id);
        if (previous is not null) _library.Remove(previous);
        _library.Insert(0, metadata);
    }

    public async Task FlushLibraryAsync()
    {
        foreach (var macro in MacroTabs.ToArray())
            if (macro.IsDirty && (macro.ChangeVersion != 0 || macro.SavedAt is not null)) await SaveRecordingAsync(macro);
        await _libraryGate.WaitAsync();
        try
        {
            if (MacroTabs.Any(macro => macro.IsDirty && (macro.ChangeVersion != 0 || macro.SavedAt is not null)))
                throw new InvalidOperationException("A recording changed while saving. Save again before closing.");
        }
        finally { _libraryGate.Release(); }
    }

    public async Task CloseRecordingAsync(MacroViewModel macro)
    {
        EnsureRecordingAvailable(macro.RecordingId);
        if (HasRecordingDrain(macro))
            throw new InvalidOperationException("Stop recording before closing its document.");
        if (ReferenceEquals(PlayingMacro, macro)) AbortPlayback();
        if (ReferenceEquals(PlayingMacro, macro)) throw new InvalidOperationException("Playback could not be stopped.");
        if (macro.IsDirty && (macro.ChangeVersion != 0 || macro.SavedAt is not null)) await SaveRecordingAsync(macro);
        EnsureRecordingAvailable(macro.RecordingId);
        if (macro.IsDirty && (macro.ChangeVersion != 0 || macro.SavedAt is not null))
            throw new InvalidOperationException("This recording changed while saving. It remains open; save again before closing.");
        if (ReferenceEquals(PlayingMacro, macro) || HasRecordingDrain(macro))
            throw new InvalidOperationException("This recording became active while saving. Stop it before closing.");
        CloseTab(macro);
    }

    public async Task ShutdownAsync()
    {
        if (_disposed) return;
        _shuttingDown = true;
        try
        {
            AbortPlayback();
            if (PlayingMacro is not null) throw new InvalidOperationException("Playback could not be stopped.");
            await StopRecordingAsync();
            await FlushLibraryAsync();
            Dispose();
        }
        catch { _shuttingDown = false; throw; }
    }

    private void EnsureLibraryWritable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_shuttingDown) throw new InvalidOperationException("The recording library is closing.");
    }

    private void ReportLibraryError(Exception error)
    {
        LibraryError = error.Message;
        OnPropertyChanged(nameof(LibraryError));
        if (!_disposed) StatusMessageRequested?.Invoke(this, $"Could not save or load recording: {error.Message}");
    }
}
