using MacroRecorderGUI;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class LibraryRecordingPreparationTests
{
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, true, true)]
    public async Task CancelDuringLibraryPreparationCannotShowDialogCreateDraftOrReachControllerAndCapture(
        bool initializationAlreadyStarted, bool close, bool fromHotkey)
    {
        var store = new BlockedLibraryStore();
        var transport = new FakeRecordingTransport();
        using var vm = new MainWindowViewModel(new RecordEngine(transport), new FakePlaybackEngine(), store);
        using var lifetime = new ShellRunLifetime();
        var initialization = initializationAlreadyStarted ? vm.InitializeLibraryAsync() : Task.CompletedTask;
        if (initializationAlreadyStarted) await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var run = lifetime.Begin();
        var originalTab = vm.ActiveMacro;
        var dialogs = 0;
        var controllers = 0;
        async Task PrepareAndContinueAsync()
        {
            // Exercise the shared production preparation with a fake dialog and controller continuation.
            var choice = await MainWindow.PrepareRecordingChoicesAsync(vm, run, false, fromHotkey, 123, name =>
            {
                dialogs++;
                return Task.FromResult<RecordingChoices?>(new(name, 3, false, 123));
            });
            Assert.IsNotNull(choice);
            await run.PrepareAsync(async () => { await vm.CreateDraftAsync(choice.Name); });
            controllers++;
            vm.StartRecording(fromHotkey);
        }
        var preparation = PrepareAndContinueAsync();
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsFalse(preparation.IsCompleted);
        Assert.IsTrue(lifetime.Owns(run));
        if (close) lifetime.Dispose();
        else lifetime.Cancel();
        store.Release.SetResult();
        await initialization.WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => preparation.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.AreEqual(0, store.Saves);
        Assert.AreEqual(0, dialogs);
        Assert.AreEqual(0, controllers);
        Assert.AreEqual(0, transport.Starts.Count);
        Assert.AreSame(originalTab, vm.ActiveMacro);
        Assert.AreEqual(1, vm.MacroTabs.Count);
        Assert.IsFalse(vm.IsRecording);
        Assert.AreEqual(1, store.Lists);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SuccessfulPreparationChoosesDistinctDateNameFromTheLoadedLibrary(bool fromHotkey)
    {
        var store = new BlockedLibraryStore();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), store);
        using var lifetime = new ShellRunLifetime();
        var run = lifetime.Begin();
        var dialogs = 0;
        var preparation = MainWindow.PrepareRecordingChoicesAsync(vm, run, false, fromHotkey, 987, name =>
        {
            dialogs++;
            return Task.FromResult<RecordingChoices?>(new(name, 3, false, 987));
        });
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(0, vm.Library.Count);
        // Include neighboring minutes so a clock rollover during the test does not hide the collision.
        var now = DateTimeOffset.Now;
        store.Items = Enumerable.Range(-1, 3).SelectMany(offset =>
        {
            var stem = RecordingNames.NewDefault(now.AddMinutes(offset), []);
            return new[] { stem, stem + " (2)" }.Select(name =>
                new RecordingLibraryItem(Guid.NewGuid(), name, true, now, now, 0, "0", false, false));
        }).ToArray();
        store.Release.SetResult();
        var choice = await preparation.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsNotNull(choice);
        Assert.AreEqual(fromHotkey ? 0 : 1, dialogs);
        StringAssert.StartsWith(choice.Name, "Recording ");
        StringAssert.EndsWith(choice.Name, " (3)");
        Assert.IsFalse(vm.Library.Any(item => item.Name == choice.Name));
        Assert.AreEqual(3, choice.CountdownSeconds);
        Assert.AreEqual(987ul, choice.OverrideDelay);
        Assert.IsFalse(choice.Clear);
        await run.PrepareAsync(async () => { await vm.CreateDraftAsync(choice.Name); });
        Assert.AreEqual(choice.Name, vm.ActiveMacro!.Name);
        Assert.AreEqual(1, store.Saves);
    }

    [TestMethod]
    public async Task EmergencyWhileDialogIsOpenRejectsEvenAnAcceptedDialogResult()
    {
        var store = new BlockedLibraryStore();
        store.Release.SetResult();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), store);
        using var lifetime = new ShellRunLifetime();
        var run = lifetime.Begin();
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<RecordingChoices?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparation = MainWindow.PrepareRecordingChoicesAsync(vm, run, false, false, null, _ =>
        {
            shown.SetResult();
            return result.Task;
        });
        await shown.Task.WaitAsync(TimeSpan.FromSeconds(3));
        lifetime.Cancel();
        result.SetResult(new("User name", 0, false, null));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => preparation.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.AreEqual(0, store.Saves);
        Assert.AreEqual(1, vm.MacroTabs.Count);
    }

    [TestMethod]
    public async Task RecordIntoExistingPreservesTheNameAndDialogCancelReturnsNoChoices()
    {
        var store = new BlockedLibraryStore();
        store.Release.SetResult();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), store);
        vm.ActiveMacro!.Name = "User's existing name";
        using var lifetime = new ShellRunLifetime();
        var choice = await MainWindow.PrepareRecordingChoicesAsync(vm, lifetime.Begin(), true, false, null, name =>
        {
            Assert.AreEqual("User's existing name", name);
            return Task.FromResult<RecordingChoices?>(null);
        });
        Assert.IsNull(choice);
        Assert.AreEqual("User's existing name", vm.ActiveMacro.Name);
        Assert.AreEqual(0, store.Saves);
    }

    private sealed class BlockedLibraryStore : IRecordingLibraryStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<RecordingLibraryItem> Items { get; set; } = [];
        public int Lists { get; private set; }
        public int Saves { get; private set; }
        public async Task<RecordingLibraryRead> ListAsync(CancellationToken cancellationToken = default)
        {
            Lists++;
            Started.TrySetResult();
            await Release.Task;
            return new(Items, []);
        }
        public Task<StoredRecording> LoadAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveAsync(StoredRecording recording, CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
