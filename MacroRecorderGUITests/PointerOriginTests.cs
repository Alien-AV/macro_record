using Google.Protobuf;
using MacroRecorderGUI.Common;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;
using Windows.System;

namespace MacroRecorderGUITests;

internal sealed class FakePointerEnvironment : IPointerEnvironment
{
    public PointerPosition Position { get; set; } = new(300, 400);
    public PointerBounds Desktop { get; set; } = new(-2000, -1000, 6000, 4000);
    public PointerBounds Primary { get; set; } = new(0, 0, 1920, 1080);
    public int Samples { get; private set; }
    public PointerPosition GetPosition() { Samples++; return Position; }
    public PointerBounds GetBounds(bool virtualDesktop) => virtualDesktop ? Desktop : Primary;
}

[TestClass]
public sealed class PointerOriginTests
{
    private static MouseEvent Move(int x, int y, bool relative = false, bool desktop = true, ulong delay = 25)
        => new(x, y, MouseActionTypeFlags.Move) { RelativePosition = relative, MappedToVirtualDesktop = desktop, TimeSinceLastEvent = delay };
    private static InputEvent[] Inputs() => [Move(7, -4, true), Move(-400, 200), new KeyboardEvent(VirtualKey.A, false)];
    private static PlaybackOptions Options(PlaybackPointerOrigin mode = PlaybackPointerOrigin.RecordedStartingPoint)
        => new() { Countdown = TimeSpan.Zero, PointerOrigin = mode };
    private static PointerOriginBoundary[] Origins() => [new(0, new(-500, 100))];

    [TestMethod]
    [DataRow(PlaybackPointerOrigin.RecordedStartingPoint, -500, 100, -400, 200, 0)]
    [DataRow(PlaybackPointerOrigin.CurrentPointer, 300, 400, 400, 500, 1)]
    public async Task OriginTranslatesOnlyAbsolutePixelsAndNeverRawCounts(PlaybackPointerOrigin mode, int sx, int sy, int ax, int ay, int samples)
    {
        var engine = new FakePlaybackEngine(); var pointer = new FakePointerEnvironment();
        var workflow = new PlaybackWorkflow(engine, Task.Delay, pointer);
        var events = Inputs(); var before = SerializeEvents.SerializeEventsToByteArray(events);
        await workflow.PlayAsync(events, Options(mode), Origins());
        var actual = engine.PlayedEvents.ToArray();
        Assert.AreEqual((sx, sy), (((MouseEvent)actual[0]).X, ((MouseEvent)actual[0]).Y));
        Assert.AreEqual((7, -4), (((MouseEvent)actual[1]).X, ((MouseEvent)actual[1]).Y));
        Assert.IsTrue(((MouseEvent)actual[1]).RelativePosition);
        Assert.AreEqual((ax, ay), (((MouseEvent)actual[2]).X, ((MouseEvent)actual[2]).Y));
        Assert.AreEqual(samples, pointer.Samples);
        CollectionAssert.AreEqual(before, SerializeEvents.SerializeEventsToByteArray(events));
        CollectionAssert.AreEqual(new ulong[] { 0, 25, 25, 0 }, actual.Select(e => e.TimeSinceLastEvent).ToArray());
    }

