using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class LibraryNamingAndRenameTests
{
    private DirectoryInfo _directory = null!;
    private RecordingLibraryStore Store => new(_directory.FullName);
    private MainWindowViewModel ViewModel(IRecordingLibraryStore? store = null) => new(new FakeRecordEngine(), new FakePlaybackEngine(), store ?? Store);
    [TestInitialize] public void Initialize() => _directory = Directory.CreateTempSubdirectory("macro-library-polish-");
    [TestCleanup] public void Cleanup() => _directory.Delete(recursive: true);

    [TestMethod]
    public void DefaultNameUsesLocalDateAndSkipsCaseInsensitiveCollisions()
    {
        var now = new DateTimeOffset(2026, 9, 20, 23, 45, 0, TimeSpan.FromHours(-4));
        var first = RecordingNames.NewDefault(now, []);
        Assert.AreEqual("Recording 2026-09-20 23.45", first);
        Assert.AreEqual(first + " (3)", RecordingNames.NewDefault(now, [first.ToUpperInvariant(), first + " (2)"]));
    }

    [TestMethod]
    public async Task NewDraftsIncludePersistedAndOpenNamesAndPreserveExplicitNames()
    {
        var now = new DateTimeOffset(2026, 9, 20, 3, 32, 0, TimeSpan.FromHours(-4));
        var firstName = RecordingNames.NewDefault(now, []);
        using (var previous = ViewModel()) await previous.CreateDraftAsync(firstName);
        using var vm = ViewModel();
        await vm.InitializeLibraryAsync();
        vm.ActiveMacro!.Name = firstName + " (2)";
        Assert.AreEqual(firstName + " (3)", vm.NewRecordingName(now));
        var first = await vm.CreateDraftAsync();
        var second = await vm.CreateDraftAsync();
        Assert.AreNotEqual(first.Name, second.Name);
        Assert.AreEqual("New recording", (await vm.CreateDraftAsync("New recording")).Name);
        Assert.AreEqual("Weekly report", (await vm.CreateDraftAsync("Weekly report")).Name);
        Assert.AreEqual(firstName, (await Store.ListAsync()).Items.Single(item => item.Name == firstName).Name);
    }

    [TestMethod]
    public async Task InvalidDraftNameDoesNotCreateADocument()
    {
        using var vm = ViewModel();
        var count = vm.MacroTabs.Count;
        await Assert.ThrowsAsync<ArgumentException>(() => vm.CreateDraftAsync("   "));
        Assert.AreEqual(count, vm.MacroTabs.Count);
        Assert.AreEqual(0, (await Store.ListAsync()).Items.Count);
    }

    [TestMethod]
    public async Task RenamingClosedRecordingPreservesPayloadIdentityAndActiveDocument()
    {
        Guid id;
        byte[] bytes;
        using (var previous = ViewModel())
        {
            var macro = await previous.CreateDraftAsync("Before");
            macro.AddEvent(new KeyboardEvent(VirtualKey.A, false) { TimeSinceLastEvent = 1234 });
            await previous.SaveRecordingAsync(macro);
            id = macro.RecordingId;
            bytes = (await Store.LoadAsync(id)).MacroBytes;
        }
        using var vm = ViewModel();
        await vm.InitializeLibraryAsync();
        var active = vm.ActiveMacro;
        var count = vm.MacroTabs.Count;
        var draft = new LibraryRenameDraft("Before") { Name = "  Weekly report  " };
        Assert.IsTrue(await draft.SaveAsync(name => vm.RenameRecordingAsync(id, name)));
        var saved = await Store.LoadAsync(id);
        Assert.AreEqual("Weekly report", saved.Metadata.Name);
        Assert.AreEqual(id, saved.Metadata.Id);
        Assert.AreEqual("Weekly report", vm.Library.Single().Name);
        CollectionAssert.AreEqual(bytes, saved.MacroBytes);
        Assert.AreSame(active, vm.ActiveMacro);
        Assert.AreEqual(count, vm.MacroTabs.Count);
    }

    [TestMethod]
    public async Task CancelAndInvalidNamesNeverReachPersistence()
    {
        var writes = 0;
        Task Save(string _) { writes++; return Task.CompletedTask; }
        var cancelled = new LibraryRenameDraft("Before") { Name = "Discard me" };
        Assert.IsTrue(cancelled.Cancel());
        Assert.IsFalse(await cancelled.SaveAsync(Save));
        foreach (var invalid in new[] { " ", new string('a', 201) })
        {
            var draft = new LibraryRenameDraft("Before") { Name = invalid };
            Assert.IsFalse(await draft.SaveAsync(Save));
            Assert.IsNotNull(draft.Error);
            Assert.IsFalse(draft.IsClosed);
        }
        Assert.AreEqual(0, writes);
    }

    [TestMethod]
    public async Task FailedRenameKeepsOriginalNameAndBytesAndAllowsRetry()
    {
        using var vm = ViewModel();
        var macro = await vm.CreateDraftAsync("Before");
        macro.AddEvent(new KeyboardEvent(VirtualKey.B, true));
        await vm.SaveRecordingAsync(macro);
        var original = await Store.LoadAsync(macro.RecordingId);
        var version = macro.ChangeVersion;
        var draft = new LibraryRenameDraft(macro.Name) { Name = "After" };
        using (var held = new FileStream(Path.Combine(_directory.FullName, $"{macro.RecordingId:N}.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.IsFalse(await draft.SaveAsync(name => vm.RenameRecordingAsync(macro.RecordingId, name)));
        Assert.AreEqual("Before", macro.Name);
        Assert.AreEqual(version, macro.ChangeVersion);
        Assert.IsFalse(macro.IsDirty);
        Assert.AreEqual("Before", vm.Library.Single().Name);
        var unchanged = await Store.LoadAsync(macro.RecordingId);
        Assert.AreEqual(original.Metadata, unchanged.Metadata);
        CollectionAssert.AreEqual(original.MacroBytes, unchanged.MacroBytes);
        Assert.AreEqual("After", draft.Name);
        Assert.IsNotNull(draft.Error);
        Assert.IsFalse(draft.IsClosed);
        Assert.IsTrue(await draft.SaveAsync(name => vm.RenameRecordingAsync(macro.RecordingId, name)));
        Assert.AreEqual("After", macro.Name);
        Assert.IsFalse(macro.IsDirty);
        Assert.AreEqual("After", (await Store.LoadAsync(macro.RecordingId)).Metadata.Name);
    }

    [TestMethod]
    public async Task CancellingAfterFailedRenameDoesNotLeakTheAttemptedNameIntoLaterSave()
    {
        using var vm = ViewModel();
        var macro = await vm.CreateDraftAsync("Keep this name");
        var draft = new LibraryRenameDraft(macro.Name) { Name = "Discard this name" };
        using (var held = new FileStream(Path.Combine(_directory.FullName, $"{macro.RecordingId:N}.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.IsFalse(await draft.SaveAsync(name => vm.RenameRecordingAsync(macro.RecordingId, name)));
        Assert.IsTrue(draft.Cancel());
        macro.AddEvent(new KeyboardEvent(VirtualKey.C, false));
        await vm.FlushLibraryAsync();
        Assert.AreEqual("Keep this name", (await Store.LoadAsync(macro.RecordingId)).Metadata.Name);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PendingRenameRejectsDuplicateCommitAndPreservesNewerEdits(bool changeName)
    {
        var store = new PausingStore(Store);
        using var vm = ViewModel(store);
        var macro = await vm.CreateDraftAsync("Before");
        var active = vm.ActiveMacro;
        store.Pause = true;
        var draft = new LibraryRenameDraft(macro.Name) { Name = "Requested name" };
        var save = draft.SaveAsync(name => vm.RenameRecordingAsync(macro.RecordingId, name));
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(draft.IsSaving);
        Assert.IsFalse(draft.Cancel());
        Assert.IsFalse(await draft.SaveAsync(_ => throw new AssertFailedException("Duplicate save")));
        Assert.AreEqual("Before", macro.Name);
        if (changeName) macro.Name = "Newer name";
        else macro.AddEvent(new KeyboardEvent(VirtualKey.D, false));
        store.Release.SetResult();
        Assert.IsTrue(await save);
        Assert.IsTrue(macro.IsDirty);
        Assert.AreSame(active, vm.ActiveMacro);
        Assert.AreEqual(changeName ? "Newer name" : "Requested name", macro.Name);
        Assert.AreEqual("Requested name", (await Store.LoadAsync(macro.RecordingId)).Metadata.Name);
        await vm.FlushLibraryAsync();
        var latest = await Store.LoadAsync(macro.RecordingId);
        Assert.AreEqual(macro.Name, latest.Metadata.Name);
        Assert.AreEqual(changeName ? 0 : 1, latest.Metadata.EventCount);
        Assert.IsFalse(macro.IsDirty);
    }

    [TestMethod]
    public async Task RenameOfOpenDirtyDocumentPersistsItsCurrentInputWithoutSwitchingTabs()
    {
        using var vm = ViewModel();
        var macro = await vm.CreateDraftAsync("Before");
        macro.AddEvent(new KeyboardEvent(VirtualKey.A, false));
        var other = await vm.CreateDraftAsync("Other");
        await vm.RenameRecordingAsync(macro.RecordingId, "After");
        Assert.AreSame(other, vm.ActiveMacro);
        Assert.AreEqual(1, (await Store.LoadAsync(macro.RecordingId)).Metadata.EventCount);
        Assert.AreEqual("After", macro.Name);
        Assert.IsFalse(macro.IsDirty);
    }

    [TestMethod]
    public async Task RenameIsRejectedDuringCapture()
    {
        using var vm = ViewModel();
        var macro = await vm.CreateDraftAsync("Before");
        Assert.IsTrue(vm.StartRecording());
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenameRecordingAsync(macro.RecordingId, "After"));
        Assert.AreEqual("Before", macro.Name);
        await vm.StopRecordingAsync();
    }

    private sealed class PausingStore(IRecordingLibraryStore inner) : IRecordingLibraryStore
    {
        public bool Pause { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RecordingLibraryRead> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public Task<StoredRecording> LoadAsync(Guid id, CancellationToken cancellationToken = default) => inner.LoadAsync(id, cancellationToken);
        public async Task SaveAsync(StoredRecording recording, CancellationToken cancellationToken = default)
        {
            if (Pause) { Started.TrySetResult(); await Release.Task; }
            await inner.SaveAsync(recording, cancellationToken);
        }
    }
}
