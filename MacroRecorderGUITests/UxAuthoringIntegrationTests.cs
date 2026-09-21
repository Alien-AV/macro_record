using Google.Protobuf;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Editor;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using MacroRecorderGUI.Views;
using ProtobufGenerated;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class UxAuthoringIntegrationTests
{
    private static readonly byte[] Unknown = [0xa0, 6, 123];

    private static WaitConditionEvent Wait()
    {
        var condition = new WaitCondition
        {
            SemanticsVersion = 1, TimeoutUs = 30_000_000, StableForUs = 200_000, PollIntervalUs = 100_000,
            Pixel = new PixelCondition { Coordinates = PixelCoordinates.DesktopPhysical, X = -400, Y = 100, Rgb = 0xabcdef }
        };
        condition = WaitCondition.Parser.ParseFrom(condition.ToByteArray().Concat(Unknown).ToArray());
        return new(condition) { TimeSinceLastEvent = 21 };
    }

    private static RecordedAction ActionFor(MacroViewModel macro, InputEvent input)
    {
        macro.Editor.Refresh();
        return macro.Editor.Projection.ActionAt(macro.Events.IndexOf(input))!;
    }

    [TestMethod]
    public async Task AuthoredActionsRoundTripWithWaitsOriginsRecoveryAndBulkTrashWithoutChangingCapturedBytes()
    {
        var temporary = Directory.CreateTempSubdirectory("macro-ux-authoring-");
        try
        {
            var directory = Path.Combine(temporary.FullName, "library");
            var source = Path.Combine(temporary.FullName, "legacy.macro");
            var export = Path.Combine(temporary.FullName, "authored.macro");
            var legacy = SerializeEvents.SerializeEventsToByteArray([
                new MouseEvent(-500, 100, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 123, MappedToVirtualDesktop = true },
                new MouseEvent(2, 3, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 100, RelativePosition = true }
            ]).Concat(Unknown).ToArray();
            await File.WriteAllBytesAsync(source, legacy);
            Guid id, otherId;
            byte[] authored;
            using (var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), new RecordingLibraryStore(directory)))
            {
                var macro = await vm.ImportRecordingAsync(source);
                id = macro.RecordingId;
                macro.AdoptFirstPositionAsOrigin();
                var wait = Wait(); macro.AddEvent(wait);
                macro.AddCaptureOrigin(new(-200, 200));
                var tail = new MouseEvent(-190, 210, MouseActionTypeFlags.Move) { TimeSinceLastEvent = 100, MappedToVirtualDesktop = true };
                macro.AddEvent(tail);
                var captured = macro.Events.ToArray();
                var capturedBytes = captured.Select(input => input.OriginalProtobufInputEvent.ToByteArray()).ToArray();
                var baseline = macro.SnapshotBytes();
                byte[][] Author()
                {
                    macro.Editor.InsertClick(ActionFor(macro, captured[0]), new ClickDefinition(MouseButton.Left, 25, 30));
                    var click = macro.SnapshotBytes();
                    macro.Editor.InsertShortcut(ActionFor(macro, wait), new ShortcutDefinition((uint)VirtualKey.A, ShortcutModifiers.Control | ShortcutModifiers.Shift, 50, 60));
                    var shortcut = macro.SnapshotBytes();
                    macro.Editor.InsertPointerMovement(ActionFor(macro, tail), new PointerMovementDefinition(-180, 220, CoordinateSpace.AbsoluteDesktop, 75));
                    return [click, shortcut, macro.SnapshotBytes()];
                }
                var checkpoints = Author();
                for (var i = 0; i < captured.Length; i++)
                    CollectionAssert.AreEqual(capturedBytes[i], captured[i].OriginalProtobufInputEvent.ToByteArray());
                CollectionAssert.AreEqual(captured, macro.Events.Where(captured.Contains).ToArray());
                Assert.AreEqual(2, macro.PointerOrigins.Count);
                Assert.IsTrue(macro.CanRecoverBeforeOriginAdoption);
                WaitValidation.ValidateSchedule(macro.Events);
                Assert.IsTrue(macro.Editor.Undo()); CollectionAssert.AreEqual(checkpoints[1], macro.SnapshotBytes());
                Assert.IsTrue(macro.Editor.Undo()); CollectionAssert.AreEqual(checkpoints[0], macro.SnapshotBytes());
                Assert.IsTrue(macro.Editor.Undo()); CollectionAssert.AreEqual(baseline, macro.SnapshotBytes());
                authored = Author()[2];
                CollectionAssert.AreEqual(checkpoints[2], authored);
                await vm.SaveRecordingAsync(macro);
                await vm.ExportRecordingAsync(macro, export);
                otherId = (await vm.CreateDraftAsync("Other selected recording")).RecordingId;
                var deleted = await vm.DeleteRecordingsAsync([id, otherId]);
                Assert.IsEmpty(deleted.Failed);
                CollectionAssert.AreEquivalent(new[] { id, otherId }, deleted.Succeeded.ToArray());
            }
            using var reopened = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), new RecordingLibraryStore(directory));
            await reopened.InitializeLibraryAsync();
            Assert.IsEmpty(reopened.Library);
            var restored = await reopened.RestoreRecordingsAsync([id, otherId]);
            Assert.IsEmpty(restored.Failed);
            CollectionAssert.AreEquivalent(new[] { id, otherId }, restored.Succeeded.ToArray());
            Assert.AreEqual(2, reopened.Library.Count);
            var result = await reopened.OpenRecordingAsync(id);
            CollectionAssert.AreEqual(authored, result.SnapshotBytes());
            CollectionAssert.AreEqual(authored, await File.ReadAllBytesAsync(export));
            CollectionAssert.AreEqual(legacy, await File.ReadAllBytesAsync(source));
            CollectionAssert.AreEqual(Wait().Condition.ToByteArray(), result.Events.OfType<WaitConditionEvent>().Single().Condition.ToByteArray());
            result.RecoverBeforeOriginAdoption();
            CollectionAssert.AreEqual(legacy, result.SnapshotBytes());
            Assert.IsTrue(result.Editor.Undo());
            CollectionAssert.AreEqual(authored, result.SnapshotBytes());
        }
        finally { temporary.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task AuthoredTargetUsesItsRememberedRunOptionsEvenWhenAnotherDocumentIsSelected()
    {
        var temporary = Directory.CreateTempSubdirectory("macro-ux-run-");
        try
        {
            var engine = new FakePlaybackEngine();
            var pointer = new FakePointerEnvironment { Position = new(300, 400) };
            using var vm = new MainWindowViewModel(new FakeRecordEngine(), engine,
                new RecordingLibraryStore(Path.Combine(temporary.FullName, "library")), pointer);
            var target = await vm.CreateDraftAsync("Authored target");
            target.AddCaptureOrigin(new(-500, 100));
            target.Editor.InsertClick(null, new ClickDefinition(MouseButton.Right, 200, 400));
            target.AddEvent(Wait());
            var bytes = target.SnapshotBytes();
            var other = await vm.CreateDraftAsync("Other document");
            var otherBytes = other.SnapshotBytes();
            var preferences = new RunPreferences(new RunPreferenceStore(Path.Combine(temporary.FullName, "preferences.json")));
            var options = new PlaybackOptions { Countdown = TimeSpan.Zero, RepeatCount = 3, Speed = 2, PointerOrigin = PlaybackPointerOrigin.CurrentPointer };
            await preferences.SavePlaybackAsync(target.RecordingId, options, default);
            using var lifetime = new ShellRunLifetime();
            var lease = lifetime.Begin();
            var prepared = await DirectRunPreparation.PlayAsync(vm, preferences, lease, target);
            Assert.AreSame(other, vm.ActiveMacro);
            Assert.AreSame(target, prepared.Macro);
            Assert.AreEqual(options, prepared.Options);
            await vm.PlayMacroAsync(prepared.Macro, prepared.Options);
            Assert.AreEqual(3, engine.Starts);
            Assert.AreEqual(1, pointer.Samples);
            Assert.IsFalse(engine.Loop);
            Assert.AreSame(other, vm.ActiveMacro);
            CollectionAssert.AreEqual(bytes, target.SnapshotBytes());
            CollectionAssert.AreEqual(otherBytes, other.SnapshotBytes());
            var played = engine.PlayedEvents.ToArray();
            Assert.AreEqual(4, played.Length);
            CollectionAssert.AreEqual(new ulong[] { 0, 100, 200, 11 }, played.Select(input => input.TimeSinceLastEvent).ToArray());
            CollectionAssert.AreEqual(Wait().Condition.ToByteArray(), played.OfType<WaitConditionEvent>().Single().Condition.ToByteArray());
            Assert.AreEqual((300, 400), (((MouseEvent)played[0]).X, ((MouseEvent)played[0]).Y));
        }
        finally { temporary.Delete(recursive: true); }
    }
}
