using System.Runtime.CompilerServices;
using System.Xml.Linq;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class DirectRunControlsTests
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
    public async Task StopOrCloseDuringLibraryLoadPreventsDialogDraftControllerAndCapture(
        bool alreadyLoading, bool close, bool fromHotkey)
    {
        var blocked = new PreparationBlock();
        var store = new RunTestLibrary { BeforeList = blocked.WaitAsync };
        var transport = new FakeRecordingTransport();
        using var vm = new MainWindowViewModel(new RecordEngine(transport), new FakePlaybackEngine(), store);
        using var lifetime = new ShellRunLifetime();
        var initialization = alreadyLoading ? vm.InitializeLibraryAsync() : Task.CompletedTask;
        var lease = lifetime.Begin();
        var original = vm.ActiveMacro;
        var controllers = 0;
        async Task StartAsync()
        {
            var choice = await DirectRunPreparation.RecordAsync(vm, Preferences(), lease, false, UnexpectedDialog);
            controllers++;
            vm.StartRecording(choice!.Macro, fromHotkey, choice.Clear);
        }
        var task = StartAsync();
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(lifetime.Owns(lease));
        if (close) lifetime.Dispose(); else lifetime.Cancel();
        blocked.Release.SetResult();
        await initialization;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => task);
        Assert.AreEqual(0, controllers);
        Assert.AreEqual(0, transport.Starts.Count);
        Assert.AreEqual(0, store.Saves);
        Assert.AreSame(original, vm.ActiveMacro);
        Assert.AreEqual(1, vm.MacroTabs.Count);
        Assert.AreEqual(1, store.Lists);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task StopOrCloseDuringPreferencesLoadPreventsAnyRunPreparation(bool playback, bool close)
    {
        var blocked = new PreparationBlock();
        var preferenceStore = new MemoryRunPreferenceStore { BeforeLoad = blocked.WaitAsync };
        var store = new RunTestLibrary();
        using var vm = ViewModel(store);
        vm.ActiveMacro!.AddEvent(Input());
        using var lifetime = new ShellRunLifetime();
        var lease = lifetime.Begin();
        var reachedController = false;
        async Task PrepareAsync()
        {
            if (playback) await DirectRunPreparation.PlayAsync(vm, new(preferenceStore), lease, vm.ActiveMacro!);
            else await DirectRunPreparation.RecordAsync(vm, new(preferenceStore), lease, false, UnexpectedDialog);
            reachedController = true;
        }
        var task = PrepareAsync();
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (close) lifetime.Dispose(); else lifetime.Cancel();
        blocked.Release.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => task);
        Assert.IsFalse(reachedController);
        Assert.AreEqual(0, store.Lists);
        Assert.AreEqual(0, store.Saves);
        Assert.AreEqual(1, vm.MacroTabs.Count);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task StopOrCloseDuringDocumentSaveCannotStartOrCreateAnotherDraft(bool playback, bool close)
    {
        var blocked = new PreparationBlock();
        var store = new RunTestLibrary { BeforeSave = blocked.WaitAsync };
        using var vm = ViewModel(store);
        vm.ActiveMacro!.AddEvent(Input());
        using var lifetime = new ShellRunLifetime();
        var lease = lifetime.Begin();
        var controllers = 0;
        async Task PrepareAsync()
        {
            if (playback) await DirectRunPreparation.PlayAsync(vm, Preferences(), lease, vm.ActiveMacro!);
            else await DirectRunPreparation.RecordAsync(vm, Preferences(), lease, false, UnexpectedDialog);
            controllers++;
        }
        var task = PrepareAsync();
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (close) lifetime.Dispose(); else lifetime.Cancel();
        blocked.Release.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => task);
        Assert.AreEqual(0, controllers);
        Assert.AreEqual(1, store.Saves, "Only the already-started document save may complete.");
        Assert.AreEqual(1, vm.MacroTabs.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StopOrCloseDuringNewDraftSaveCannotReachController(bool close)
    {
        var blocked = new PreparationBlock();
        var store = new RunTestLibrary { BeforeSave = blocked.WaitAsync };
        using var vm = ViewModel(store);
        using var lifetime = new ShellRunLifetime();
        var lease = lifetime.Begin();
        var task = DirectRunPreparation.RecordAsync(vm, Preferences(), lease, false, UnexpectedDialog);
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(2, vm.MacroTabs.Count, "This draft was created before Stop.");
        var draft = vm.ActiveMacro;
        if (close) lifetime.Dispose(); else lifetime.Cancel();
        blocked.Release.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => task);
        Assert.AreSame(draft, vm.ActiveMacro);
        Assert.AreEqual(2, vm.MacroTabs.Count, "No later draft may appear.");
        Assert.AreEqual(1, store.Saves);
        Assert.IsFalse(vm.IsRecording);
    }

    [TestMethod]
    public async Task RecordingRetainsTargetAndSharedSettingsAcrossLibraryAwait()
    {
        var blocked = new PreparationBlock();
        var store = new RunTestLibrary { BeforeList = blocked.WaitAsync };
        using var vm = ViewModel(store);
        var original = vm.ActiveMacro!;
        original.Name = "Requested recording";
        var prefs = Preferences(new() { CountdownSeconds = 9, OverrideDelay = 789 });
        await prefs.InitializeAsync();
        var displayed = prefs.Recording;
        using var lifetime = new ShellRunLifetime();
        var task = DirectRunPreparation.RecordAsync(vm, prefs, lifetime.Begin(), true, name =>
        {
            Assert.AreEqual(original.Name, name);
            return Task.FromResult<bool?>(false);
        });
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var other = vm.AddNewTab();
        await prefs.SaveRecordingAsync(new() { CountdownSeconds = 0 }, default);
        blocked.Release.SetResult();
        var prepared = await task;
        Assert.IsNotNull(prepared);
        Assert.AreSame(original, prepared.Macro);
        Assert.AreEqual(displayed, prepared.Options);
        Assert.AreEqual(1, store.Records.Count);
        Assert.IsTrue(store.Records.ContainsKey(original.RecordingId));
        Assert.AreSame(other, vm.ActiveMacro);
    }

    [TestMethod]
    public async Task CancelInsideDraftLibraryInitializationPreventsTheNestedContinuationFromAddingATab()
    {
        var blocked = new PreparationBlock();
        var store = new RunTestLibrary { BeforeList = blocked.WaitAsync };
        using var vm = ViewModel(store);
        using var lifetime = new ShellRunLifetime();
        var lease = lifetime.Begin();
        var task = vm.CreateDraftAsync("Requested draft", lease.Token);
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        lifetime.Cancel();
        blocked.Release.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => task);
        Assert.AreEqual(1, vm.MacroTabs.Count);
        Assert.AreEqual(0, store.Saves);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DirectRecordGeneratesDistinctNameAndUsesSharedOptionsWithoutDialog(bool fromHotkey)
    {
        var store = new RunTestLibrary();
        var now = DateTimeOffset.Now;
        foreach (var offset in Enumerable.Range(-1, 3))
        {
            var stem = RecordingNames.NewDefault(now.AddMinutes(offset), []);
            foreach (var name in new[] { stem, stem + " (2)" })
            {
                var id = Guid.NewGuid();
                store.Records[id] = new(new(id, name, true, now, now, 0, "0", false, false), []);
            }
        }
        using var vm = ViewModel(store);
        var prefs = Preferences(new RecordingOptions { CountdownSeconds = 7, OverrideDelay = 987 });
        using var lifetime = new ShellRunLifetime();
        var result = await DirectRunPreparation.RecordAsync(vm, prefs, lifetime.Begin(), false, UnexpectedDialog);
        Assert.IsNotNull(result);
        StringAssert.StartsWith(result.Macro.Name, "Recording ");
        StringAssert.EndsWith(result.Macro.Name, " (3)");
        Assert.AreEqual(7, result.Options.CountdownSeconds);
        Assert.AreEqual(987ul, result.Options.OverrideDelay);
        Assert.IsFalse(result.Clear);
        Assert.AreEqual(1, store.Saves);
        Assert.IsFalse(vm.IsRecording, "Preparation itself never captures input.");
        Assert.IsTrue(vm.StartRecording(result.Macro, fromHotkey, result.Clear), "Only fake capture is used.");
        vm.StopRecording();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExistingRecordingRequiresScopedAppendOrReplaceAndNeverRenames(bool replace)
    {
        var store = new RunTestLibrary();
        using var vm = ViewModel(store);
        var original = vm.ActiveMacro!;
        original.Name = "User's name";
        original.AddEvent(Input());
        var before = original.SnapshotBytes();
        using var lifetime = new ShellRunLifetime();
        var dialogs = 0;
        var result = await DirectRunPreparation.RecordAsync(vm, Preferences(), lifetime.Begin(), true, name =>
        {
            dialogs++;
            Assert.AreEqual("User's name", name);
            return Task.FromResult<bool?>(replace);
        });
        Assert.IsNotNull(result);
        Assert.AreEqual(1, dialogs);
        Assert.AreSame(original, result.Macro);
        Assert.AreEqual(replace, result.Clear);
        CollectionAssert.AreEqual(before, original.SnapshotBytes(), "Replace must wait until capture starts.");
        Assert.AreEqual("User's name", original.Name);
        Assert.IsTrue(vm.StartRecording(result.Macro, false, result.Clear));
        Assert.AreEqual(replace ? 0 : 1, original.Events.Count);
        vm.StopRecording();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExistingDialogCancelOrEmergencyDoesNotMutateOrSave(bool emergency)
    {
        var store = new RunTestLibrary();
        using var vm = ViewModel(store);
        var original = vm.ActiveMacro!;
        original.Name = "Keep this";
        original.AddEvent(Input());
        var version = original.ChangeVersion;
        using var lifetime = new ShellRunLifetime();
        var lease = lifetime.Begin();
        var shown = new PreparationBlock();
        var task = DirectRunPreparation.RecordAsync(vm, Preferences(), lease, true, async _ =>
        {
            await shown.WaitAsync();
            return emergency ? true : null;
        });
        await shown.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (emergency) lifetime.Cancel();
        shown.Release.SetResult();
        if (emergency) await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => task);
        else Assert.IsNull(await task);
        Assert.AreEqual(version, original.ChangeVersion);
        Assert.AreEqual(0, store.Saves);
        Assert.AreEqual(1, original.Events.Count);
    }

    [TestMethod]
    public async Task PlaybackRetainsTargetAndDisplayedSettingsAcrossSaveAndSelectionChanges()
    {
        var blocked = new PreparationBlock();
        var store = new RunTestLibrary { BeforeSave = blocked.WaitAsync };
        var engine = new FakePlaybackEngine();
        using var vm = ViewModel(store, engine);
        var original = vm.ActiveMacro!;
        original.AddEvent(Input());
        var prefs = Preferences();
        await prefs.SavePlaybackAsync(original.RecordingId, new() { Speed = 2, RepeatCount = 2, Countdown = TimeSpan.Zero }, default);
        var displayed = prefs.PlaybackFor(original.RecordingId);
        using var lifetime = new ShellRunLifetime();
        var task = DirectRunPreparation.PlayAsync(vm, prefs, lifetime.Begin(), original);
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var other = vm.AddNewTab();
        other.AddEvent(Input(777));
        await prefs.SavePlaybackAsync(other.RecordingId, new() { Speed = 4, RepeatUntilStopped = true }, default);
        await prefs.SavePlaybackAsync(original.RecordingId, new() { Speed = 3 }, default);
        blocked.Release.SetResult();
        var prepared = await task;
        Assert.AreSame(original, prepared.Macro);
        Assert.AreEqual(displayed, prepared.Options);
        await vm.PlayMacroAsync(prepared.Macro, prepared.Options);
        Assert.AreEqual(2, engine.Starts);
        Assert.AreEqual(500ul, engine.PlayedEvents.Single().TimeSinceLastEvent);
        Assert.IsFalse(engine.Loop);
        Assert.AreSame(other, vm.ActiveMacro);
        Assert.AreEqual(1000ul, original.Events[0].TimeSinceLastEvent);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangedOrClosedPlaybackTargetDoesNotReachController(bool close)
    {
        var blocked = new PreparationBlock();
        var store = new RunTestLibrary { BeforeSave = blocked.WaitAsync };
        using var vm = ViewModel(store);
        var macro = vm.ActiveMacro!;
        macro.AddEvent(Input());
        using var lifetime = new ShellRunLifetime();
        var task = DirectRunPreparation.PlayAsync(vm, Preferences(), lifetime.Begin(), macro);
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (close) vm.CloseTab(macro); else macro.AddEvent(Input());
        blocked.Release.SetResult();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => task);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task OptionsApplyOnlyPersistsAndCancelNeverMutatesDocumentOrSettings(bool playback, bool apply)
    {
        var store = new RunTestLibrary();
        var engine = new FakePlaybackEngine();
        using var vm = ViewModel(store, engine);
        var macro = vm.ActiveMacro!;
        macro.AddEvent(Input());
        var before = (macro.Name, macro.ChangeVersion, macro.SaveState, macro.SavedAt, macro.IsDirty);
        var bytes = macro.SnapshotBytes();
        var preferenceStore = new MemoryRunPreferenceStore();
        var prefs = new RunPreferences(preferenceStore);
        using var lifetime = new ShellRunLifetime();
        var lease = lifetime.Begin();
        if (playback)
            await DirectRunPreparation.EditPlaybackOptionsAsync(prefs, lease, macro.RecordingId,
                options => Task.FromResult(apply ? options with { Speed = 2, RepeatUntilStopped = true } : null));
        else
            await DirectRunPreparation.EditRecordingOptionsAsync(prefs, lease,
                options => Task.FromResult(apply ? options with { CountdownSeconds = 8, OverrideDelay = 321 } : null));
        Assert.AreEqual(apply ? 1 : 0, preferenceStore.Saves);
        Assert.AreEqual(before, (macro.Name, macro.ChangeVersion, macro.SaveState, macro.SavedAt, macro.IsDirty));
        CollectionAssert.AreEqual(bytes, macro.SnapshotBytes());
        Assert.AreEqual(0, store.Saves);
        Assert.AreEqual(0, engine.Starts);
        Assert.IsFalse(vm.IsRecording);
        Assert.AreEqual(playback && apply ? 2d : 1d, prefs.PlaybackFor(macro.RecordingId).Speed);
        Assert.AreEqual(!playback && apply ? 8 : 3, prefs.Recording.CountdownSeconds);
    }

    [TestMethod]
    [DataRow(false, "load")]
    [DataRow(true, "load")]
    [DataRow(false, "dialog")]
    [DataRow(true, "dialog")]
    [DataRow(false, "save")]
    [DataRow(true, "save")]
    public async Task StopDuringOptionsLoadDialogOrSaveRejectsStaleContinuation(bool playback, string stage)
    {
        var blocked = new PreparationBlock();
        var store = new MemoryRunPreferenceStore
        {
            BeforeLoad = stage == "load" ? blocked.WaitAsync : null,
            BeforeSave = stage == "save" ? blocked.WaitAsync : null
        };
        var prefs = new RunPreferences(store);
        using var lifetime = new ShellRunLifetime();
        var lease = lifetime.Begin();
        var dialogs = 0;
        async Task DialogAsync()
        {
            dialogs++;
            if (stage == "dialog") await blocked.WaitAsync();
        }
        var task = playback
            ? DirectRunPreparation.EditPlaybackOptionsAsync(prefs, lease, Guid.NewGuid(), async options => { await DialogAsync(); return options with { Speed = 2 }; })
            : DirectRunPreparation.EditRecordingOptionsAsync(prefs, lease, async options => { await DialogAsync(); return options with { CountdownSeconds = 6 }; });
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        lifetime.Cancel();
        blocked.Release.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => task);
        Assert.AreEqual(stage == "load" ? 0 : 1, dialogs);
        Assert.AreEqual(stage == "save" ? 1 : 0, store.Saves, "An atomic write already committed can finish, but cannot execute a run.");
    }

    [TestMethod]
    public void ControlsRouteDirectCommandsSeparatelyFromOptionsAndExposeSafety()
    {
        var root = SourceDirectory();
        var xaml = XDocument.Load(Path.Combine(root, "MainWindow.xaml"));
        var names = (XNamespace)"http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Control(string name) => xaml.Descendants().Single(element => (string?)element.Attribute(names + "Name") == name);
        foreach (var (control, handler) in new[] { ("RecordButton", "Record_Click"), ("PlayButton", "Play_Click"),
            ("RecordingOptionsButton", "RecordingOptions_Click"), ("PlaybackOptionsButton", "PlaybackOptions_Click") })
            Assert.AreEqual(handler, (string?)Control(control).Attribute("Click"));
        StringAssert.Contains((string)Control("PlayButton").Attribute("AutomationProperties.Name")!, "real input");
        StringAssert.Contains((string)Control("PreviewButton").Attribute("AutomationProperties.Name")!, "original timing");
        StringAssert.Contains((string)Control("PreviewButton").Attribute("AutomationProperties.Name")!, "no input");
        Assert.AreEqual("Loading playback options…", (string?)Control("PlaybackSettingsSummary").Attribute("Text"));
        Assert.AreEqual("Grid", Control("TransportInfo").Name.LocalName);
        Assert.AreEqual("*", (string?)Control("TransportInfo").Elements().First().Elements().First().Attribute("Width"));
        Assert.AreEqual("1", (string?)Control("PlaybackSettings").Attribute("Grid.Column"));
        Assert.AreEqual("CharacterEllipsis", (string?)Control("StatusText").Attribute("TextTrimming"));
        Assert.AreEqual("Wrap", (string?)Control("PlaybackSettingsSummary").Attribute("TextWrapping"));
        var shell = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"));
        StringAssert.Contains(shell, "PlaybackSettingsSummary.Visibility = preview ? Visibility.Collapsed : Visibility.Visible");
        StringAssert.Contains(shell, "PlaybackSafetyNote.Text = preview ? \"Preview: original timing · no input\"");
        StringAssert.Contains(shell, "async () => await OperationAsync(() => StartRecordingAsync(false, true))");
        StringAssert.Contains(shell, "async () => await OperationAsync(PreparePlaybackAsync)");
        StringAssert.Contains(shell, "PlayButton.IsEnabled = !_busy && ViewModel.CanPlay && macro?.Events.Count > 0 && _preferences.IsLoaded");
        StringAssert.Contains(shell, "if (!_preferences.IsLoaded) { SetMessage(\"Playback options are loading.");
        StringAssert.Contains(shell, "New empty recording");
        StringAssert.Contains(shell, "Record into this recording…");
        var play = shell[shell.IndexOf("private async Task PreparePlaybackAsync()")..shell.IndexOf("private void ShowController")];
        Assert.IsTrue(play.IndexOf("_runLifetime.Begin()") < play.IndexOf("await "));
        Assert.IsFalse(play.Contains("Dialogs."));
        var record = shell[shell.IndexOf("private async Task StartRecordingAsync(")..shell.IndexOf("private async void Play_Click")];
        Assert.IsTrue(record.IndexOf("_runLifetime.Begin()") < record.IndexOf("await "));
        StringAssert.Contains(shell, "DesktopThemeMonitor");
        Assert.IsFalse(shell.Contains("HighContrastChanged"));
        Assert.IsFalse(shell.Contains("Activate();"));
    }

    [TestMethod]
    public void LoadingAndInfiniteSummariesAreUnambiguous()
    {
        var loop = new PlaybackOptions { RepeatUntilStopped = true };
        Assert.AreEqual("Loading playback options…", RunSettingsPresentation.PlaybackSummary(false, loop));
        Assert.AreEqual("1× · Once · 3s delay", RunSettingsPresentation.PlaybackSummary(true, new()));
        Assert.AreEqual("1× · Until stopped · 3s delay", RunSettingsPresentation.PlaybackSummary(true, loop));
        var countdown = RunControllerPresentation.ForPlayback(new(PlaybackPhase.Countdown, 0, 0,
            TimeSpan.FromSeconds(3), TimeSpan.Zero, RepeatUntilStopped: true), false);
        StringAssert.Contains(countdown.Detail, "Until stopped");
        Assert.IsTrue(countdown.CanStop);
    }

    [TestMethod]
    public void PreparationLeaseRejectsOverlappingRequests()
    {
        using var lifetime = new ShellRunLifetime();
        var first = lifetime.Begin();
        Assert.ThrowsExactly<InvalidOperationException>(() => lifetime.Begin());
        lifetime.Cancel();
        Assert.ThrowsExactly<InvalidOperationException>(() => lifetime.Begin(), "Await the cancelled preparation before accepting another request.");
        lifetime.Complete(first);
        var next = lifetime.Begin();
        Assert.IsTrue(lifetime.Owns(next));
        Assert.IsFalse(lifetime.Complete(first));
    }

    private static RunPreferences Preferences(RecordingOptions? recording = null)
        => new(new MemoryRunPreferenceStore { Data = new(recording ?? new(), new Dictionary<Guid, PlaybackOptions>()) });
    private static MainWindowViewModel ViewModel(RunTestLibrary store, FakePlaybackEngine? playback = null)
        => new(new FakeRecordEngine(), playback ?? new(), store);
    private static KeyboardEvent Input(ulong delay = 1000) => new(VirtualKey.A, false) { TimeSinceLastEvent = delay };
    private static Task<bool?> UnexpectedDialog(string name) => throw new AssertFailedException("Direct Record must not show a dialog.");
    private static string SourceDirectory([CallerFilePath] string source = "") => Path.Combine(Path.GetDirectoryName(source)!, "..", "MacroRecorderGUI");
}

