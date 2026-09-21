using System.Security.Cryptography;
using System.Text.Json.Nodes;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class LibraryOriginIntegrationTests
{
    [TestMethod]
    public async Task BulkTrashRestoreAndExportPreserveOriginsRecoveryBytesAndLocalPreferences()
    {
        var temporary = Directory.CreateTempSubdirectory("macro-library-origin-");
        try
        {
            var directory = Path.Combine(temporary.FullName, "library");
            var preferencePath = Path.Combine(temporary.FullName, "preferences.json");
            var source = Path.Combine(temporary.FullName, "legacy.macro");
            var export = Path.Combine(temporary.FullName, "versioned.macro");
            var legacy = SerializeEvents.SerializeEventsToByteArray([
                new MouseEvent(-400, 100, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 123 },
                new MouseEvent(7, -2, MouseActionTypeFlags.Move) { RelativePosition = true, TimeSinceLastEvent = 500 }
            ]).Concat(new byte[] { 0xa0, 6, 123 }).ToArray();
            await File.WriteAllBytesAsync(source, legacy);
            var preferences = new RunPreferences(new RunPreferenceStore(preferencePath));
            var options = new PlaybackOptions { PointerOrigin = PlaybackPointerOrigin.CurrentPointer, RepeatCount = 3, Speed = 2 };
            Guid firstId, secondId;
            byte[] versioned;
            using (var vm = Create(directory))
            {
                var first = await vm.ImportRecordingAsync(source);
                firstId = first.RecordingId;
                first.AdoptFirstPositionAsOrigin();
                first.AddCaptureOrigin(new PointerPosition(-600, 200));
                first.Events.Add(new MouseEvent(3, 4, MouseActionTypeFlags.Move) { RelativePosition = true, TimeSinceLastEvent = 800 });
                versioned = first.SnapshotBytes();
                await vm.SaveRecordingAsync(first);
                await vm.ExportRecordingAsync(first, export);
                await preferences.SavePlaybackAsync(firstId, options, default);
                var second = await vm.CreateDraftAsync("Second selection");
                secondId = second.RecordingId;
                var deleted = await vm.DeleteRecordingsAsync([firstId, secondId]);
                Assert.AreEqual(0, deleted.Failed.Count);
                CollectionAssert.AreEquivalent(new[] { firstId, secondId }, deleted.Succeeded.ToArray());
                Assert.AreEqual(0, vm.Library.Count);
                await vm.FlushLibraryAsync();
            }
            using var reopened = Create(directory);
            await reopened.InitializeLibraryAsync();
            await reopened.RefreshTrashAsync();
            Assert.AreEqual(0, reopened.Library.Count);
            Assert.AreEqual(2, reopened.Trash.Count);
            var restored = await reopened.RestoreRecordingsAsync([firstId, secondId]);
            Assert.AreEqual(0, restored.Failed.Count);
            var recording = await reopened.OpenRecordingAsync(firstId);
            CollectionAssert.AreEqual(versioned, recording.SnapshotBytes());
            Assert.AreEqual(2, recording.PointerOrigins.Count);
            Assert.AreEqual(123ul, recording.PointerOrigins[0].DelayMicroseconds);
            Assert.AreEqual(1, recording.PointerOrigins[1].EventIndex);
            Assert.AreEqual(new PointerPosition(-600, 200), recording.PointerOrigins[1].Position);
            Assert.IsTrue(recording.CanRecoverBeforeOriginAdoption);
            var reloadedPreferences = new RunPreferences(new RunPreferenceStore(preferencePath));
            await reloadedPreferences.InitializeAsync();
            Assert.AreEqual(options, reloadedPreferences.PlaybackFor(firstId));
            CollectionAssert.AreEqual(legacy, await File.ReadAllBytesAsync(source));
            CollectionAssert.AreEqual(versioned, await File.ReadAllBytesAsync(export));
            var imported = await reopened.ImportRecordingAsync(export);
            Assert.AreNotEqual(firstId, imported.RecordingId);
            Assert.AreEqual(new PlaybackOptions(), reloadedPreferences.PlaybackFor(imported.RecordingId));
            CollectionAssert.AreEqual(versioned, imported.SnapshotBytes());
            imported.RecoverBeforeOriginAdoption();
            CollectionAssert.AreEqual(legacy, imported.SnapshotBytes());
            Assert.IsTrue(imported.Editor.Undo());
            CollectionAssert.AreEqual(versioned, imported.SnapshotBytes());
        }
        finally { temporary.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task MalformedAdoptedOriginRecoversThroughBackupAndTrashRestoresBothExactCopies()
    {
        var temporary = Directory.CreateTempSubdirectory("macro-library-origin-");
        try
        {
            var directory = Path.Combine(temporary.FullName, "library");
            var store = new RecordingLibraryStore(directory);
            var playback = new FakePlaybackEngine();
            using var macro = new MacroViewModel("Adopted origin", playback);
            macro.AddEvent(new MouseEvent(-400, 100, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 123 });
            macro.AddEvent(new MouseEvent(7, -2, MouseActionTypeFlags.Move) { RelativePosition = true, TimeSinceLastEvent = 500 });
            macro.AdoptFirstPositionAsOrigin();
            var healthy = macro.SnapshotBytes();
            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var metadata = RecordingLibraryStore.Describe(id, macro.Name, false, now, now, healthy);
            var record = new StoredRecording(metadata, healthy);
            await store.SaveAsync(record); await store.SaveAsync(record);
            var path = Path.Combine(directory, $"{id:N}.json");
            var backupBytes = await File.ReadAllBytesAsync(path + ".bak");
            var document = RecordingDocument.Read(healthy);
            var malformed = (document with { Origins = [document.Origins[0] with { AdoptedEvent = "not base64!" }] }).Write();
            var primary = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            primary["MacroBytes"] = Convert.ToBase64String(malformed);
            primary["Sha256"] = Convert.ToHexString(SHA256.HashData(malformed));
            await File.WriteAllTextAsync(path, primary.ToJsonString());
            var primaryBytes = await File.ReadAllBytesAsync(path);

            var recovered = await store.LoadAsync(id);
            Assert.IsTrue(recovered.Recovered);
            CollectionAssert.AreEqual(healthy, recovered.MacroBytes);
            var deleted = await store.DeleteAsync(id);
            Assert.AreEqual(metadata, deleted.Metadata); Assert.IsEmpty(deleted.Warnings);
            Assert.IsFalse(File.Exists(path)); Assert.IsFalse(File.Exists(path + ".bak"));
            store = new RecordingLibraryStore(directory);
            Assert.IsEmpty((await store.ListAsync()).Items);
            Assert.AreEqual(metadata, (await store.ListTrashAsync()).Items.Single().Metadata);
            await store.RestoreAsync(id);
            CollectionAssert.AreEqual(primaryBytes, await File.ReadAllBytesAsync(path));
            CollectionAssert.AreEqual(backupBytes, await File.ReadAllBytesAsync(path + ".bak"));
            recovered = await store.LoadAsync(id);
            Assert.IsTrue(recovered.Recovered);
            CollectionAssert.AreEqual(healthy, recovered.MacroBytes);
            var error = Assert.Throws<InvalidDataException>(() => RecordingDocument.Read(malformed));
            Assert.IsInstanceOfType<FormatException>(error.InnerException);
            StringAssert.Contains(error.Message, "Base64");
            Assert.AreEqual(0, playback.Starts); Assert.AreEqual(0, playback.Aborts);
        }
        finally { temporary.Delete(recursive: true); }
    }

    private static MainWindowViewModel Create(string directory) =>
        new(new FakeRecordEngine(), new FakePlaybackEngine(), new RecordingLibraryStore(directory));
}