    [TestMethod]
    public async Task CurrentPointerIsSampledAfterCountdownAndFiniteRepeatsReuseIt()
    {
        var engine = new TrackingEngine(); var pointer = new FakePointerEnvironment();
        var countdownEntered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var workflow = new PlaybackWorkflow(engine, async (_, token) =>
        {
            countdownEntered.TrySetResult(); await release.Task.WaitAsync(token);
            await Task.Delay(30, token);
        }, pointer);
        var task = workflow.PlayAsync(Inputs(), Options(PlaybackPointerOrigin.CurrentPointer) with { Countdown = TimeSpan.FromMilliseconds(10), RepeatCount = 3 }, Origins());
        await countdownEntered.Task;
        Assert.AreEqual(0, pointer.Samples); Assert.HasCount(0, engine.Runs);
        pointer.Position = new(-800, -300); release.SetResult();
        for (var i = 0; i < 3; i++)
        {
            await engine.Started.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual((-800, -300), (((MouseEvent)engine.Runs[i][0]).X, ((MouseEvent)engine.Runs[i][0]).Y));
            pointer.Position = new(700, 900); engine.Completions[i].SetResult();
        }
        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(1, pointer.Samples);
        Assert.IsTrue(engine.Runs.All(run => SerializeEvents.SerializeEventsToByteArray(run).SequenceEqual(SerializeEvents.SerializeEventsToByteArray(engine.Runs[0]))));
    }

    [TestMethod]
    public async Task InfiniteRunContainsSameSetupAtBeginningOfNativeLoopAndCanCancel()
    {
        var engine = new TrackingEngine(); var pointer = new FakePointerEnvironment();
        var workflow = new PlaybackWorkflow(engine, Task.Delay, pointer);
        var task = workflow.PlayAsync(Inputs(), Options(PlaybackPointerOrigin.CurrentPointer) with { RepeatUntilStopped = true }, Origins());
        Assert.HasCount(1, engine.Runs); Assert.IsTrue(engine.Loop);
        Assert.AreEqual(300, ((MouseEvent)engine.Runs[0][0]).X);
        pointer.Position = new(900, 900);
        workflow.Abort();
        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        Assert.AreEqual(1, pointer.Samples); Assert.AreEqual(1, engine.Aborts);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task CancellationDuringCountdownDoesNotSamplePointerOrStartEngine(bool infinite)
    {
        var engine = new FakePlaybackEngine(); var pointer = new FakePointerEnvironment();
        var workflow = new PlaybackWorkflow(engine, (_, token) => Task.Delay(Timeout.Infinite, token), pointer);
        var task = workflow.PlayAsync(Inputs(), Options(PlaybackPointerOrigin.CurrentPointer) with { Countdown = TimeSpan.FromSeconds(1), RepeatUntilStopped = infinite }, Origins());
        workflow.Abort(); await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        Assert.AreEqual(0, pointer.Samples); Assert.AreEqual(0, engine.Starts);
    }

    [TestMethod]
    public async Task LegacyKeepsFirstMoveExactlyAndCurrentPointerDoesNotGuess()
    {
        var engine = new FakePlaybackEngine(); var pointer = new FakePointerEnvironment();
        var workflow = new PlaybackWorkflow(engine, Task.Delay, pointer);
        var events = new[] { Move(-500, 100), Move(5, 8, true) };
        var bytes = SerializeEvents.SerializeEventsToByteArray(events);
        await workflow.PlayAsync(events, Options());
        CollectionAssert.AreEqual(bytes, SerializeEvents.SerializeEventsToByteArray(engine.PlayedEvents));
        Assert.Throws<InvalidOperationException>(() => workflow.PlayAsync(events, Options(PlaybackPointerOrigin.CurrentPointer)));
        Assert.AreEqual(0, pointer.Samples); Assert.AreEqual(1, engine.Starts);
    }

    [TestMethod]
    public async Task OverflowAndFrameViolationsFailBeforeAnyEngineStart()
    {
        var engine = new FakePlaybackEngine(); var pointer = new FakePointerEnvironment();
        var workflow = new PlaybackWorkflow(engine, Task.Delay, pointer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.PlayAsync([Move(int.MaxValue, 0)], Options(PlaybackPointerOrigin.CurrentPointer), [new(0, new(0, 0))]));
        pointer.Position = new(-500, 50);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.PlayAsync([Move(10, 10, desktop: false)], Options(PlaybackPointerOrigin.CurrentPointer), [new(0, new(0, 0))]));
        Assert.Throws<InvalidOperationException>(() => workflow.PlayAsync(Inputs(), Options(), [new(0, null)]));
        Assert.Throws<InvalidOperationException>(() => workflow.PlayAsync(Inputs(), Options(), [new(0, new(0, 0, (PointerCoordinateFrame)99))]));
        Assert.AreEqual(0, engine.Starts);
    }