internal sealed class PreparationBlock
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WaitAsync() { Started.TrySetResult(); return Release.Task; }
}

internal sealed class MemoryRunPreferenceStore : IRunPreferenceStore
{
    public RunPreferenceData Data { get; set; } = new(new(), new Dictionary<Guid, PlaybackOptions>());
    public Func<Task>? BeforeLoad { get; init; }
    public Func<Task>? BeforeSave { get; init; }
    public int Saves { get; private set; }
    public async Task<RunPreferenceData> LoadAsync(CancellationToken cancellationToken)
    {
        if (BeforeLoad is not null) await BeforeLoad();
        return Data;
    }
    public async Task SaveAsync(RunPreferenceData data, CancellationToken cancellationToken)
    {
        Saves++;
        if (BeforeSave is not null) await BeforeSave();
        Data = data;
    }
}

internal sealed class RunTestLibrary : IRecordingLibraryStore
{
    public Dictionary<Guid, StoredRecording> Records { get; } = [];
    public Func<Task>? BeforeList { get; init; }
    public Func<Task>? BeforeSave { get; set; }
    public int Lists { get; private set; }
    public int Saves { get; private set; }
    public async Task<RecordingLibraryRead> ListAsync(CancellationToken cancellationToken = default)
    {
        Lists++;
        if (BeforeList is not null) await BeforeList();
        return new(Records.Values.Select(record => record.Metadata).ToArray(), []);
    }
    public Task<StoredRecording> LoadAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Records[id]);
    public async Task SaveAsync(StoredRecording recording, CancellationToken cancellationToken = default)
    {
        Saves++;
        if (BeforeSave is not null) await BeforeSave();
        Records[recording.Metadata.Id] = recording;
    }
}
