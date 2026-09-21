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

    private static MainWindowViewModel Create(string directory) =>
        new(new FakeRecordEngine(), new FakePlaybackEngine(), new RecordingLibraryStore(directory));
}