    [TestMethod]
    public async Task AppendSegmentsEachGetSetupAndOneSharedTranslation()
    {
        var engine = new FakePlaybackEngine(); var pointer = new FakePointerEnvironment();
        var workflow = new PlaybackWorkflow(engine, Task.Delay, pointer);
        await workflow.PlayAsync([Move(4, 5, true), Move(10, 20, true)], Options(PlaybackPointerOrigin.CurrentPointer), [new(0, new(100, 100)), new(1, new(-500, -100))]);
        var mouse = engine.PlayedEvents.Cast<MouseEvent>().ToArray();
        CollectionAssert.AreEqual(new[] { (300, 400), (4, 5), (-300, 200), (10, 20) }, mouse.Select(m => (m.X, m.Y)).ToArray());
        Assert.AreEqual(1, pointer.Samples);
    }

    [TestMethod]
    public void AdoptionIsExplicitLosslessUndoableAndRecoverableAfterReload()
    {
        var original = SerializeEvents.SerializeEventsToByteArray([Move(-400, 50, delay: 123), Move(8, 9, true)])
            .Concat(new byte[] { 0xa0, 6, 123 }).ToArray();
        using var macro = Macro(original);
        Assert.IsFalse(macro.HasOriginMetadata); Assert.HasCount(2, macro.Events);
        StringAssert.Contains(macro.OriginAdoptionDescription()!, "123 µs");
        macro.AdoptFirstPositionAsOrigin();
        Assert.HasCount(1, macro.Events); Assert.AreEqual(123ul, macro.PointerOrigins[0].DelayMicroseconds);
        Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(original, macro.SnapshotBytes());
        macro.AdoptFirstPositionAsOrigin();
        var saved = macro.SnapshotBytes();
        Assert.Throws<InvalidProtocolBufferException>(() => ProtobufInputEventList.Parser.ParseFrom(saved));
        using var restored = Macro(saved);
        restored.Events[0].TimeSinceLastEvent = 333;
        restored.RecoverBeforeOriginAdoption();
        CollectionAssert.AreEqual(original, restored.SnapshotBytes());
        Assert.IsTrue(restored.Editor.Undo());
        Assert.AreEqual(333ul, restored.Events[0].TimeSinceLastEvent);
        Assert.IsTrue(restored.CanRecoverBeforeOriginAdoption);
    }

    [TestMethod]
    public void UnknownPayloadFieldsSurviveMetadataRenameEditsAndUndo()
    {
        var bytes = SerializeEvents.SerializeEventsToByteArray([Move(10, 20), Move(3, 4, true)])
            .Concat(new byte[] { 0xa0, 6, 123 }).ToArray();
        using var macro = Macro(bytes); macro.AddCaptureOrigin(new(99, 88));
        var envelope = macro.SnapshotBytes();
        using var loaded = Macro(envelope); loaded.Name = "Renamed";
        CollectionAssert.AreEqual(envelope, loaded.SnapshotBytes());
        loaded.Editor.Execute("Change wait", () => loaded.Events[0].TimeSinceLastEvent = 45);
        var expected = ProtobufInputEventList.Parser.ParseFrom(bytes); expected.InputEvents[0].TimeSinceLastEvent = 45;
        CollectionAssert.AreEqual(expected.ToByteArray(), RecordingDocument.Read(loaded.SnapshotBytes()).Events);
        Assert.AreEqual(new PointerOriginBoundary(2, new(99, 88)), loaded.PointerOrigins.Single());
        Assert.IsTrue(loaded.Editor.Undo());
        CollectionAssert.AreEqual(envelope, loaded.SnapshotBytes());
    }

