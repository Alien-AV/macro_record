using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class RecordingDeletionTests
{
    private DirectoryInfo _temporary = null!;
    private string Root => Path.Combine(_temporary.FullName, "library");
    private RecordingLibraryStore Store => new(Root);
    private string PathFor(Guid id) => Path.Combine(Root, $"{id:N}.json");
    private string TrashPath(Guid id) => Path.Combine(Root, ".trash", $"{id:N}.json");
    [TestInitialize] public void Initialize() => _temporary = Directory.CreateTempSubdirectory("macro-delete-tests-");
    [TestCleanup] public void Cleanup() => _temporary.Delete(recursive: true);
    private MainWindowViewModel ViewModel(IRecordingLibraryStore? store = null) => new(new FakeRecordEngine(), new FakePlaybackEngine(), store ?? Store);
    private static StoredRecording Recording(string name = "Example", Guid? id = null)
    {
        // Unknown wire fields must survive both copies of a recoverable deletion.
        var bytes = SerializeEvents.SerializeEventsToByteArray([new KeyboardEvent(VirtualKey.A, false) { TimeSinceLastEvent = 987 }])
            .Concat(new byte[] { 0xA0, 0x06, 0x7B }).ToArray();
        var now = DateTimeOffset.UtcNow;
        return new(RecordingLibraryStore.Describe(id ?? Guid.NewGuid(), name, false, now, now, bytes), bytes);
    }

    [TestMethod]
    public async Task TrashPersistsAcrossRestartAndRestoresExactPrimaryAndBackupWithSameId()
    {
        var first = Recording(); var id = first.Metadata.Id;
        await Store.SaveAsync(first);
        await Store.SaveAsync(Recording("Newer", id));
        var primary = await File.ReadAllBytesAsync(PathFor(id));
        var backup = await File.ReadAllBytesAsync(PathFor(id) + ".bak");
        var change = await Store.DeleteAsync(id);
        Assert.AreEqual("Newer", change.Metadata.Name);
        Assert.IsFalse(File.Exists(PathFor(id))); Assert.IsFalse(File.Exists(PathFor(id) + ".bak"));
        Assert.AreEqual(0, (await Store.ListAsync()).Items.Count);
        Assert.AreEqual(id, (await Store.ListTrashAsync()).Items.Single().Metadata.Id);
        await Assert.ThrowsAsync<RecordingDeletedException>(() => Store.LoadAsync(id));
        await Assert.ThrowsAsync<RecordingDeletedException>(() => Store.SaveAsync(first));
        await Store.RestoreAsync(id);
        CollectionAssert.AreEqual(primary, await File.ReadAllBytesAsync(PathFor(id)));
        CollectionAssert.AreEqual(backup, await File.ReadAllBytesAsync(PathFor(id) + ".bak"));
        Assert.AreEqual(0, (await Store.ListTrashAsync()).Items.Count);
        Assert.AreEqual(id, (await Store.ListAsync()).Items.Single().Id);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RecoveredAndBackupOnlyEntriesDoNotResurrectAndRemainRecoverable(bool missingPrimary)
    {
        var first = Recording(); var id = first.Metadata.Id;
        await Store.SaveAsync(first); await Store.SaveAsync(Recording("Later", id));
        if (missingPrimary) File.Delete(PathFor(id));
        else await File.WriteAllTextAsync(PathFor(id), "interrupted primary");
        await Store.DeleteAsync(id);
        Assert.AreEqual(0, (await Store.ListAsync()).Items.Count);
        await Store.RestoreAsync(id);
        var recovered = await Store.LoadAsync(id);
        Assert.IsTrue(recovered.Recovered);
        CollectionAssert.AreEqual(first.MacroBytes, recovered.MacroBytes);
        if (missingPrimary) Assert.IsFalse(File.Exists(PathFor(id)));
        else Assert.AreEqual("interrupted primary", await File.ReadAllTextAsync(PathFor(id)));
    }

    [TestMethod]
    public async Task FailureBeforeTrashCommitKeepsBothSavedCopies()
    {
        var record = Recording(); var id = record.Metadata.Id;
        await Store.SaveAsync(record); await Store.SaveAsync(record);
        await File.WriteAllTextAsync(Path.Combine(Root, ".trash"), "blocks trash directory");
        await Assert.ThrowsAsync<IOException>(() => Store.DeleteAsync(id));
        Assert.AreEqual(id, (await Store.ListAsync()).Items.Single().Id);
        Assert.IsTrue(File.Exists(PathFor(id))); Assert.IsTrue(File.Exists(PathFor(id) + ".bak"));
    }

    [TestMethod]
    public async Task LockedDuplicateAfterTrashCommitCannotResurrectEvenAfterRestart()
    {
        var record = Recording(); var id = record.Metadata.Id;
        await Store.SaveAsync(record); await Store.SaveAsync(record);
        using (var held = new FileStream(PathFor(id) + ".bak", FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await Store.DeleteAsync(id);
            Assert.AreEqual(1, result.Warnings.Count);
            Assert.AreEqual(0, (await Store.ListAsync()).Items.Count);
            await Assert.ThrowsAsync<RecordingDeletedException>(() => Store.SaveAsync(record));
        }
        await Store.RestoreAsync(id);
        CollectionAssert.AreEqual(record.MacroBytes, (await Store.LoadAsync(id)).MacroBytes);
    }

    [TestMethod]
    public async Task FailedRestoreRemainsInTrashAndCanBeRetriedWithoutLosingBackup()
    {
        var record = Recording(); var id = record.Metadata.Id;
        await Store.SaveAsync(record); await Store.SaveAsync(record); await Store.DeleteAsync(id);
        await File.WriteAllTextAsync(PathFor(id) + ".bak", "partial earlier restore");
        using (var held = new FileStream(PathFor(id) + ".bak", FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<IOException>(() => Store.RestoreAsync(id));
        Assert.AreEqual(0, (await Store.ListAsync()).Items.Count);
        Assert.AreEqual(id, (await Store.ListTrashAsync()).Items.Single().Metadata.Id);
        await Assert.ThrowsAsync<RecordingDeletedException>(() => Store.SaveAsync(record));
        await Store.RestoreAsync(id);
        CollectionAssert.AreEqual(record.MacroBytes, (await Store.LoadAsync(id)).MacroBytes);
    }

    [TestMethod]
    public async Task CancelledOperationsNeverCommitOrRemoveTrash()
    {
        var record = Recording(); var id = record.Metadata.Id;
        await Store.SaveAsync(record);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Store.DeleteAsync(id, cancelled.Token));
        Assert.AreEqual(id, (await Store.ListAsync()).Items.Single().Id);
        await Store.DeleteAsync(id);
        await Assert.ThrowsAsync<OperationCanceledException>(() => Store.RestoreAsync(id, cancelled.Token));
        Assert.AreEqual(id, (await Store.ListTrashAsync()).Items.Single().Metadata.Id);
        Assert.AreEqual(0, (await Store.ListAsync()).Items.Count);
    }

    [TestMethod]
    public async Task CorruptTrashIsRetainedAndDoesNotExposeResidualBackup()
    {
        var record = Recording(); var id = record.Metadata.Id;
        await Store.SaveAsync(record);
        var bytes = await File.ReadAllBytesAsync(PathFor(id));
        await Store.DeleteAsync(id);
        await File.WriteAllBytesAsync(PathFor(id) + ".bak", bytes);
        await File.WriteAllTextAsync(TrashPath(id), "damaged trash");
        var read = await Store.ListTrashAsync();
        Assert.AreEqual(0, read.Items.Count); Assert.AreEqual(1, read.Warnings.Count);
        Assert.AreEqual(0, (await Store.ListAsync()).Items.Count);
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => Store.RestoreAsync(id));
        Assert.IsTrue(File.Exists(TrashPath(id))); Assert.IsTrue(File.Exists(PathFor(id) + ".bak"));
    }

    [TestMethod]
    public async Task DifferentStoreInstancesSerializeSaveWithDeletion()
    {
        var record = Recording(); var id = record.Metadata.Id;
        await Store.SaveAsync(record);
        var save = Store.SaveAsync(Recording("Before deletion", id));
        var delete = Store.DeleteAsync(id);
        await save; await delete;
        await Assert.ThrowsAsync<RecordingDeletedException>(() => Store.SaveAsync(record));
        Assert.AreEqual(0, (await Store.ListAsync()).Items.Count);
        await Store.RestoreAsync(id);
        Assert.AreEqual("Before deletion", (await Store.LoadAsync(id)).Metadata.Name);
    }

    [TestMethod]
    public async Task DirtyOpenRecordingIsSavedThenClosedAndRestoredWithPreferencesAndExternalFilesIntact()
    {
        var source = Path.Combine(_temporary.FullName, "source.macro");
        var export = Path.Combine(_temporary.FullName, "export.macro");
        var bytes = Recording().MacroBytes;
        await File.WriteAllBytesAsync(source, bytes);
        using var vm = ViewModel();
        var macro = await vm.ImportRecordingAsync(source);
        await vm.ExportRecordingAsync(macro, export);
        var id = macro.RecordingId;
        var preferences = new RunPreferences(new RunPreferenceStore(Path.Combine(_temporary.FullName, "preferences.json")));
        var options = new PlaybackOptions { Speed = 2, RepeatCount = 3 };
        await preferences.SavePlaybackAsync(id, options, default);
        macro.Name = "Unsaved name";
        macro.Events[0].TimeSinceLastEvent = 42;
        var expected = macro.SnapshotBytes();
        var result = await vm.DeleteRecordingsAsync([id, id]);
        Assert.AreEqual(1, result.Succeeded.Count); Assert.AreEqual(0, result.Failed.Count);
        Assert.IsFalse(vm.MacroTabs.Contains(macro)); Assert.AreEqual(0, vm.Library.Count);
        await vm.FlushLibraryAsync();
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(source));
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(export));
        await vm.RestoreRecordingsAsync(vm.LastDeleted);
        var restored = await vm.OpenRecordingAsync(id);
        Assert.AreEqual("Unsaved name", restored.Name);
        CollectionAssert.AreEqual(expected, restored.SnapshotBytes());
        Assert.AreEqual(options, preferences.PlaybackFor(restored.RecordingId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveRecordingAsync(macro));
    }

    [TestMethod]
    public async Task BatchReportsPartialDeleteAndRestoreFailuresAndUndoContainsOnlySuccessfulIds()
    {
        var store = new ControlledStore(Store);
        using var vm = ViewModel(store);
        var first = await vm.CreateDraftAsync("First"); var second = await vm.CreateDraftAsync("Second");
        var third = await vm.CreateDraftAsync("Third");
        store.FailDelete = second.RecordingId;
        var result = await vm.DeleteRecordingsAsync([first.RecordingId, second.RecordingId, third.RecordingId]);
        CollectionAssert.AreEquivalent(new[] { first.RecordingId, third.RecordingId }, result.Succeeded.ToArray());
        Assert.AreEqual(second.RecordingId, result.Failed.Single().Id);
        Assert.AreSame(second, vm.MacroTabs.Single(item => item.RecordingId == second.RecordingId));
        Assert.AreEqual(second.RecordingId, vm.Library.Single().Id);
        CollectionAssert.AreEquivalent(result.Succeeded.ToArray(), vm.LastDeleted.ToArray());
        store.FailRestore = third.RecordingId;
        result = await vm.RestoreRecordingsAsync(vm.LastDeleted);
        Assert.AreEqual(first.RecordingId, result.Succeeded.Single());
        Assert.AreEqual(third.RecordingId, result.Failed.Single().Id);
        Assert.AreEqual(third.RecordingId, vm.LastDeleted.Single());
        await vm.RefreshTrashAsync(); Assert.AreEqual(third.RecordingId, vm.Trash.Single().Metadata.Id);
        StringAssert.Contains(MacroRecorderGUI.MainWindow.DescribeTrashResult(result, true), "1 could not be restored");
    }

    [TestMethod]
    public async Task PendingSaveCompletesBeforeDeleteAndLatestSnapshotIsInTrash()
    {
        var store = new ControlledStore(Store);
        using var vm = ViewModel(store);
        var macro = await vm.CreateDraftAsync("Initial");
        store.PauseSave = true; macro.Name = "Saved during wait";
        var save = vm.SaveRecordingAsync(macro);
        await store.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var delete = vm.DeleteRecordingsAsync([macro.RecordingId]);
        Assert.IsFalse(delete.IsCompleted);
        store.ReleaseSave.TrySetResult();
        await save; Assert.AreEqual(1, (await delete).Succeeded.Count);
        await vm.RestoreRecordingsAsync(vm.LastDeleted);
        Assert.AreEqual("Saved during wait", (await Store.LoadAsync(macro.RecordingId)).Metadata.Name);
    }

    [TestMethod]
    public async Task SaveAndOpenQueuedBehindDeleteCannotResurrectOrReturnDisposedDocument()
    {
        var store = new ControlledStore(Store);
        using var vm = ViewModel(store);
        var macro = await vm.CreateDraftAsync("Initial");
        store.PauseDelete = true;
        var delete = vm.DeleteRecordingsAsync([macro.RecordingId]);
        await store.DeleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var save = vm.SaveRecordingAsync(macro); var open = vm.OpenRecordingAsync(macro.RecordingId);
        Assert.IsFalse(save.IsCompleted); Assert.IsFalse(open.IsCompleted);
        store.ReleaseDelete.TrySetResult();
        await delete;
        await Assert.ThrowsAsync<RecordingDeletedException>(() => save);
        await Assert.ThrowsAsync<RecordingDeletedException>(() => open);
        Assert.AreEqual(0, (await Store.ListAsync()).Items.Count);
        await vm.RestoreRecordingsAsync(vm.LastDeleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveRecordingAsync(macro));
    }

    [TestMethod]
    public async Task OpenAlreadyLoadingIsSerializedWithDelete()
    {
        var record = Recording(); await Store.SaveAsync(record);
        var store = new ControlledStore(Store) { PauseLoad = true };
        using var vm = ViewModel(store);
        var open = vm.OpenRecordingAsync(record.Metadata.Id);
        await store.LoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var delete = vm.DeleteRecordingsAsync([record.Metadata.Id]);
        Assert.IsFalse(delete.IsCompleted);
        store.ReleaseLoad.TrySetResult();
        await open; Assert.AreEqual(1, (await delete).Succeeded.Count);
        Assert.IsFalse(vm.MacroTabs.Any(item => item.RecordingId == record.Metadata.Id));
        Assert.AreEqual(0, (await Store.ListAsync()).Items.Count);
    }

    [TestMethod]
    public async Task NewEditDuringDeleteSaveKeepsDocumentOpenAndOutOfTrash()
    {
        var store = new ControlledStore(Store);
        using var vm = ViewModel(store);
        var macro = await vm.CreateDraftAsync("Initial");
        macro.Name = "Save me"; store.PauseSave = true;
        var delete = vm.DeleteRecordingsAsync([macro.RecordingId]);
        await store.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        macro.Name = "Newer edit";
        store.ReleaseSave.TrySetResult();
        Assert.AreEqual(1, (await delete).Failed.Count);
        Assert.IsTrue(vm.MacroTabs.Contains(macro)); Assert.IsTrue(macro.IsDirty);
        Assert.AreEqual(0, (await Store.ListTrashAsync()).Items.Count);
        await vm.SaveRecordingAsync(macro);
        Assert.AreEqual("Newer edit", (await Store.LoadAsync(macro.RecordingId)).Metadata.Name);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EditDuringDeleteRestoresOrKeepsDirtyDocumentUntilRetry(bool restoreFails)
    {
        var store = new ControlledStore(Store);
        using var vm = ViewModel(store);
        var macro = await vm.CreateDraftAsync("Initial");
        store.PauseDelete = true;
        if (restoreFails) store.FailRestore = macro.RecordingId;
        var delete = vm.DeleteRecordingsAsync([macro.RecordingId]);
        await store.DeleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        macro.Name = "New edit during deletion";
        store.ReleaseDelete.TrySetResult();
        var result = await delete;
        Assert.AreEqual(0, result.Succeeded.Count); Assert.AreEqual(1, result.Failed.Count);
        Assert.IsTrue(vm.MacroTabs.Contains(macro)); Assert.IsTrue(macro.IsDirty);
        if (restoreFails)
        {
            Assert.AreEqual(0, vm.Library.Count);
            await Assert.ThrowsAsync<RecordingDeletedException>(() => vm.CloseRecordingAsync(macro));
            await Assert.ThrowsAsync<RecordingDeletedException>(() => vm.FlushLibraryAsync());
            vm.CloseTab(macro); Assert.IsTrue(vm.MacroTabs.Contains(macro));
            store.FailRestore = null;
            await vm.RestoreRecordingsAsync([macro.RecordingId]);
        }
        await vm.SaveRecordingAsync(macro);
        Assert.AreEqual("New edit during deletion", (await Store.LoadAsync(macro.RecordingId)).Metadata.Name);
    }

    [TestMethod]
    public async Task DirtySaveFailureSkipsDeleteAndKeepsChangesOpen()
    {
        using var vm = ViewModel();
        var macro = await vm.CreateDraftAsync("Saved"); macro.Name = "Unsaved";
        using (var held = new FileStream(PathFor(macro.RecordingId), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.AreEqual(1, (await vm.DeleteRecordingsAsync([macro.RecordingId])).Failed.Count);
        Assert.AreEqual("Unsaved", macro.Name); Assert.IsTrue(macro.IsDirty);
        Assert.AreEqual(RecordingSaveState.Failed, macro.SaveState);
        Assert.IsTrue(vm.MacroTabs.Contains(macro)); Assert.AreEqual(0, (await Store.ListTrashAsync()).Items.Count);
    }

    [TestMethod]
    public async Task PlaybackTargetIsSkippedWithoutAbortWhileOtherSelectedRecordingIsDeleted()
    {
        var playback = new FakePlaybackEngine { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), playback, Store);
        var live = await vm.CreateDraftAsync("Playing"); live.AddEvent(new KeyboardEvent(VirtualKey.A, false));
        var idle = await vm.CreateDraftAsync("Idle");
        var play = live.PlayMacro();
        var result = await vm.DeleteRecordingsAsync([live.RecordingId, idle.RecordingId]);
        Assert.AreEqual(live.RecordingId, result.Failed.Single().Id);
        Assert.AreEqual(idle.RecordingId, result.Succeeded.Single());
        Assert.AreSame(live, vm.PlayingMacro); Assert.AreEqual(0, playback.Aborts);
        playback.Pending.TrySetResult(); await play;
    }

    [TestMethod]
    public async Task CapturingTargetIsSkippedWithoutStoppingIt()
    {
        var recorder = new FakeRecordEngine();
        using var vm = new MainWindowViewModel(recorder, new FakePlaybackEngine(), Store);
        var macro = await vm.CreateDraftAsync("Capturing");
        Assert.IsTrue(vm.StartRecording());
        var result = await vm.DeleteRecordingsAsync([macro.RecordingId]);
        Assert.AreEqual(macro.RecordingId, result.Failed.Single().Id);
        Assert.IsTrue(vm.IsRecording); Assert.IsTrue(vm.MacroTabs.Contains(macro));
        await vm.StopRecordingAsync();
    }

    [TestMethod]
    public async Task DeleteReservationBlocksNewCapturePlaybackAndCloseDuringAwait()
    {
        var store = new ControlledStore(Store);
        var playback = new FakePlaybackEngine();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), playback, store);
        var macro = await vm.CreateDraftAsync("Ready"); macro.AddEvent(new KeyboardEvent(VirtualKey.A, false));
        await vm.SaveRecordingAsync(macro); store.PauseDelete = true;
        var delete = vm.DeleteRecordingsAsync([macro.RecordingId]);
        await store.DeleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(vm.StartRecording()); await macro.PlayMacro(); Assert.AreEqual(0, playback.Starts);
        vm.CloseTab(macro); Assert.IsTrue(vm.MacroTabs.Contains(macro));
        await Assert.ThrowsAsync<RecordingDeletedException>(() => vm.CloseRecordingAsync(macro));
        store.ReleaseDelete.TrySetResult(); Assert.AreEqual(1, (await delete).Succeeded.Count);
    }

    [TestMethod]
    public async Task FinalizingCaptureIsSkippedUntilItsQueuedTailHasBeenApplied()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredRecordingViewModel(transport, Store);
        var macro = await vm.CreateDraftAsync("Finalizing");
        Assert.IsTrue(vm.StartRecording());
        var session = transport.Starts.Single(); transport.Begin(session);
        vm.StopRecording();
        Assert.IsTrue(vm.IsFinalizingRecording);
        Assert.AreEqual(1, (await vm.DeleteRecordingsAsync([macro.RecordingId])).Failed.Count);
        transport.Push(session, FakeRecordEngine.MakeKeyboardEvent(0x41, false, 789));
        transport.End(session);
        Assert.AreEqual(1, (await vm.DeleteRecordingsAsync([macro.RecordingId])).Failed.Count);
        vm.Deliver();
        Assert.AreEqual(1, (await vm.DeleteRecordingsAsync([macro.RecordingId])).Succeeded.Count);
        await vm.RestoreRecordingsAsync(vm.LastDeleted);
        Assert.AreEqual("789", (await Store.LoadAsync(macro.RecordingId)).Metadata.DurationMicroseconds);
    }

    [TestMethod]
    public async Task PendingCreateAndDuplicateDeleteCannotSaveAfterDeletion()
    {
        var store = new ControlledStore(Store) { PauseSave = true };
        using var vm = ViewModel(store);
        var create = vm.CreateDraftAsync("New draft");
        await store.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var id = vm.ActiveMacro!.RecordingId;
        var first = vm.DeleteRecordingsAsync([id]);
        var second = vm.DeleteRecordingsAsync([id]);
        store.ReleaseSave.TrySetResult();
        var macro = await create;
        Assert.AreEqual(id, (await first).Succeeded.Single());
        Assert.AreEqual(id, (await second).Failed.Single().Id);
        Assert.AreEqual(0, (await Store.ListAsync()).Items.Count);
        await Assert.ThrowsAsync<RecordingDeletedException>(() => vm.SaveRecordingAsync(macro));
        Assert.AreEqual(id, vm.LastDeleted.Single());
        await vm.RestoreRecordingsAsync(vm.LastDeleted);
        Assert.AreEqual("New draft", (await vm.OpenRecordingAsync(id)).Name);
    }

    private sealed class DeferredRecordingViewModel(FakeRecordingTransport transport, IRecordingLibraryStore store)
        : MainWindowViewModel(new RecordEngine(transport), new FakePlaybackEngine(), store)
    {
        private readonly Queue<Action> _pending = [];
        protected override void InvokeDispatcher(Action action) => _pending.Enqueue(action);
        public void Deliver() { while (_pending.TryDequeue(out var action)) action(); }
    }

    private sealed class ControlledStore(RecordingLibraryStore inner) : IRecordingLibraryStore, IRecordingTrashStore
    {
        public bool PauseSave { get; set; }
        public bool PauseDelete { get; set; }
        public bool PauseLoad { get; set; }
        public Guid? FailDelete { get; set; }
        public Guid? FailRestore { get; set; }
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DeleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LoadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDelete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseLoad { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RecordingLibraryRead> ListAsync(CancellationToken token = default) => inner.ListAsync(token);
        public Task<RecordingTrashRead> ListTrashAsync(CancellationToken token = default) => inner.ListTrashAsync(token);
        public async Task<StoredRecording> LoadAsync(Guid id, CancellationToken token = default)
        {
            if (PauseLoad) { LoadStarted.TrySetResult(); await ReleaseLoad.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            return await inner.LoadAsync(id, token);
        }
        public async Task SaveAsync(StoredRecording recording, CancellationToken token = default)
        {
            if (PauseSave) { SaveStarted.TrySetResult(); await ReleaseSave.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            await inner.SaveAsync(recording, token);
        }
        public async Task<RecordingTrashChange> DeleteAsync(Guid id, CancellationToken token = default)
        {
            if (id == FailDelete) throw new IOException("Simulated trash failure");
            if (PauseDelete) { DeleteStarted.TrySetResult(); await ReleaseDelete.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            return await inner.DeleteAsync(id, token);
        }
        public Task<RecordingTrashChange> RestoreAsync(Guid id, CancellationToken token = default)
        {
            if (id == FailRestore) throw new IOException("Simulated restore failure");
            return inner.RestoreAsync(id, token);
        }
    }
}
