using Google.Protobuf;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public class RecordingCapturedWaitTests
{
    private static readonly RecordingCaptureGesture Gesture = new(2, 0x58);
    private static WaitCondition Condition(uint rgb = 123) => new()
    {
        SemanticsVersion = 1, TimeoutUs = 1_000_000, PollIntervalUs = 10_000,
        Pixel = new() { Coordinates = PixelCoordinates.DesktopPhysical, Rgb = rgb, X = -15, Y = 27 }
    };
    private static ProtobufInputEvent Wait(ulong delay = 20) => new() { TimeSinceLastEvent = delay, WaitCondition = Condition() };

    [TestMethod]
    public void SessionAndContinuationKeepAnImmutableGestureSnapshot()
    {
        var gestures = new[] { Gesture };
        var first = new RecordingSession(captureGestures: gestures);
        gestures[0] = new(4, 0x59);
        var continued = first.Continue();
        CollectionAssert.AreEqual(new[] { Gesture }, first.CaptureGestures.ToArray());
        CollectionAssert.AreEqual(new[] { Gesture }, continued.CaptureGestures.ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<RecordingCaptureGesture>)first.CaptureGestures)[0] = gestures[0]);
    }

    [TestMethod]
    public void SamplesAreClonedAndInactiveStaleAndUnregisteredCommandsNeverReachNative()
    {
        var transport = new FakeRecordingTransport();
        using var capture = new RecordingCapture(transport);
        var session = new RecordingSession(captureGestures: [Gesture]);
        Assert.AreEqual(CapturedWaitSubmission.Inactive, capture.CapturedWait(Condition(), Gesture, session.RequestedAt));
        capture.Start(session);
        Assert.AreEqual(CapturedWaitSubmission.Stale, capture.CapturedWait(Condition(), Gesture, unchecked(session.RequestedAt - 1)));
        Assert.AreEqual(CapturedWaitSubmission.Unregistered, capture.CapturedWait(Condition(), new(4, 0x59), session.RequestedAt));
        Assert.IsEmpty(transport.CapturedWaits);
        var sample = Condition();
        Assert.AreEqual(CapturedWaitSubmission.Queued, capture.CapturedWait(sample, Gesture, session.RequestedAt));
        sample.Pixel.Rgb = 456;
        Assert.AreEqual(123u, transport.CapturedWaits.Single().Condition!.Pixel.Rgb);
        Assert.AreEqual(CapturedWaitSubmission.Queued, capture.CapturedWait(null, Gesture, session.RequestedAt));
        Assert.IsNull(transport.CapturedWaits.Last().Condition);
    }

    [TestMethod]
    public void InvalidSampleExplicitlyCancelsItsReservation()
    {
        var transport = new FakeRecordingTransport();
        using var capture = new RecordingCapture(transport);
        var session = new RecordingSession(captureGestures: [Gesture]);
        capture.Start(session);
        var invalid = Condition(); invalid.SemanticsVersion = 0;
        Assert.ThrowsExactly<ArgumentException>(() => capture.CapturedWait(invalid, Gesture, session.RequestedAt));
        Assert.HasCount(1, transport.CapturedWaits);
        Assert.IsNull(transport.CapturedWaits[0].Condition);
        Assert.IsTrue(capture.IsRecording);
    }

    [TestMethod]
    public void EnqueueFailureAndRejectedStopRetainSessionAndAllowRetry()
    {
        var transport = new FakeRecordingTransport { CaptureResult = false, StopError = new InvalidOperationException("queue full") };
        using var capture = new RecordingCapture(transport);
        var session = new RecordingSession(captureGestures: [Gesture]);
        capture.Start(session);
        Assert.AreEqual(CapturedWaitSubmission.EnqueueFailed, capture.CapturedWait(Condition(), Gesture, session.RequestedAt));
        Assert.ThrowsExactly<InvalidOperationException>(() => capture.Stop());
        Assert.IsTrue(capture.IsRecording);
        transport.CaptureResult = true;
        Assert.AreEqual(CapturedWaitSubmission.Queued, capture.CapturedWait(null, Gesture, session.RequestedAt));
        Assert.IsFalse(session.Completion.IsCompleted);
        transport.StopError = null;
        Assert.IsTrue(capture.Stop());
        Assert.AreEqual(CapturedWaitSubmission.Inactive, capture.CapturedWait(Condition(), Gesture, session.RequestedAt));
        transport.End(session);
        Assert.IsTrue(session.Completion.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task BackloggedNativeWaitUsesOriginalDestinationAndOriginUntilUiStopDrains()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredViewModel(transport);
        vm.RegisteredRecordingCaptures = [Gesture];
        var target = vm.ActiveMacro!;
        Assert.IsTrue(vm.StartRecording());
        var session = transport.Starts.Single();
        transport.Begin(session, origin: new(-42, 13));
        Assert.AreEqual(CapturedWaitSubmission.Queued, vm.AddCapturedWait(Condition(), Gesture, unchecked((uint)Environment.TickCount)));
        Assert.IsEmpty(target.Events, "Submission must not append from the UI.");
        transport.Push(session, Wait());
        transport.Push(session, FakeRecordEngine.MakeMouseEvent(3, 4, 1, true, 30));
        vm.AddNewTab();
        var stop = vm.StopRecordingAsync();
        transport.End(session);
        Assert.IsFalse(stop.IsCompleted);
        Assert.IsTrue(vm.IsFinalizingRecording);
        Assert.IsEmpty(target.Events);
        vm.Deliver();
        await stop.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.HasCount(2, target.Events);
        Assert.IsInstanceOfType<WaitConditionEvent>(target.Events[0]);
        CollectionAssert.AreEqual(new ulong[] { 20, 30 }, target.Events.Select(input => input.TimeSinceLastEvent).ToArray());
        Assert.IsEmpty(vm.ActiveMacro!.Events);
        Assert.AreEqual(new PointerPosition(-42, 13), target.PointerOrigins.Single().Position);
        WaitValidation.ValidateSchedule(target.Events);
    }

    [TestMethod]
    public void ReplacementRolloverRetainsGesturesAndDiscardsOldDestinationCallbacks()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredViewModel(transport);
        vm.RegisteredRecordingCaptures = [Gesture];
        Assert.IsTrue(vm.StartRecording());
        var first = transport.Starts.Single();
        transport.Begin(first, origin: new(-42, 13));
        vm.RegisteredRecordingCaptures = [new(4, 0x59)];
        vm.ActiveMacro!.Clear();
        var second = transport.Starts.Last();
        Assert.AreNotEqual(first, second);
        transport.Push(first, Wait(90)); transport.End(first);
        transport.Begin(second, origin: new(15, 30)); transport.Push(second, Wait(10));
        Assert.AreEqual(CapturedWaitSubmission.Queued, vm.CancelCapturedWait(Gesture, unchecked((uint)Environment.TickCount)));
        Assert.AreEqual(second, transport.CapturedWaits.Single().Session);
        CollectionAssert.AreEqual(new[] { Gesture }, transport.CaptureGestures.Last().ToArray());
        vm.Deliver();
        Assert.HasCount(1, vm.ActiveMacro.Events);
        Assert.AreEqual(10ul, vm.ActiveMacro.Events[0].TimeSinceLastEvent);
        Assert.AreEqual(new PointerPosition(15, 30), vm.ActiveMacro.PointerOrigins.Single().Position);
    }

    [TestMethod]
    [DataRow(RecordingBoundary.WaitHeldInput, "release other keys")]
    [DataRow(RecordingBoundary.WaitTimedOut, "timed out")]
    [DataRow(RecordingBoundary.WaitCancelled, "cancelled")]
    [DataRow(RecordingBoundary.WaitStale, "stale")]
    public void RejectionIsReportedWithoutEndingRecording(RecordingBoundary reason, string message)
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredViewModel(transport);
        var messages = new List<string>(); vm.StatusMessageRequested += (_, value) => messages.Add(value);
        vm.RegisteredRecordingCaptures = [Gesture]; vm.StartRecording();
        var session = transport.Starts.Single(); transport.Begin(session);
        transport.RejectWait(session, reason); vm.Deliver();
        Assert.IsTrue(vm.IsRecording); Assert.IsFalse(vm.CanPlay);
        Assert.IsEmpty(vm.ActiveMacro!.Events);
        StringAssert.Contains(messages.Last(), message);
        transport.Push(session, Wait()); transport.End(session); vm.Deliver();
        Assert.HasCount(1, vm.ActiveMacro.Events); Assert.IsFalse(vm.IsRecording);
    }

    [TestMethod]
    public void NativeFailureDrainsPriorWaitThenFailsWithoutAcceptingLateCommands()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredViewModel(transport);
        vm.RegisteredRecordingCaptures = [Gesture]; vm.StartRecording();
        var session = transport.Starts.Single(); transport.Begin(session); transport.Push(session, Wait()); transport.Fail(session);
        Assert.AreEqual(CapturedWaitSubmission.Inactive, vm.AddCapturedWait(Condition(), Gesture, unchecked((uint)Environment.TickCount)));
        Assert.IsTrue(vm.IsRecording, "UI completion remains queued.");
        vm.Deliver();
        Assert.HasCount(1, vm.ActiveMacro!.Events); Assert.IsFalse(vm.IsRecording);
    }

    [TestMethod]
    public void NativeTransportFlattensSnapshotAndCopiesConditionWithCancellationAsEmptyPayload()
    {
        var native = new FakeNative();
        using var transport = new NativeRecordingTransport(native);
        transport.Start(7, RecordingStopGestures.ControlW, [Gesture, new(5, 0x59)]);
        CollectionAssert.AreEqual(new uint[] { 2, 0x58, 5, 0x59 }, native.Gestures);
        var condition = Condition();
        Assert.IsTrue(transport.CapturedWait(7, condition, Gesture, uint.MaxValue));
        condition.Pixel.Rgb = 9;
        Assert.AreEqual(123u, WaitCondition.Parser.ParseFrom(native.Payload).Pixel.Rgb);
        Assert.AreEqual((7ul, 2u, 0x58u, uint.MaxValue), native.Command);
        Assert.IsTrue(transport.CapturedWait(7, null, Gesture, 0)); Assert.IsEmpty(native.Payload);
        native.Accept = false;
        Assert.IsFalse(transport.CapturedWait(7, condition, Gesture, 1));
    }

    private sealed class DeferredViewModel(FakeRecordingTransport transport)
        : MainWindowViewModel(new RecordEngine(transport), new FakePlaybackEngine(), new RunTestLibrary(), new FakePointerEnvironment())
    {
        private readonly Queue<Action> _pending = [];
        protected override void InvokeDispatcher(Action action) => _pending.Enqueue(action);
        public void Deliver() { while (_pending.TryDequeue(out var action)) action(); }
    }

    private sealed class FakeNative : NativeRecordingTransport.INativeApi
    {
        public uint[] Gestures = [];
        public byte[] Payload = [];
        public (ulong, uint, uint, uint) Command;
        public bool Accept = true;
        public bool Initialize(NativeRecordingTransport.InputCallback input, NativeRecordingTransport.StatusCallback status, NativeRecordingTransport.BoundaryCallback boundary) => true;
        public bool Start(ulong sessionId, RecordingStopGestures stopGestures, uint[] captureGestures) { Gestures = captureGestures; return true; }
        public bool Stop(ulong sessionId, RecordingStopGestures gesture, uint messageTime) => true;
        public bool CapturedWait(ulong sessionId, uint modifiers, uint key, uint messageTime, byte[] condition)
        { Payload = condition; Command = (sessionId, modifiers, key, messageTime); return Accept; }
        public void Shutdown() { }
    }
}
