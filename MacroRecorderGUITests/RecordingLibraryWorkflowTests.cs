using Google.Protobuf;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class RecordingLibraryWorkflowTests
{
    private DirectoryInfo _directory = null!;
    [TestInitialize] public void Initialize() => _directory = Directory.CreateTempSubdirectory("macro-library-tests-");
    [TestCleanup] public void Cleanup() => _directory.Delete(recursive: true);
    private string Root => Path.Combine(_directory.FullName, "library");
    private RecordingLibraryStore Store() => new(Root);
    private MainWindowViewModel ViewModel(IRecordingLibraryStore? store = null) => new(new FakeRecordEngine(), new FakePlaybackEngine(), store ?? Store());
    private static byte[] Wire(ulong delay = 987654321) => SerializeEvents.SerializeEventsToByteArray([
        new KeyboardEvent(VirtualKey.A, false) { TimeSinceLastEvent = delay },
        new KeyboardEvent(VirtualKey.A, true) { TimeSinceLastEvent = 9 }
    ]);
    private static StoredRecording Record(string name = "Weekly report", Guid? id = null, byte[]? bytes = null)
    {
        bytes ??= Wire();
        var now = DateTimeOffset.UtcNow;
        return new(RecordingLibraryStore.Describe(id ?? Guid.NewGuid(), name, false, now, now, bytes), bytes);
    }

    [TestMethod]
    public async Task FailedAtomicReplacePreservesPreviousSnapshotAndRemovesTemporaryFile()
    {
        var store = Store();
        var first = Record();
        await store.SaveAsync(first);
        var path = Path.Combine(Root, $"{first.Metadata.Id:N}.json");
        var previous = await File.ReadAllBytesAsync(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(Record("Edited", first.Metadata.Id, Wire(42))));
        CollectionAssert.AreEqual(previous, await File.ReadAllBytesAsync(path));
        CollectionAssert.AreEqual(first.MacroBytes, (await store.LoadAsync(first.Metadata.Id)).MacroBytes);
        Assert.AreEqual(0, Directory.GetFiles(Root, "*.tmp").Length);
    }

    [TestMethod]
    public async Task CancelledSavePreservesPreviousSnapshot()
    {
        var store = Store();
        var first = Record();
        await store.SaveAsync(first);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync(Record("New name", first.Metadata.Id), cancellation.Token));
        Assert.AreEqual(first.Metadata, (await store.LoadAsync(first.Metadata.Id)).Metadata);
    }

    [TestMethod]
    public async Task CorruptCurrentCopyRecoversAndRepairKeepsHealthyBackup()
    {
        var store = Store();
        var first = Record();
        await store.SaveAsync(first);
        await store.SaveAsync(Record("Second save", first.Metadata.Id, Wire(1)));
        var path = Path.Combine(Root, $"{first.Metadata.Id:N}.json");
        await File.WriteAllTextAsync(path, "interrupted or corrupt content");
        var recovered = await store.LoadAsync(first.Metadata.Id);
        Assert.IsTrue(recovered.Recovered);
        Assert.AreEqual(first.Metadata, recovered.Metadata);
        var listing = await store.ListAsync();
        Assert.AreEqual(1, listing.Warnings.Count);
        await store.SaveAsync(recovered);
        Assert.IsFalse((await store.LoadAsync(first.Metadata.Id)).Recovered);
        await File.WriteAllTextAsync(path, "corrupt again");
        Assert.AreEqual(first.Metadata, (await store.LoadAsync(first.Metadata.Id)).Metadata);
    }

    [TestMethod]
    public async Task UnreadableEntryIsReportedWithoutHidingHealthyRecordings()
    {
        var store = Store();
        var good = Record();
        await store.SaveAsync(good);
        await File.WriteAllTextAsync(Path.Combine(Root, $"{Guid.NewGuid():N}.json"), "bad json");
        var result = await store.ListAsync();
        Assert.AreEqual(good.Metadata, result.Items.Single());
        Assert.AreEqual(1, result.Warnings.Count);
    }

    [TestMethod]
    public async Task ImportRenameExportIsByteExactIncludingUnknownProtobufFields()
    {
        var bytes = Wire(ulong.MaxValue).Concat(new byte[] { 0xA0, 0x06, 0x7B }).ToArray();
        var source = Path.Combine(_directory.FullName, "source.macro");
        var export = Path.Combine(_directory.FullName, "export.macro");
        await File.WriteAllBytesAsync(source, bytes);
        using var vm = ViewModel();
        var macro = await vm.ImportRecordingAsync(source);
        macro.Name = "Renamed recording";
        await vm.SaveRecordingAsync(macro);
        await vm.ExportRecordingAsync(macro, export);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(export));
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(source));
        Assert.AreEqual(ulong.MaxValue, macro.Events[0].TimeSinceLastEvent);
        Assert.AreEqual(RecordingSaveState.Saved, macro.SaveState);
        Assert.IsFalse(macro.IsDraft);
        Assert.IsTrue(vm.Library.Single().Matches("renamed"));
        Assert.IsTrue(vm.Library.Single().Matches("keyboard"));
        Assert.AreEqual("18446744073709551624", vm.Library.Single().DurationMicroseconds);

        macro.Events[0].TimeSinceLastEvent = 3;
        await vm.ExportRecordingAsync(macro, export);
        var expected = ProtobufInputEventList.Parser.ParseFrom(bytes);
        expected.InputEvents[0].TimeSinceLastEvent = 3;
        CollectionAssert.AreEqual(expected.ToByteArray(), await File.ReadAllBytesAsync(export));
        Assert.IsTrue(macro.IsDirty); // Export is not a library save.
    }

    [TestMethod]
    public async Task StableIdsAllowDuplicateNamesAndDetachedReadsNeverSelectOrMutate()
    {
        Guid id;
        using (var vm = ViewModel())
        {
            var one = await vm.CreateDraftAsync("Same name");
            one.AddEvent(new KeyboardEvent(VirtualKey.B, false));
            await vm.SaveRecordingAsync(one);
            id = one.RecordingId;
            var two = await vm.CreateDraftAsync("Same name");
            Assert.AreNotEqual(id, two.RecordingId);
            var count = vm.MacroTabs.Count;
            var snapshot = await vm.LoadRecordingPreviewAsync(id);
            snapshot.Events[0].TimeSinceLastEvent = 99;
            Assert.AreSame(two, vm.ActiveMacro);
            Assert.AreEqual(count, vm.MacroTabs.Count);
            Assert.AreEqual(0ul, one.Events[0].TimeSinceLastEvent);
        }
        using var reopened = ViewModel();
        await reopened.InitializeLibraryAsync();
        Assert.AreEqual(2, reopened.SearchLibrary("same NAME").Count());
        var loaded = await reopened.OpenRecordingAsync(id);
        Assert.AreEqual(id, loaded.RecordingId);
        Assert.AreEqual(0ul, loaded.Events[0].TimeSinceLastEvent);
        Assert.IsFalse(loaded.IsDirty);
        Assert.AreSame(loaded, await reopened.OpenRecordingAsync(id));
    }

    [TestMethod]
    public async Task SaveFailureKeepsDirtyDocumentAndLastSavedMetadata()
    {
        using var vm = ViewModel();
        var macro = await vm.CreateDraftAsync("Before");
        var savedAt = macro.SavedAt;
        macro.Name = "After";
        using (var held = new FileStream(Path.Combine(Root, $"{macro.RecordingId:N}.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<IOException>(() => vm.SaveRecordingAsync(macro));
        Assert.IsTrue(macro.IsDirty);
        Assert.AreEqual(RecordingSaveState.Failed, macro.SaveState);
        Assert.IsNotNull(macro.SaveError);
        Assert.AreEqual(savedAt, macro.SavedAt);
        Assert.AreEqual("Before", vm.Library.Single().Name);
        Assert.AreEqual("Before", (await Store().LoadAsync(macro.RecordingId)).Metadata.Name);
        await vm.SaveRecordingAsync(macro);
        Assert.IsFalse(macro.IsDirty);
    }

    [TestMethod]
    public async Task EditDuringAwaitedSaveRemainsDirtyAndFlushPersistsLatestRevision()
    {
        var store = new DelayedStore(Store());
        using var vm = ViewModel(store);
        var macro = vm.ActiveMacro!;
        macro.Name = "First revision";
        var save = vm.SaveRecordingAsync(macro);
        await store.Started.Task;
        Assert.AreEqual(RecordingSaveState.Saving, macro.SaveState);
        macro.Name = "Newer revision";
        store.Release.TrySetResult();
        await save;
        Assert.IsTrue(macro.IsDirty);
        Assert.AreEqual(RecordingSaveState.Dirty, macro.SaveState);
        Assert.AreEqual("First revision", (await Store().LoadAsync(macro.RecordingId)).Metadata.Name);
        await vm.FlushLibraryAsync();
        Assert.IsFalse(macro.IsDirty);
        Assert.AreEqual("Newer revision", (await Store().LoadAsync(macro.RecordingId)).Metadata.Name);
    }

    [TestMethod]
    public async Task PristineStartupNeverCreatesLibraryButExplicitEmptyDraftDoes()
    {
        var vm = ViewModel();
        await vm.InitializeLibraryAsync();
        await vm.ShutdownAsync();
        Assert.IsFalse(Directory.Exists(Root));
        using var next = ViewModel();
        await next.CreateDraftAsync("Empty but intentional");
        await next.FlushLibraryAsync();
        Assert.AreEqual(1, (await Store().ListAsync()).Items.Count);
    }

    [TestMethod]
    public async Task ShutdownJoinsOutstandingSaveAndRejectsNewWork()
    {
        var store = new DelayedStore(Store());
        var vm = ViewModel(store);
        vm.ActiveMacro!.Name = "Before closing";
        var save = vm.SaveRecordingAsync(vm.ActiveMacro);
        await store.Started.Task;
        var shutdown = vm.ShutdownAsync();
        Assert.IsFalse(shutdown.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateDraftAsync());
        store.Release.TrySetResult();
        await Task.WhenAll(save, shutdown).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual("Before closing", (await Store().ListAsync()).Items.Single().Name);
    }

    [TestMethod]
    public async Task StopAwaitsQueuedTailBeforePlaybackOrSaving()
    {
        var transport = new FakeRecordingTransport();
        var engine = new FakePlaybackEngine();
        using var vm = new QueuedRecordingViewModel(transport, engine, Store());
        Assert.IsTrue(vm.StartRecording());
        var id = transport.Starts.Single();
        transport.Begin(id);
        var stop = vm.StopRecordingAsync();
        Assert.IsFalse(stop.IsCompleted);
        Assert.IsTrue(vm.IsFinalizingRecording);
        transport.Push(id, FakeRecordEngine.MakeKeyboardEvent(0x41, false, 123));
        transport.End(id);
        await vm.PlayActiveMacro(new PlaybackOptions { Countdown = TimeSpan.Zero });
        Assert.AreEqual(0, engine.Starts);
        Assert.IsFalse(stop.IsCompleted, "Native completion does not imply queued UI input was applied.");
        vm.Deliver();
        await stop.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(vm.IsFinalizingRecording);
        Assert.AreEqual(1, vm.RecordedEventCount);
        Assert.AreEqual(123ul, vm.ActiveMacro!.Events.Single().TimeSinceLastEvent);
        await vm.FlushLibraryAsync();
        Assert.AreEqual(1, (await Store().ListAsync()).Items.Single().EventCount);
    }

    [TestMethod]
    public async Task ShutdownDoesNotDropQueuedCaptureTail()
    {
        var transport = new FakeRecordingTransport();
        var vm = new QueuedRecordingViewModel(transport, new FakePlaybackEngine(), Store());
        vm.StartRecording();
        var id = transport.Starts.Single();
        transport.Begin(id);
        var shutdown = vm.ShutdownAsync();
        Assert.IsFalse(shutdown.IsCompleted);
        transport.Push(id, FakeRecordEngine.MakeKeyboardEvent(0x42, false, 456));
        transport.End(id);
        vm.Deliver();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
        var saved = (await Store().ListAsync()).Items.Single();
        Assert.AreEqual(1, saved.EventCount);
        Assert.AreEqual("456", saved.DurationMicroseconds);
    }

    private sealed class QueuedRecordingViewModel(FakeRecordingTransport transport, IPlaybackEngine engine, IRecordingLibraryStore store)
        : MainWindowViewModel(new RecordEngine(transport), engine, store)
    {
        private readonly Queue<Action> _pending = [];
        protected override void InvokeDispatcher(Action action) => _pending.Enqueue(action);
        public void Deliver() { while (_pending.TryDequeue(out var action)) action(); }
    }

    [TestMethod]
    public async Task CloseRefusesToDisposeDocumentEditedWhileSaveWasPending()
    {
        var store = new DelayedStore(Store());
        using var vm = ViewModel(store);
        var macro = vm.ActiveMacro!;
        macro.Name = "Before save";
        var close = vm.CloseRecordingAsync(macro);
        await store.Started.Task;
        macro.Name = "Edit during save";
        store.Release.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => close);
        Assert.IsTrue(vm.MacroTabs.Contains(macro));
        Assert.IsTrue(macro.IsDirty);
        Assert.AreEqual("Edit during save", macro.Name);
        await vm.CloseRecordingAsync(macro);
        Assert.IsFalse(vm.MacroTabs.Contains(macro));
        Assert.AreEqual("Edit during save", (await Store().LoadAsync(macro.RecordingId)).Metadata.Name);
    }

    [TestMethod]
    public async Task ShutdownRefusesToDisposeDocumentEditedWhileSaveWasPending()
    {
        var store = new DelayedStore(Store());
        using var vm = ViewModel(store);
        var macro = vm.ActiveMacro!;
        macro.Name = "Before shutdown";
        var shutdown = vm.ShutdownAsync();
        await store.Started.Task;
        macro.Name = "Edit during shutdown save";
        store.Release.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => shutdown);
        Assert.IsTrue(macro.IsDirty);
        Assert.IsTrue(vm.CanRecord);
        await vm.ShutdownAsync();
        Assert.AreEqual("Edit during shutdown save", (await Store().LoadAsync(macro.RecordingId)).Metadata.Name);
    }

    [TestMethod]
    public async Task OlderRecordingCannotCloseWhileItsTailDrainsAndNewerMacroRecords()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new QueuedRecordingViewModel(transport, new FakePlaybackEngine(), Store());
        var first = vm.ActiveMacro!;
        vm.StartRecording();
        var firstId = transport.Starts.Single();
        transport.Begin(firstId);
        vm.StopRecording();
        var second = vm.AddNewTab();
        vm.StartRecording();
        var secondId = transport.Starts.Last();
        transport.Begin(secondId);
        Assert.AreSame(second, vm.RecordingMacro);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CloseRecordingAsync(first));
        Assert.IsTrue(vm.MacroTabs.Contains(first));
        transport.Push(firstId, FakeRecordEngine.MakeKeyboardEvent(0x41, false, 789));
        transport.End(firstId);
        vm.Deliver();
        Assert.AreEqual(789ul, first.Events.Single().TimeSinceLastEvent);
        Assert.IsTrue(vm.IsRecording);
        await vm.CloseRecordingAsync(first);
        Assert.IsFalse(vm.MacroTabs.Contains(first));
        Assert.AreEqual("789", (await Store().LoadAsync(first.RecordingId)).Metadata.DurationMicroseconds);
        vm.StopRecording(); transport.End(secondId); vm.Deliver();
    }

    private sealed class DelayedStore(IRecordingLibraryStore inner) : IRecordingLibraryStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RecordingLibraryRead> ListAsync(CancellationToken token = default) => inner.ListAsync(token);
        public Task<StoredRecording> LoadAsync(Guid id, CancellationToken token = default) => inner.LoadAsync(id, token);
        public async Task SaveAsync(StoredRecording recording, CancellationToken token = default)
        {
            Started.TrySetResult();
            await Release.Task;
            await inner.SaveAsync(recording, token);
        }
    }
}