    [TestMethod]
    public void SegmentIndicesFollowEditsAndUndoWithoutTurningOriginsIntoActions()
    {
        using var macro = new MacroViewModel("Segments", new FakePlaybackEngine());
        macro.AddCaptureOrigin(new(10, 20)); macro.AddEvent(Move(1, 2, true));
        macro.AddCaptureOrigin(new(100, 200)); macro.AddEvent(Move(3, 4, true));
        macro.Editor.Execute("Insert", () => macro.Events.Insert(0, new KeyboardEvent(VirtualKey.B, false)));
        CollectionAssert.AreEqual(new[] { 0, 2 }, macro.PointerOrigins.Select(p => p.EventIndex).ToArray());
        Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(new[] { 0, 1 }, macro.PointerOrigins.Select(p => p.EventIndex).ToArray());
        macro.Editor.Refresh(); Assert.AreEqual(2, macro.Editor.Projection.ProcessedCount);
        Assert.IsTrue(macro.Editor.Projection.HasRelativeMovement);
        Assert.AreEqual(MacroRecorderGUI.Editor.CoordinateSpace.RelativeCounts, macro.Editor.Projection.Samples[0].Position!.Value.Space);
        macro.Clear(); Assert.IsEmpty(macro.PointerOrigins); Assert.IsEmpty(macro.Events);
    }

    internal static MacroViewModel Macro(byte[] bytes)
    {
        var now = DateTimeOffset.UtcNow; var macro = new MacroViewModel("Test", new FakePlaybackEngine());
        macro.Restore(new(RecordingLibraryStore.Describe(Guid.NewGuid(), "Test", false, now, now, bytes), bytes));
        return macro;
    }

