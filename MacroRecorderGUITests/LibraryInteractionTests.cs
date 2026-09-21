using System.Runtime.CompilerServices;
using System.Xml.Linq;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class LibraryInteractionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LibraryTargetDoesNotSwitchEditorsAndSavesThePreviousDocument(bool alreadyOpen)
    {
        var store = new LibraryPreparationStore();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), store);
        var target = await vm.CreateDraftAsync("Requested");
        target.AddEvent(new KeyboardEvent(VirtualKey.A, false) { TimeSinceLastEvent = 17 });
        await vm.SaveRecordingAsync(target);
        var bytes = target.SnapshotBytes();
        var id = target.RecordingId;
        if (!alreadyOpen) vm.CloseTab(target);
        var previous = vm.AddNewTab();
        previous.AddEvent(new KeyboardEvent(VirtualKey.B, false));
        var changes = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.ActiveMacro)) changes++; };
        using var lifetime = new ShellRunLifetime();
        var result = await LibraryCommandTarget.OpenAsync(vm, id, lifetime.Begin());
        Assert.AreEqual(id, result.RecordingId);
        Assert.AreSame(previous, vm.ActiveMacro);
        Assert.AreEqual(0, changes, "A settings/preparation load must never detach the active editor.");
        Assert.IsFalse(previous.IsDirty);
        CollectionAssert.AreEqual(bytes, result.SnapshotBytes());
        if (alreadyOpen) Assert.AreSame(target, result);
        Assert.IsFalse(vm.PlaybackState.IsActive);
        Assert.IsFalse(vm.IsRecording);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task StopOrCloseDuringSaveOrLoadCannotHandOffOrCreateTheRequestedTab(bool duringLoad, bool close)
    {
        var store = new LibraryPreparationStore();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), store);
        var target = await vm.CreateDraftAsync("Requested");
        var id = target.RecordingId;
        vm.CloseTab(target);
        var previous = vm.ActiveMacro!;
        previous.AddEvent(new KeyboardEvent(VirtualKey.B, false));
        var blocked = new PreparationBlock();
        if (duringLoad) store.BeforeLoad = blocked.WaitAsync; else store.BeforeSave = blocked.WaitAsync;
        using var lifetime = new ShellRunLifetime();
        var task = LibraryCommandTarget.OpenAsync(vm, id, lifetime.Begin());
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (close) lifetime.Dispose(); else lifetime.Cancel();
        blocked.Release.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => task);
        Assert.AreSame(previous, vm.ActiveMacro);
        Assert.IsFalse(vm.MacroTabs.Any(macro => macro.RecordingId == id));
        Assert.AreEqual(duringLoad ? 1 : 0, store.Loads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangedOrClosedPreviousDocumentDuringSaveDoesNotSwitchOrLoad(bool close)
    {
        var store = new LibraryPreparationStore();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), store);
        var target = await vm.CreateDraftAsync("Requested");
        var id = target.RecordingId;
        vm.CloseTab(target);
        var previous = vm.ActiveMacro!;
        previous.AddEvent(new KeyboardEvent(VirtualKey.B, false));
        var blocked = new PreparationBlock();
        store.BeforeSave = blocked.WaitAsync;
        using var lifetime = new ShellRunLifetime();
        var task = LibraryCommandTarget.OpenAsync(vm, id, lifetime.Begin());
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (close) vm.CloseTab(previous); else previous.AddEvent(new KeyboardEvent(VirtualKey.C, false));
        blocked.Release.SetResult();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => task);
        Assert.AreEqual(0, store.Loads);
        Assert.IsFalse(vm.MacroTabs.Any(macro => macro.RecordingId == id));
        if (!close) { Assert.AreSame(previous, vm.ActiveMacro); Assert.IsTrue(previous.IsDirty); }
    }

    [TestMethod]
    public async Task TargetRemainsBoundToRequestedIdWhenSelectionChangesDuringLoad()
    {
        var store = new LibraryPreparationStore();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), store);
        var target = await vm.CreateDraftAsync("Requested");
        var id = target.RecordingId;
        vm.CloseTab(target);
        var blocked = new PreparationBlock();
        store.BeforeLoad = blocked.WaitAsync;
        using var lifetime = new ShellRunLifetime();
        var task = LibraryCommandTarget.OpenAsync(vm, id, lifetime.Begin());
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var other = vm.AddNewTab();
        blocked.Release.SetResult();
        var result = await task;
        Assert.AreEqual(id, result.RecordingId);
        Assert.AreSame(other, vm.ActiveMacro);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OrdinaryOpenStillSelectsTheRequestedRecording(bool alreadyOpen)
    {
        var store = new LibraryPreparationStore();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), store);
        var target = await vm.CreateDraftAsync("Requested");
        var id = target.RecordingId;
        if (!alreadyOpen) vm.CloseTab(target);
        vm.AddNewTab();
        var opened = await vm.OpenRecordingAsync(id);
        Assert.AreSame(opened, vm.ActiveMacro);
        Assert.AreEqual(id, opened.RecordingId);
    }

    [TestMethod]
    public void LibraryAndTransportKeepSelectionOpeningPlaybackAndOptionsSeparate()
    {
        var directory = SourceDirectory();
        var library = XDocument.Load(Path.Combine(directory, "Views", "LibraryView.xaml"));
        var shell = XDocument.Load(Path.Combine(directory, "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Find(XDocument document, string name) => document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
        Assert.AreEqual("None", (string?)Find(library, "Cards").Attribute("SelectionMode"));
        Assert.AreEqual("True", (string?)Find(library, "Cards").Attribute("IsItemClickEnabled"));
        Assert.AreEqual("CheckBox", Find(library, "SelectionCheckBox").Name.LocalName);
        Assert.IsNull(Find(library, "SelectionCheckBox").Attribute("Visibility"), "Checkboxes are always visible.");
        Assert.IsFalse(library.Descendants().Any(element => (string?)element.Attribute(x + "Name") == "SelectToggle"));
        Assert.AreEqual("Library_KeyDown", (string?)library.Root!.Attribute("KeyDown"));
        Assert.AreEqual("Play_Click", (string?)Find(library, "CardPlayButton").Attribute("Click"));
        Assert.AreEqual("PlaybackOptions_Click", (string?)Find(library, "CardPlaybackOptionsButton").Attribute("Click"));
        Assert.AreEqual("{StaticResource MacroPrimaryButtonStyle}", (string?)Find(shell, "PlayButton").Attribute("Style"));
        Assert.IsNull(Find(shell, "PreviewButton").Attribute("Style"));
        StringAssert.Contains((string)Find(shell, "PreviewLabel").Attribute("Text")!, "simulated");
        StringAssert.Contains((string)Find(shell, "RecordingOptionsButton").Attribute("AutomationProperties.Name")!, "Global");
    }

    [TestMethod]
    public void CardSummariesExposeLoopingCountdownAndScopeInBothLayouts()
    {
        foreach (var compact in new[] { false, true })
        {
            var card = new LibraryCard(Guid.NewGuid(), "Export", "2 actions", "Saved", new([], "", ""))
            {
                IsCompact = compact, PreferencesLoaded = true,
                Playback = new() { RepeatUntilStopped = true, Speed = 2, Countdown = TimeSpan.FromSeconds(7), PointerOrigin = PlaybackPointerOrigin.CurrentPointer }
            };
            StringAssert.Contains(card.PlaybackSummary, "Until stopped");
            StringAssert.Contains(card.PlaybackSummary, "7s");
            StringAssert.Contains(card.PlaybackSummary, "2×");
            StringAssert.Contains(card.PlaybackSummary, "Current pointer");
            StringAssert.Contains(card.PlaybackOptionsLabel, "Export");
            StringAssert.Contains(card.PlaybackOptionsLabel, "does not start playback");
            StringAssert.Contains(card.AccessibleName, "Until stopped");
        }
    }

    private static string SourceDirectory([CallerFilePath] string source = "") => Path.Combine(Path.GetDirectoryName(source)!, "..", "MacroRecorderGUI");

    private sealed class LibraryPreparationStore : IRecordingLibraryStore
    {
        private readonly Dictionary<Guid, StoredRecording> _records = [];
        public Func<Task>? BeforeLoad { get; set; }
        public Func<Task>? BeforeSave { get; set; }
        public int Loads { get; private set; }
        public Task<RecordingLibraryRead> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new RecordingLibraryRead(_records.Values.Select(record => record.Metadata).ToArray(), []));
        public async Task<StoredRecording> LoadAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Loads++;
            if (BeforeLoad is not null) await BeforeLoad();
            return _records[id];
        }
        public async Task SaveAsync(StoredRecording recording, CancellationToken cancellationToken = default)
        {
            if (BeforeSave is not null) await BeforeSave();
            _records[recording.Metadata.Id] = recording;
        }
    }
}
