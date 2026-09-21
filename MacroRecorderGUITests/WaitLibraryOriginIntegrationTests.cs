using Google.Protobuf;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitLibraryOriginIntegrationTests
{
    private static readonly byte[] Unknown = [0xa0, 0x06, 0x7b];

    private static WaitConditionEvent PixelWait()
    {
        var condition = new WaitCondition
        {
            SemanticsVersion = 1, TimeoutUs = 30_000_000, StableForUs = 200_000, PollIntervalUs = 100_000,
            Pixel = new PixelCondition { Coordinates = PixelCoordinates.DesktopPhysical, X = -400, Y = 150, Rgb = 0x123456 }
        };
        condition = WaitCondition.Parser.ParseFrom(condition.ToByteArray().Concat(Unknown).ToArray());
        var input = new ProtobufInputEvent { WaitCondition = condition, TimeSinceLastEvent = 25 };
        return new WaitConditionEvent(ProtobufInputEvent.Parser.ParseFrom(input.ToByteArray().Concat(Unknown).ToArray()));
    }

    private static MouseEvent Move(int x, int y, bool relative = false) =>
        new(x, y, MouseActionTypeFlags.Move) { RelativePosition = relative, MappedToVirtualDesktop = true, TimeSinceLastEvent = 100 };

    [TestMethod]
    public async Task WaitsSurviveOriginAdoptionBulkTrashRestoreExportAndRecoveryUndo()
    {
        var temporary = Directory.CreateTempSubdirectory("macro-wait-origin-library-");
        try
        {
            var directory = Path.Combine(temporary.FullName, "library");
            var source = Path.Combine(temporary.FullName, "source.macro");
            var export = Path.Combine(temporary.FullName, "export.macro");
            var legacy = SerializeEvents.SerializeEventsToByteArray([
                Move(-500, 100), Move(2, 3, true), new KeyboardEvent(VirtualKey.A, false), new KeyboardEvent(VirtualKey.A, true)
            ]).Concat(Unknown).ToArray();
            await File.WriteAllBytesAsync(source, legacy);
            Guid id, otherId;
            byte[] saved;
            using (var vm = Create(directory))
            {
                var macro = await vm.ImportRecordingAsync(source);
                id = macro.RecordingId;
                macro.AdoptFirstPositionAsOrigin();
                macro.AddCaptureOrigin(new(-200, 200));
                macro.AddEvent(Move(-190, 210));
                var beforeInsertion = macro.SnapshotBytes();
                macro.Editor.Execute("Insert wait", () => macro.Events.Insert(1, PixelWait()));
                CollectionAssert.AreEqual(new[] { 0, 4 }, macro.PointerOrigins.Select(origin => origin.EventIndex).ToArray());
                Assert.IsTrue(macro.Editor.Undo());
                CollectionAssert.AreEqual(beforeInsertion, macro.SnapshotBytes());
                macro.Editor.Execute("Insert wait", () => macro.Events.Insert(1, PixelWait()));
                saved = macro.SnapshotBytes();
                await vm.SaveRecordingAsync(macro);
                await vm.ExportRecordingAsync(macro, export);
                var other = await vm.CreateDraftAsync("Other selected recording");
                otherId = other.RecordingId;
                var deleted = await vm.DeleteRecordingsAsync([id, otherId]);
                Assert.IsEmpty(deleted.Failed);
                Assert.AreEqual(2, deleted.Succeeded.Count);
            }
            using var reopened = Create(directory);
            await reopened.InitializeLibraryAsync();
            Assert.IsEmpty(reopened.Library);
            var restored = await reopened.RestoreRecordingsAsync([id, otherId]);
            Assert.IsEmpty(restored.Failed);
            CollectionAssert.AreEquivalent(new[] { id, otherId }, restored.Succeeded.ToArray());
            Assert.AreEqual(2, reopened.Library.Count);
            Assert.AreEqual(otherId, (await reopened.OpenRecordingAsync(otherId)).RecordingId);
            var recording = await reopened.OpenRecordingAsync(id);
            CollectionAssert.AreEqual(saved, recording.SnapshotBytes());
            CollectionAssert.AreEqual(saved, await File.ReadAllBytesAsync(export));
            CollectionAssert.AreEqual(legacy, await File.ReadAllBytesAsync(source));
            var wait = recording.Events.OfType<WaitConditionEvent>().Single();
            CollectionAssert.AreEqual(PixelWait().OriginalProtobufInputEvent.ToByteArray(), wait.OriginalProtobufInputEvent.ToByteArray());
            Assert.IsTrue(recording.CanRecoverBeforeOriginAdoption);
            Assert.AreEqual(100ul, recording.PointerOrigins[0].DelayMicroseconds);
            CollectionAssert.AreEqual(new[] { 0, 4 }, recording.PointerOrigins.Select(origin => origin.EventIndex).ToArray());

            var legacyExport = Path.Combine(temporary.FullName, "existing-legacy.macro");
            await File.WriteAllBytesAsync(legacyExport, legacy);
            try
            {
                await reopened.ExportLegacyRecordingAsync(recording, legacyExport);
                Assert.Fail("Legacy export must reject conditional waits, never strip or misrepresent them.");
            }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ArgumentException or NotSupportedException) { }
            CollectionAssert.AreEqual(legacy, await File.ReadAllBytesAsync(legacyExport));

            recording.RecoverBeforeOriginAdoption();
            CollectionAssert.AreEqual(legacy, recording.SnapshotBytes());
            Assert.IsTrue(recording.Editor.Undo());
            CollectionAssert.AreEqual(saved, recording.SnapshotBytes());
        }
        finally { temporary.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task PointerRelocationAndSpeedChangeInputsButNeverPixelTargetOrConditionTiming()
    {
        using var macro = new MacroViewModel("Wait and pointer", new FakePlaybackEngine());
        macro.AddCaptureOrigin(new(-500, 100));
        macro.AddEvent(Move(2, 3, true));
        macro.AddEvent(PixelWait());
        macro.AddCaptureOrigin(new(-200, 200));
        macro.AddEvent(Move(-190, 210));
        macro.RestoreOriginState(macro.OriginState with
        {
            Origins = [macro.PointerOrigins[0] with { DelayMicroseconds = 120 },
                macro.PointerOrigins[1] with { DelayMicroseconds = 80 }]
        });
        var before = macro.SnapshotBytes();
        var engine = new FakePlaybackEngine();
        var pointer = new FakePointerEnvironment { Position = new(300, 400) };
        var workflow = new PlaybackWorkflow(engine, Task.Delay, pointer);
        await workflow.PlayAsync(macro.Events, new PlaybackOptions
        {
            Countdown = TimeSpan.Zero, PointerOrigin = PlaybackPointerOrigin.CurrentPointer, Speed = 2
        }, macro.PointerOrigins);
        var actual = engine.PlayedEvents.ToArray();
        Assert.AreEqual(5, actual.Length);
        Assert.IsInstanceOfType<WaitConditionEvent>(actual[2], "Origin setup shifts the wait's native event index.");
        var wait = (WaitConditionEvent)actual[2];
        CollectionAssert.AreEqual(PixelWait().Condition.ToByteArray(), wait.Condition.ToByteArray());
        Assert.AreEqual(13ul, wait.TimeSinceLastEvent);
        CollectionAssert.AreEqual(new ulong[] { 60, 50, 13, 40, 50 }, actual.Select(input => input.TimeSinceLastEvent).ToArray());
        Assert.AreEqual((300, 400), (((MouseEvent)actual[0]).X, ((MouseEvent)actual[0]).Y));
        Assert.AreEqual((2, 3), (((MouseEvent)actual[1]).X, ((MouseEvent)actual[1]).Y));
        Assert.AreEqual((600, 500), (((MouseEvent)actual[3]).X, ((MouseEvent)actual[3]).Y));
        Assert.AreEqual((610, 510), (((MouseEvent)actual[4]).X, ((MouseEvent)actual[4]).Y));
        Assert.AreEqual(1, pointer.Samples);
        CollectionAssert.AreEqual(before, macro.SnapshotBytes());
    }

    [TestMethod]
    public async Task UnsupportedWaitAfterOriginAndInputFailsBeforePointerSamplingOrEngineStart()
    {
        var wait = PixelWait();
        wait.OriginalProtobufInputEvent.WaitCondition.SemanticsVersion = 999;
        var engine = new FakePlaybackEngine();
        var pointer = new FakePointerEnvironment();
        var workflow = new PlaybackWorkflow(engine, Task.Delay, pointer);
        try
        {
            await workflow.PlayAsync([Move(10, 20), wait], new PlaybackOptions
            {
                Countdown = TimeSpan.Zero, PointerOrigin = PlaybackPointerOrigin.CurrentPointer
            }, [new(0, new(0, 0))]);
            Assert.Fail("Unsupported wait semantics must reject the entire schedule before any input.");
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ArgumentException or NotSupportedException) { }
        Assert.AreEqual(0, engine.Starts);
        Assert.AreEqual(0, pointer.Samples);
    }

    [TestMethod]
    public async Task NativeWaitRequestsCorrelateAfterRealOriginPreparationAcrossConsecutiveWaitsAndRepeats()
    {
        using var macro = new MacroViewModel("Consecutive waits", new FakePlaybackEngine());
        macro.AddCaptureOrigin(new(-500, 100));
        macro.AddEvent(Move(2, 3, true));
        var first = PixelWait();
        var condition = first.Condition; condition.StableForUs = 0; first.SetCondition(condition);
        macro.AddEvent(first);
        condition.Pixel.Rgb = 0xabcdef;
        macro.AddEvent(new WaitConditionEvent(condition));
        macro.AddCaptureOrigin(new(-200, 200));
        macro.AddEvent(Move(-190, 210));
        var before = macro.SnapshotBytes();
        var pointer = new FakePointerEnvironment { Position = new(300, 400) };
        var native = new PreparedWaitNative(() => pointer.Position = new(900, 900));
        var observer = new RecordingWaitObserver();
        using var engine = new PlaybackEngine(native, observer);
        var workflow = new PlaybackWorkflow(engine, Task.Delay, pointer);
        await workflow.PlayAsync(macro.Events, new PlaybackOptions
        {
            Countdown = TimeSpan.Zero, PointerOrigin = PlaybackPointerOrigin.CurrentPointer, RepeatCount = 2
        }, macro.PointerOrigins).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, native.Runs.Count);
        Assert.AreEqual(4, native.Resolved.Count);
        CollectionAssert.AreEqual(new ulong[] { 2, 3, 2, 3 }, native.Resolved.Select(request => request.Index).ToArray());
        CollectionAssert.AreEqual(new uint[] { 0x123456, 0xabcdef, 0x123456, 0xabcdef }, observer.Colors.ToArray());
        Assert.AreEqual(1, pointer.Samples);
        CollectionAssert.AreEqual(native.Runs[0], native.Runs[1]);
        var prepared = ProtobufInputEventList.Parser.ParseFrom(native.Runs[0]);
        Assert.AreEqual(6, prepared.InputEvents.Count);
        Assert.AreEqual(300, prepared.InputEvents[0].MouseEvent.X);
        Assert.AreEqual(600, prepared.InputEvents[4].MouseEvent.X);
        CollectionAssert.AreEqual(before, macro.SnapshotBytes());
    }

    private sealed class RecordingWaitObserver : IWaitObserver
    {
        public System.Collections.Concurrent.ConcurrentQueue<uint> Colors { get; } = new();
        public ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Colors.Enqueue(condition.Pixel.Rgb);
            return ValueTask.FromResult(new WaitObservation(ObservationState.Match, "Fake pixel matched", "desktop", condition.Pixel.Rgb));
        }
    }

    private sealed class PreparedWaitNative(Action firstStart) : IPlaybackWaitNativeApi
    {
        public List<byte[]> Runs { get; } = [];
        public List<(ulong Session, ulong Index)> Resolved { get; } = [];
        private ulong _session, _occurrence;
        private Queue<ulong> _indices = new();
        private PlaybackResult _result;
        public PlaybackResult Start(byte[] events, bool loop, out ulong sessionId)
        {
            Assert.IsFalse(loop);
            Runs.Add(events.ToArray()); sessionId = ++_session;
            var wire = ProtobufInputEventList.Parser.ParseFrom(events);
            _indices = new(wire.InputEvents.Select((input, index) => (input, index))
                .Where(item => item.input.WaitCondition is not null).Select(item => (ulong)item.index));
            Assert.AreEqual(2, _indices.Count);
            _result = PlaybackResult.Running; ++_occurrence;
            if (_session == 1) firstStart();
            return _result;
        }
        public PlaybackResult Poll(ulong sessionId) { Assert.AreEqual(_session, sessionId); return _result; }
        public PlaybackResult Abort(ulong sessionId) => _result = PlaybackResult.Cancelled;
        public PlaybackResult SetLoop(ulong sessionId, bool loop) => _result;
        public PlaybackResult WaitRequest(ulong sessionId, out NativeWaitRequest request)
        {
            Assert.AreEqual(_session, sessionId);
            request = new() { Occurrence = _occurrence, EventIndex = _indices.Peek(), RemainingUs = 1_000_000 };
            return _result;
        }
        public PlaybackResult ResolveWait(ulong sessionId, ulong occurrence, bool satisfied)
        {
            Assert.AreEqual(_session, sessionId); Assert.AreEqual(_occurrence, occurrence); Assert.IsTrue(satisfied);
            Resolved.Add((sessionId, _indices.Dequeue())); ++_occurrence;
            if (_indices.Count == 0) _result = PlaybackResult.Finished;
            return PlaybackResult.Running;
        }
    }

    private static MainWindowViewModel Create(string directory) =>
        new(new FakeRecordEngine(), new FakePlaybackEngine(), new RecordingLibraryStore(directory));
}
