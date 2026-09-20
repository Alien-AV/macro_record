using System.Text.Json;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class RunPreferencesTests
{
    private DirectoryInfo _directory = null!;
    private string PreferencePath => Path.Combine(_directory.FullName, "run-preferences.json");
    private RunPreferences Preferences() => new(new RunPreferenceStore(PreferencePath));
    [TestInitialize] public void Initialize() => _directory = Directory.CreateTempSubdirectory("macro-run-preferences-");
    [TestCleanup] public void Cleanup() => _directory.Delete(recursive: true);

    [TestMethod]
    public async Task SharedRecordingAndIsolatedPlaybackSettingsSurviveRestartAndRecordingSwitches()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var prefs = Preferences();
        var recording = new RecordingOptions { CountdownSeconds = 8, OverrideDelay = 12345 };
        var finite = new PlaybackOptions { Speed = 1.5, RepeatCount = 9, Countdown = TimeSpan.FromSeconds(6) };
        var infinite = new PlaybackOptions { Speed = 0.25, RepeatCount = 4, Countdown = TimeSpan.Zero, RepeatUntilStopped = true };
        await prefs.SaveRecordingAsync(recording, default);
        await prefs.SavePlaybackAsync(first, finite, default);
        await prefs.SavePlaybackAsync(second, infinite, default);
        var reloaded = Preferences();
        await reloaded.InitializeAsync();
        Assert.AreEqual(recording, reloaded.Recording);
        Assert.AreEqual(finite, reloaded.PlaybackFor(first));
        Assert.AreEqual(infinite, reloaded.PlaybackFor(second));
        Assert.AreEqual(finite, reloaded.PlaybackFor(first));
        Assert.AreEqual(new PlaybackOptions(), reloaded.PlaybackFor(Guid.NewGuid()));
        Assert.IsNull(reloaded.Warning);
        using var file = JsonDocument.Parse(await File.ReadAllTextAsync(PreferencePath));
        Assert.AreEqual(1, file.RootElement.GetProperty("Schema").GetInt32());
        Assert.AreEqual(6d, file.RootElement.GetProperty("Playback").GetProperty(first.ToString()).GetProperty("CountdownSeconds").GetDouble());
        Assert.IsFalse(file.RootElement.TryGetProperty("MacroBytes", out _));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingFileOrDirectoryUsesDefaultsWithoutCreatingAnything(bool missingDirectory)
    {
        var path = missingDirectory ? Path.Combine(_directory.FullName, "missing", "prefs.json") : PreferencePath;
        var prefs = new RunPreferences(new RunPreferenceStore(path));
        Assert.IsFalse(prefs.IsLoaded);
        await prefs.InitializeAsync();
        Assert.IsTrue(prefs.IsLoaded);
        Assert.AreEqual(new RecordingOptions(), prefs.Recording);
        Assert.AreEqual(new PlaybackOptions(), prefs.PlaybackFor(Guid.NewGuid()));
        Assert.IsNull(prefs.Warning);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public async Task UnreadableExistingFileReportsWarningInsteadOfPretendingPreferencesAreMissing()
    {
        await File.WriteAllTextAsync(PreferencePath, """{"Schema":1}""");
        using var held = new FileStream(PreferencePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var prefs = Preferences();
        await prefs.InitializeAsync();
        Assert.IsNotNull(prefs.Warning);
        Assert.AreEqual(new PlaybackOptions(), prefs.PlaybackFor(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task AccessDeniedReadIsVisibleAndCannotSilentlyOverwritePreferences()
    {
        var prefs = new RunPreferences(new RunPreferenceStore(_directory.FullName));
        await prefs.InitializeAsync();
        Assert.IsNotNull(prefs.Warning);
        await Assert.ThrowsAsync<Exception>(() => prefs.SaveRecordingAsync(new() { CountdownSeconds = 9 }, default));
        Assert.AreEqual(new RecordingOptions(), prefs.Recording);
        Assert.IsNotNull(prefs.Warning);
    }

    [TestMethod]
    [DataRow("""{"RepeatCount":0}""")]
    [DataRow("""{"RepeatCount":-2}""")]
    [DataRow("""{"RepeatCount":1001}""")]
    [DataRow("""{"Speed":0}""")]
    [DataRow("""{"Speed":11}""")]
    [DataRow("""{"Speed":"NaN"}""")]
    [DataRow("""{"Speed":1e400}""")]
    [DataRow("""{"CountdownSeconds":-1}""")]
    [DataRow("""{"CountdownSeconds":61}""")]
    [DataRow("""{"CountdownSeconds":1e300}""")]
    [DataRow("""{"RepeatUntilStopped":"true"}""")]
    [DataRow("""{"RepeatUntilStopped":1}""")]
    [DataRow("""{"RepeatUntilStopped":true}""")]
    [DataRow("""{"Speed":1,"RepeatCount":0,"CountdownSeconds":3,"RepeatUntilStopped":true}""")]
    [DataRow("""{"Speed":1,"RepeatCount":1,"CountdownSeconds":-1,"RepeatUntilStopped":true}""")]
    [DataRow("""{"Speed":1,"RepeatCount":1,"CountdownSeconds":3,"RepeatUntilStopped":false,"RepeatUntilStopped":true}""")]
    [DataRow("null")]
    [DataRow("[]")]
    public async Task MalformedEntryDefaultsSafelyWithoutLosingOtherRecordingSettings(string entry)
    {
        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();
        await File.WriteAllTextAsync(PreferencePath, $$$$"""
            {"Schema":1,"Playback":{"{{{{bad}}}}":{{{{entry}}}},"{{{{good}}}}":{"Speed":2,"RepeatCount":5,"CountdownSeconds":4,"RepeatUntilStopped":false}}}
            """);
        var prefs = Preferences();
        await prefs.InitializeAsync();
        Assert.AreEqual(new PlaybackOptions(), prefs.PlaybackFor(bad));
        Assert.IsFalse(prefs.PlaybackFor(bad).RepeatUntilStopped);
        Assert.AreEqual(2d, prefs.PlaybackFor(good).Speed);
        Assert.AreEqual(5, prefs.PlaybackFor(good).RepeatCount);
        Assert.IsNotNull(prefs.Warning);
        await prefs.SavePlaybackAsync(bad, new(), default);
        Assert.IsNull(prefs.Warning);
        var repaired = Preferences();
        await repaired.InitializeAsync();
        Assert.IsNull(repaired.Warning);
        Assert.AreEqual(prefs.PlaybackFor(good), repaired.PlaybackFor(good));
    }

    [TestMethod]
    [DataRow("""{"CountdownSeconds":-1}""")]
    [DataRow("""{"CountdownSeconds":31}""")]
    [DataRow("""{"CountdownSeconds":2.5}""")]
    [DataRow("""{"OverrideDelay":18446744073709551615}""")]
    [DataRow("""{"OverrideDelay":-1}""")]
    [DataRow("null")]
    public async Task MalformedRecordingSettingsRevertToCapturedTimingAndSafeCountdown(string options)
    {
        await File.WriteAllTextAsync(PreferencePath, $$$$"""{"Schema":1,"Recording":{{{{options}}}}}""");
        var prefs = Preferences();
        await prefs.InitializeAsync();
        Assert.AreEqual(new RecordingOptions(), prefs.Recording);
        Assert.IsNotNull(prefs.Warning);
    }

    [TestMethod]
    [DataRow("not-an-id")]
    [DataRow("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidIdsAreDiscardedAndCannotBlockLaterOptionSaves(string id)
    {
        await File.WriteAllTextAsync(PreferencePath, $$$$"""{"Schema":1,"Playback":{"{{{{id}}}}":{"RepeatUntilStopped":true}}}""");
        var prefs = Preferences();
        await prefs.InitializeAsync();
        Assert.IsNotNull(prefs.Warning);
        await prefs.SaveRecordingAsync(new() { CountdownSeconds = 7 }, default);
        var valid = Guid.NewGuid();
        await prefs.SavePlaybackAsync(valid, new() { Speed = 2 }, default);
        var read = await new RunPreferenceStore(PreferencePath).LoadAsync(default);
        Assert.AreEqual(1, read.Playback.Count);
        Assert.AreEqual(2d, read.Playback[valid].Speed);
        Assert.IsFalse(read.Playback.ContainsKey(Guid.Empty));
        Assert.IsNull(prefs.Warning);
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("null")]
    [DataRow("""{"Schema":1,"Playback":[]}""")]
    [DataRow("""{"Schema":1,"Schema":1}""")]
    public async Task CorruptDocumentHasVisibleDefaultsAndCanBeExplicitlyRepaired(string document)
    {
        await File.WriteAllTextAsync(PreferencePath, document);
        var prefs = Preferences();
        await prefs.InitializeAsync();
        Assert.IsNotNull(prefs.Warning);
        Assert.AreEqual(new PlaybackOptions(), prefs.PlaybackFor(Guid.NewGuid()));
        await prefs.SaveRecordingAsync(new(), default);
        Assert.IsNull(prefs.Warning);
        var reloaded = Preferences();
        await reloaded.InitializeAsync();
        Assert.IsNull(reloaded.Warning);
    }

    [TestMethod]
    public async Task DuplicateRecordingIdCannotEnableInfinitePlayback()
    {
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(PreferencePath, $$$$"""
            {"Schema":1,"Playback":{"{{{{id}}}}":{},"{{{{id}}}}":{"Speed":1,"RepeatCount":1,"CountdownSeconds":3,"RepeatUntilStopped":true}}}
            """);
        var prefs = Preferences();
        await prefs.InitializeAsync();
        Assert.AreEqual(new PlaybackOptions(), prefs.PlaybackFor(id));
        Assert.IsNotNull(prefs.Warning);
    }

    [TestMethod]
    public async Task FailedOrCancelledWritePreservesPreviouslyCommittedSettings()
    {
        var id = Guid.NewGuid();
        var prefs = Preferences();
        var committed = new PlaybackOptions { Speed = 2 };
        await prefs.SavePlaybackAsync(id, committed, default);
        var before = await File.ReadAllBytesAsync(PreferencePath);
        using (var held = new FileStream(PreferencePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<IOException>(() => prefs.SavePlaybackAsync(id, new() { RepeatUntilStopped = true }, default));
        Assert.AreEqual(committed, prefs.PlaybackFor(id));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(PreferencePath));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => prefs.SavePlaybackAsync(id, new() { Speed = 4 }, cancel.Token));
        Assert.AreEqual(committed, prefs.PlaybackFor(id));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(PreferencePath));
        Assert.IsFalse(Directory.EnumerateFiles(_directory.FullName).Any(path => path.EndsWith(".tmp")));
    }

    [TestMethod]
    public async Task PreferencesLeaveOldLibraryMetadataWireUnknownFieldsAndDirtyStateUntouched()
    {
        var libraryPath = Path.Combine(_directory.FullName, "recordings");
        var store = new RecordingLibraryStore(libraryPath);
        var bytes = SerializeEvents.SerializeEventsToByteArray([new KeyboardEvent(VirtualKey.A, false) { TimeSinceLastEvent = 123 }])
            .Concat(new byte[] { 0xa0, 0x06, 0x07 }).ToArray();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var metadata = RecordingLibraryStore.Describe(id, "User metadata", false, now, now, bytes);
        await store.SaveAsync(new(metadata, bytes));
        var libraryFile = Path.Combine(libraryPath, $"{id:N}.json");
        var originalFile = await File.ReadAllBytesAsync(libraryFile);
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), store);
        var macro = await vm.OpenRecordingAsync(id);
        var originalVersion = macro.ChangeVersion;
        var prefs = Preferences();
        await prefs.InitializeAsync();
        Assert.AreEqual(new PlaybackOptions(), prefs.PlaybackFor(id), "Old library entries need no migration.");
        await prefs.SavePlaybackAsync(id, new() { RepeatUntilStopped = true, Speed = 2 }, default);
        CollectionAssert.AreEqual(bytes, macro.SnapshotBytes());
        CollectionAssert.AreEqual(originalFile, await File.ReadAllBytesAsync(libraryFile));
        Assert.AreEqual(metadata, (await store.LoadAsync(id)).Metadata);
        Assert.AreEqual(originalVersion, macro.ChangeVersion);
        Assert.AreEqual(RecordingSaveState.Saved, macro.SaveState);
        Assert.IsFalse(macro.IsDirty);
        macro.Events[0].TimeSinceLastEvent = 456;
        var dirtyVersion = macro.ChangeVersion;
        await prefs.SaveRecordingAsync(new() { CountdownSeconds = 7 }, default);
        Assert.IsTrue(macro.IsDirty);
        Assert.AreEqual(dirtyVersion, macro.ChangeVersion);
        await vm.RenameRecordingAsync(id, "Renamed");
        var reloaded = Preferences();
        await reloaded.InitializeAsync();
        Assert.IsTrue(reloaded.PlaybackFor(macro.RecordingId).RepeatUntilStopped);
    }

    [TestMethod]
    public async Task OptionsDialogPinsRecordingIdentityEvenWhenSelectionChanges()
    {
        var store = new RunTestLibrary();
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), store);
        var original = vm.ActiveMacro!;
        var prefs = Preferences();
        using var lifetime = new ShellRunLifetime();
        var dialog = new PreparationBlock();
        var task = DirectRunPreparation.EditPlaybackOptionsAsync(prefs, lifetime.Begin(), original.RecordingId, async options =>
        {
            await dialog.WaitAsync();
            return options with { Speed = 4 };
        });
        await dialog.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var other = vm.AddNewTab();
        dialog.Release.SetResult();
        await task;
        Assert.AreEqual(4d, prefs.PlaybackFor(original.RecordingId).Speed);
        Assert.AreEqual(new PlaybackOptions(), prefs.PlaybackFor(other.RecordingId));
    }
}