    [TestMethod]
    public void LegacyPlusAppendAdoptionPreservesLaterBoundariesAndExtendedRecovery()
    {
        var original = SerializeEvents.SerializeEventsToByteArray([Move(-500, 50), Move(3, 4, true)])
            .Concat(new byte[] { 0xa0, 6, 99 }).ToArray();
        using var macro = Macro(original);
        macro.AddCaptureOrigin(new(-900, 90)); macro.AddEvent(Move(7, 8, true));
        var beforeAdoption = macro.SnapshotBytes();
        macro.AdoptFirstPositionAsOrigin();
        CollectionAssert.AreEqual(new[] { 0, 1 }, macro.PointerOrigins.Select(p => p.EventIndex).ToArray());
        using var reloaded = Macro(macro.SnapshotBytes());
        Assert.AreEqual(new PointerPosition(-900, 90), reloaded.PointerOrigins[1].Position);
        reloaded.Editor.Execute("Later edit", () => reloaded.Events[0].TimeSinceLastEvent = 777);
        reloaded.AddCaptureOrigin(new(200, 300)); reloaded.AddEvent(Move(99, 100, true));
        reloaded.RecoverBeforeOriginAdoption();
        CollectionAssert.AreEqual(beforeAdoption, reloaded.SnapshotBytes());
        Assert.IsTrue(reloaded.Editor.Undo());
        Assert.AreEqual(777ul, reloaded.Events[0].TimeSinceLastEvent);
        Assert.HasCount(3, reloaded.PointerOrigins);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)]
    public void DeletingBeforeOrAtBoundaryThenUndoRetainsLaterCaptureBoundary(int index)
    {
        using var macro = new MacroViewModel("Delete boundaries", new FakePlaybackEngine());
        macro.AddCaptureOrigin(new(10, 20)); macro.AddEvent(Move(1, 2, true));
        macro.AddCaptureOrigin(new(30, 40)); macro.AddEvent(Move(3, 4, true));
        macro.Editor.Execute("Delete", () => macro.Events.RemoveAt(index));
        CollectionAssert.AreEqual(index == 0 ? new[] { 0, 0 } : new[] { 0, 1 }, macro.PointerOrigins.Select(p => p.EventIndex).ToArray());
        macro.AddCaptureOrigin(new(50, 60)); macro.AddEvent(Move(5, 6, true));
        Assert.IsTrue(macro.Editor.Undo());
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, macro.PointerOrigins.Select(p => p.EventIndex).ToArray());
        Assert.AreEqual(50, macro.PointerOrigins[2].Position!.X);
        macro.AddCaptureOrigin(new(70, 80)); macro.AddEvent(Move(7, 8, true));
        using var loaded = Macro(macro.SnapshotBytes());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, loaded.PointerOrigins.Select(p => p.EventIndex).ToArray());
    }

    [TestMethod]
    public void ProjectionSplitsCaptureSegmentsAndGeometryNeverBridgesTheirOrigins()
    {
        using var macro = new MacroViewModel("Segments", new FakePlaybackEngine());
        macro.AddCaptureOrigin(new(10, 20)); macro.AddEvent(Move(30, 40));
        macro.AddCaptureOrigin(new(100, 200)); macro.AddEvent(Move(130, 240));
        macro.Editor.Refresh();
        Assert.AreEqual(2, macro.Editor.Projection.Actions.Count);
        Assert.AreNotEqual(macro.Editor.Projection.Samples[0].Segment, macro.Editor.Projection.Samples[1].Segment);
        Assert.IsTrue(macro.Editor.Projection.Samples[1].StartsSegment);
        StringAssert.Contains(macro.Editor.GeometryBlockReason(macro.Editor.Projection.Actions[0])!, "separate capture segments");
        var bytes = macro.SnapshotBytes();
        Assert.Throws<ArgumentException>(() => macro.Editor.SetDestination(macro.Editor.Projection.Actions[0], 70, 90));
        Assert.Throws<ArgumentException>(macro.Editor.ConvertAnchoredEstimate);
        CollectionAssert.AreEqual(bytes, macro.SnapshotBytes());
    }

    [TestMethod]
    public async Task DelayedStartMetadataRoutesToOriginalTargetAndClearRolloverDropsStaleOrigin()
    {
        var transport = new FakeRecordingTransport(); using var vm = new DeferredOriginsViewModel(transport);
        var first = vm.ActiveMacro!;
        vm.StartRecording(fromHotkey: true); var old = transport.Starts.Last();
        transport.Begin(old, RecordingStartKeys.Q, origin: new(-300, 30));
        transport.Push(old, FakeRecordEngine.MakeKeyboardEvent(0x51, true, 10));
        transport.Push(old, FakeRecordEngine.MakeKeyboardEvent(0x41, false, 20));
        vm.StopRecording(); var other = vm.AddNewTab();
        vm.StartRecording(); var second = transport.Starts.Last();
        transport.End(old); transport.Begin(second, origin: new(800, 90));
        transport.Push(second, FakeRecordEngine.MakeKeyboardEvent(0x42, false, 7));
        vm.Deliver();
        Assert.AreEqual(new PointerPosition(-300, 30), first.PointerOrigins.Single().Position);
        Assert.AreEqual(30ul, first.Events.Single().TimeSinceLastEvent);
        Assert.AreEqual(new PointerPosition(800, 90), other.PointerOrigins.Single().Position);
        other.Clear(); var rollover = transport.Starts.Last();
        transport.Push(second, FakeRecordEngine.MakeKeyboardEvent(0x42, true, 9)); transport.End(second);
        transport.Begin(rollover, origin: new(-800, -100));
        transport.Push(rollover, FakeRecordEngine.MakeKeyboardEvent(0x43, false, 11));
        vm.Deliver();
        Assert.AreEqual(new PointerOriginBoundary(0, new(-800, -100)), other.PointerOrigins.Single());
        Assert.AreEqual(11ul, other.Events.Single().TimeSinceLastEvent);
        vm.StopRecording(); transport.End(rollover); vm.Deliver(); await vm.StopRecordingAsync();
    }

    [TestMethod]
    public void DuplicateStartAndEmptyCaptureDoNotInventEventsOrReplaceEarlierMetadata()
    {
        var transport = new FakeRecordingTransport(); using var capture = new RecordingCapture(transport);
        var session = new RecordingSession(); var starts = new List<PointerPosition?>(); var inputs = new List<ProtobufInputEvent>();
        capture.Started += (_, origin) => starts.Add(origin); capture.Input += (_, input) => inputs.Add(input);
        capture.Start(session); transport.Begin(session, origin: new(10, 20)); transport.Begin(session, origin: new(99, 99));
        capture.Stop(); transport.End(session);
        CollectionAssert.AreEqual(new[] { new PointerPosition(10, 20) }, starts); Assert.IsEmpty(inputs);
    }

    [TestMethod]
    public async Task EnvelopeImportExportRenameThumbnailAndLegacyExportAreLosslessInTemporaryLibrary()
    {
        var directory = Directory.CreateTempSubdirectory("macro-origin-tests-");
        try
        {
            var store = new RecordingLibraryStore(Path.Combine(directory.FullName, "library"));
            using var vm = new MainWindowViewModel(new FakeRecordEngine(), new FakePlaybackEngine(), store, new FakePointerEnvironment());
            using var source = Macro(SerializeEvents.SerializeEventsToByteArray([Move(-400, 30, delay: 77), Move(4, 5, true)])
                .Concat(new byte[] { 0xa0, 6, 88 }).ToArray());
            source.AdoptFirstPositionAsOrigin(); source.AddCaptureOrigin(new(100, 200)); source.AddEvent(Move(120, 230));
            var bytes = source.SnapshotBytes(); var file = Path.Combine(directory.FullName, "new.macro");
            await FileOperations.WriteMacroBytesAsync(file, bytes);
            var imported = await vm.ImportRecordingAsync(file); var id = imported.RecordingId;
            await vm.RenameRecordingAsync(id, "Renamed origins");
            var exported = Path.Combine(directory.FullName, "roundtrip.macro"); await vm.ExportRecordingAsync(imported, exported);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(exported));
            var preview = await vm.LoadRecordingPreviewAsync(id);
            CollectionAssert.AreEqual(source.PointerOrigins.ToArray(), preview.Origins.ToArray());
            Assert.HasCount(2, preview.Events);
            var legacyPath = Path.Combine(directory.FullName, "legacy.macro"); await vm.ExportLegacyRecordingAsync(imported, legacyPath);
            var legacy = ProtobufInputEventList.Parser.ParseFrom(await File.ReadAllBytesAsync(legacyPath));
            Assert.HasCount(4, legacy.InputEvents);
            Assert.AreEqual(-400, legacy.InputEvents[0].MouseEvent.X); Assert.AreEqual(77ul, legacy.InputEvents[0].TimeSinceLastEvent);
            Assert.AreEqual(100, legacy.InputEvents[2].MouseEvent.X);
            legacy.InputEvents.RemoveAt(2); legacy.InputEvents.RemoveAt(0);
            CollectionAssert.AreEqual(RecordingDocument.Read(bytes).Events, legacy.ToByteArray());
            CollectionAssert.AreEqual(bytes, imported.SnapshotBytes());
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public void FormatRejectsFutureVersionsInvalidBoundariesAndDeepRecovery()
    {
        var wire = SerializeEvents.SerializeEventsToByteArray([Move(1, 2)]);
        var doc = new RecordingDocument { IsExtended = true, Events = wire, Origins = [new(0, new(1, 2))] };
        Assert.Throws<InvalidDataException>(() => RecordingDocument.Read((doc with { Version = 3 }).Write()));
        Assert.Throws<InvalidDataException>(() => RecordingDocument.Read((doc with { Origins = [new(2, new(1, 2))] }).Write()));
        var bytes = doc.Write();
        for (var i = 0; i < 6; i++) bytes = (doc with { BeforeOriginAdoption = bytes }).Write();
        Assert.Throws<InvalidDataException>(() => RecordingDocument.Read(bytes));
    }

    [TestMethod]
    public async Task AdoptionRetainsUnknownFieldsInTheRemovedEventForPlaybackAndLegacyExport()
    {
        var first = Move(-100, 20).OriginalProtobufInputEvent;
        first.MouseEvent = ProtobufInputEvent.Types.MouseEventType.Parser.ParseFrom(first.MouseEvent.ToByteArray().Concat(new byte[] { 0xa0, 6, 11 }).ToArray());
        first = ProtobufInputEvent.Parser.ParseFrom(first.ToByteArray().Concat(new byte[] { 0xa0, 6, 22 }).ToArray());
        var wire = new ProtobufInputEventList(); wire.InputEvents.Add(first);
        using var macro = Macro(wire.ToByteArray()); macro.AdoptFirstPositionAsOrigin();
        Assert.IsEmpty(macro.Events);
        var document = RecordingDocument.Read(macro.SnapshotBytes());
        CollectionAssert.AreEqual(wire.ToByteArray(), document.ExportLegacy());
        var engine = new FakePlaybackEngine();
        await new PlaybackWorkflow(engine, Task.Delay, new FakePointerEnvironment()).PlayAsync(macro.Events, Options(), macro.PointerOrigins);
        CollectionAssert.AreEqual(wire.ToByteArray(), SerializeEvents.SerializeEventsToByteArray(engine.PlayedEvents));
        macro.Editor.Refresh(); Assert.AreEqual(new System.Numerics.BigInteger(25), macro.Editor.Projection.TotalTime);
        macro.AddEvent(Move(7, 8, true)); macro.Editor.Refresh();
        Assert.AreEqual(new System.Numerics.BigInteger(50), macro.Editor.Projection.TotalTime);
    }

    [TestMethod]
    public void ConsecutiveIdenticalBoundaryDelaysRemainDistinctInPreview()
    {
        var doc = new RecordingDocument { IsExtended = true, Events = [],
            Origins = [new(0, new(100, 200), 50), new(0, new(100, 200), 50)] };
        using var macro = Macro(doc.Write()); macro.Editor.Refresh();
        Assert.AreEqual(new System.Numerics.BigInteger(100), macro.Editor.Projection.TotalTime);
        macro.AddEvent(Move(1, 2, true)); macro.Editor.Refresh();
        Assert.AreEqual(new System.Numerics.BigInteger(125), macro.Editor.Projection.TotalTime);
    }

    private sealed class DeferredOriginsViewModel(FakeRecordingTransport transport)
        : MainWindowViewModel(new RecordEngine(transport), new FakePlaybackEngine(), new RunTestLibrary(), new FakePointerEnvironment())
    {
        private readonly Queue<Action> _queue = [];
        protected override void InvokeDispatcher(Action action) => _queue.Enqueue(action);
        public void Deliver() { while (_queue.TryDequeue(out var action)) action(); }
    }

    private sealed class TrackingEngine : IPlaybackEngine
    {
        public List<InputEvent[]> Runs { get; } = [];
        public List<TaskCompletionSource> Completions { get; } = [];
        public SemaphoreSlim Started { get; } = new(0);
        public bool Loop { get; private set; }
        public int Aborts { get; private set; }
        public Task PlaybackEventsAsync(IEnumerable<InputEvent> events, bool loop = false)
        {
            Runs.Add(events.ToArray()); Loop = loop;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Completions.Add(completion); Started.Release(); return completion.Task;
        }
        public void PlaybackEventAbort() { Aborts++; Completions.Last().TrySetCanceled(); }
        public void SetLoopPlayback(bool loop) => Loop = loop;
        public void Dispose() => Started.Dispose();
    }
}
