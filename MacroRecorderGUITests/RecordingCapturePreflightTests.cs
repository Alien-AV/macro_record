using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class RecordingCapturePreflightTests
{
    private static readonly RecordingCaptureGesture Gesture = new(2, 0x58);
    private static readonly RecordingCaptureGesture OtherGesture = new(4, 0x59);
    private static uint Now => unchecked((uint)Environment.TickCount);
    private static WaitCondition Condition() => new()
    {
        SemanticsVersion = 1, TimeoutUs = 1_000_000, PollIntervalUs = 10_000,
        Pixel = new() { Coordinates = PixelCoordinates.DesktopPhysical, Rgb = 123 }
    };

    [TestMethod]
    public void PreflightIsReadOnlyAndSharesSubmissionGuards()
    {
        var transport = new FakeRecordingTransport();
        using var capture = new RecordingCapture(transport);
        var session = new RecordingSession(100, [Gesture]);
        var callbacks = 0;
        capture.Input += (_, _) => callbacks++;
        capture.Started += (_, _) => callbacks++;
        capture.Ended += (_, _) => callbacks++;
        capture.WaitRejected += (_, _) => callbacks++;
        Assert.IsFalse(capture.AcceptsRecordingCapture(Gesture, 100));
        Assert.AreEqual(CapturedWaitSubmission.Inactive, capture.CapturedWait(Condition(), Gesture, 100));
        capture.Start(session);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.IsTrue(capture.AcceptsRecordingCapture(Gesture, 100));
            Assert.IsFalse(capture.AcceptsRecordingCapture(Gesture, 99));
            Assert.IsFalse(capture.AcceptsRecordingCapture(OtherGesture, 100));
        }
        Assert.AreEqual(CapturedWaitSubmission.Stale, capture.CapturedWait(Condition(), Gesture, 99));
        Assert.AreEqual(CapturedWaitSubmission.Unregistered, capture.CapturedWait(Condition(), OtherGesture, 100));
        Assert.IsEmpty(transport.CapturedWaits);
        Assert.IsEmpty(transport.Stops);
        Assert.HasCount(1, transport.Starts);
        Assert.AreEqual(0, callbacks);
        Assert.IsFalse(session.Completion.IsCompleted);
        Assert.IsTrue(capture.IsRecording);
    }

    [TestMethod]
    [DataRow(0xfffffffeu, 1u, true)]
    [DataRow(1u, 0xfffffffeu, false)]
    [DataRow(uint.MaxValue, 0u, true)]
    [DataRow(0u, uint.MaxValue, false)]
    [DataRow(0u, 0x7fffffffu, true)]
    [DataRow(0u, 0x80000000u, false)]
    public void MessageTimestampWrapUsesTheSameSerialOrderingAsSubmission(uint requestedAt, uint messageTime, bool accepted)
    {
        var transport = new FakeRecordingTransport();
        using var engine = new RecordEngine(transport);
        engine.StartRecord(new RecordingSession(requestedAt, [Gesture]));
        Assert.AreEqual(accepted, engine.AcceptsRecordingCapture(Gesture, messageTime));
        Assert.IsEmpty(transport.CapturedWaits);
        Assert.AreEqual(accepted ? CapturedWaitSubmission.Queued : CapturedWaitSubmission.Stale,
            engine.CapturedWait(Condition(), Gesture, messageTime));
        Assert.AreEqual(accepted ? 1 : 0, transport.CapturedWaits.Count);
    }

    [TestMethod]
    public void ReplacingRequestedSessionRevalidatesAfterPreflightBeforeOldBoundaryDrains()
    {
        var transport = new FakeRecordingTransport();
        using var engine = new RecordEngine(transport);
        var first = new RecordingSession(100, [Gesture]);
        engine.StartRecord(first);
        Assert.IsTrue(engine.AcceptsRecordingCapture(Gesture, 110));
        engine.StopRecord();
        var second = new RecordingSession(120, [OtherGesture]);
        engine.StartRecord(second);
        Assert.IsFalse(engine.AcceptsRecordingCapture(Gesture, 110));
        Assert.AreEqual(CapturedWaitSubmission.Stale, engine.CapturedWait(Condition(), Gesture, 110));
        Assert.IsFalse(engine.AcceptsRecordingCapture(Gesture, 120));
        Assert.AreEqual(CapturedWaitSubmission.Unregistered, engine.CapturedWait(Condition(), Gesture, 120));
        Assert.IsTrue(engine.AcceptsRecordingCapture(OtherGesture, 120));
        transport.End(first);
        Assert.IsTrue(engine.AcceptsRecordingCapture(OtherGesture, 120));
        Assert.IsEmpty(transport.CapturedWaits);
        Assert.IsFalse(second.Completion.IsCompleted);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StopOrDisposalAfterPreflightRejectsLaterSubmission(bool dispose)
    {
        var transport = new FakeRecordingTransport();
        using var engine = new RecordEngine(transport);
        engine.StartRecord(new RecordingSession(100, [Gesture]));
        Assert.IsTrue(engine.AcceptsRecordingCapture(Gesture, 100));
        if (dispose) engine.Dispose(); else engine.StopRecord();
        Assert.IsFalse(engine.AcceptsRecordingCapture(Gesture, 100));
        Assert.AreEqual(CapturedWaitSubmission.Inactive, engine.CapturedWait(Condition(), Gesture, 100));
        Assert.IsEmpty(transport.CapturedWaits);
    }

    [TestMethod]
    public void RejectedStopRetainsEligibilityButFailedDisposalDoesNot()
    {
        var transport = new FakeRecordingTransport { StopError = new InvalidOperationException("fake queue failure") };
        using var engine = new RecordEngine(transport);
        engine.StartRecord(new RecordingSession(100, [Gesture]));
        Assert.ThrowsExactly<InvalidOperationException>(() => engine.StopRecord());
        Assert.IsTrue(engine.AcceptsRecordingCapture(Gesture, 100));
        transport.OnDispose = () => throw new InvalidOperationException("fake join failure");
        Assert.ThrowsExactly<InvalidOperationException>(() => engine.Dispose());
        Assert.IsFalse(engine.AcceptsRecordingCapture(Gesture, 100));
        Assert.AreEqual(CapturedWaitSubmission.Inactive, engine.CapturedWait(Condition(), Gesture, 100));
        Assert.IsEmpty(transport.CapturedWaits);
        transport.OnDispose = null;
    }

    [TestMethod]
    public void ViewModelPreflightIsSilentUsesSessionSnapshotAndDoesNotDependOnSelectedTab()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredViewModel(transport);
        vm.RegisteredRecordingCaptures = [Gesture];
        var stale = unchecked(Now - 1);
        Assert.IsFalse(vm.AcceptsRecordingCapture(Gesture, Now));
        Assert.IsTrue(vm.StartRecording());
        var target = vm.ActiveMacro!;
        var revision = target.ContentRevision;
        vm.AddNewTab();
        vm.RegisteredRecordingCaptures = [OtherGesture];
        var statuses = 0;
        vm.StatusMessageRequested += (_, _) => statuses++;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.IsTrue(vm.AcceptsRecordingCapture(Gesture, Now));
            Assert.IsFalse(vm.AcceptsRecordingCapture(Gesture, stale));
            Assert.IsFalse(vm.AcceptsRecordingCapture(OtherGesture, Now));
        }
        Assert.AreEqual(0, statuses);
        Assert.AreEqual(revision, target.ContentRevision);
        Assert.IsEmpty(target.Events);
        Assert.IsEmpty(vm.ActiveMacro!.Events);
        Assert.IsEmpty(transport.CapturedWaits);
        Assert.IsEmpty(transport.Stops);
        Assert.AreEqual(0, vm.RecordedEventCount);
        Assert.IsTrue(vm.IsRecording);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RemovedOrReplacedTargetBlocksPreflightAndSubmissionEvenWhenEngineIsStillActive(bool replace)
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredViewModel(transport);
        vm.RegisteredRecordingCaptures = [Gesture];
        Assert.IsTrue(vm.StartRecording());
        var target = vm.ActiveMacro!;
        Assert.IsTrue(vm.AcceptsRecordingCapture(Gesture, Now));
        if (replace)
        {
            transport.StopError = new InvalidOperationException("fake queue failure");
            target.Clear();
        }
        else vm.MacroTabs.Remove(target);
        var statuses = 0;
        vm.StatusMessageRequested += (_, _) => statuses++;
        Assert.IsTrue(vm.RecordEngine.AcceptsRecordingCapture(Gesture, Now));
        Assert.IsFalse(vm.AcceptsRecordingCapture(Gesture, Now));
        Assert.AreEqual(CapturedWaitSubmission.Inactive, vm.AddCapturedWait(Condition(), Gesture, Now));
        Assert.AreEqual(CapturedWaitSubmission.Inactive, vm.CancelCapturedWait(Gesture, Now));
        Assert.AreEqual(0, statuses);
        Assert.IsEmpty(transport.CapturedWaits);
        if (!replace) vm.MacroTabs.Add(target);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeStopOrFailureRejectsBeforeQueuedUiCompletion(bool failed)
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredViewModel(transport);
        vm.RegisteredRecordingCaptures = [Gesture];
        Assert.IsTrue(vm.StartRecording());
        var session = transport.Starts.Single();
        Assert.IsTrue(vm.AcceptsRecordingCapture(Gesture, Now));
        if (failed) transport.Fail(session); else transport.End(session);
        Assert.IsTrue(vm.IsRecording, "The UI completion is still backlogged.");
        Assert.IsFalse(vm.AcceptsRecordingCapture(Gesture, Now));
        Assert.AreEqual(CapturedWaitSubmission.Inactive, vm.AddCapturedWait(Condition(), Gesture, Now));
        Assert.IsEmpty(transport.CapturedWaits);
        vm.Deliver();
        Assert.IsFalse(vm.IsRecording);
        vm.Dispose();
        Assert.IsFalse(vm.AcceptsRecordingCapture(Gesture, Now));
    }

    private sealed class DeferredViewModel(FakeRecordingTransport transport)
        : MainWindowViewModel(new RecordEngine(transport), new FakePlaybackEngine(), new RunTestLibrary(), new FakePointerEnvironment())
    {
        private readonly Queue<Action> _pending = [];
        protected override void InvokeDispatcher(Action action) => _pending.Enqueue(action);
        public void Deliver() { while (_pending.TryDequeue(out var action)) action(); }
    }
}
