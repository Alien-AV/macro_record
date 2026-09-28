using Google.Protobuf;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class RecordingStopFinalizationTests
{
    [TestMethod]
    public void RejectedStopDoesNotApplyUncommittedDelayToTheQueuedTail()
    {
        var transport = new FakeRecordingTransport();
        using var engine = new RejectableStopEngine(transport) { RejectStop = true };
        using var vm = new DeferredViewModel(engine);
        Assert.IsTrue(vm.StartRecording());
        var session = transport.Starts.Single();
        transport.Begin(session);
        var input = Input(456);
        var expected = input.ToByteArray();
        transport.Push(session, input);
        vm.StopRecording(123);
        Assert.IsTrue(vm.IsRecording);
        transport.End(session);
        vm.Deliver();
        Assert.IsFalse(vm.IsRecording);
        Assert.IsFalse(vm.IsFinalizingRecording);
        CollectionAssert.AreEqual(expected, vm.ActiveMacro!.Events.Single().OriginalProtobufInputEvent.ToByteArray());
    }

    [TestMethod]
    public async Task RejectedStopCanRetryWithDelayAndStillWaitForQueuedInput()
    {
        var transport = new FakeRecordingTransport();
        using var engine = new RejectableStopEngine(transport) { RejectStop = true };
        using var vm = new DeferredViewModel(engine);
        Assert.IsTrue(vm.StartRecording());
        var session = transport.Starts.Single();
        transport.Begin(session);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.StopRecordingAsync(123));
        engine.RejectStop = false;
        var stop = vm.StopRecordingAsync(789);
        Assert.IsFalse(stop.IsCompleted, "The retry must wait for the native boundary and queued input. " + stop.Exception);
        var input = Input(456);
        var expected = input.Clone();
        expected.TimeSinceLastEvent = 789;
        transport.Push(session, input);
        transport.End(session);
        Assert.IsFalse(stop.IsCompleted);
        vm.Deliver();
        await stop.WaitAsync(TimeSpan.FromSeconds(3));
        CollectionAssert.AreEqual(expected.ToByteArray(), vm.ActiveMacro!.Events.Single().OriginalProtobufInputEvent.ToByteArray());
        Assert.IsFalse(vm.IsFinalizingRecording);
    }

    [TestMethod]
    public async Task StopAfterNativeCompletionWaitsForQueuedBoundaryAndAppliesDelayOnce()
    {
        var transport = new FakeRecordingTransport();
        using var vm = new DeferredViewModel(new RecordEngine(transport));
        Assert.IsTrue(vm.StartRecording());
        var session = transport.Starts.Single();
        transport.Begin(session);
        var input = Input(456);
        var expected = input.Clone();
        expected.TimeSinceLastEvent = 123;
        transport.Push(session, input);
        transport.End(session);
        Assert.IsTrue(vm.IsRecording, "The UI has not received the completion yet.");
        var stop = vm.StopRecordingAsync(123);
        Assert.IsFalse(stop.IsCompleted, "Native completion must still await the queued UI drain. " + stop.Exception);
        var retry = vm.StopRecordingAsync(789);
        Assert.IsFalse(retry.IsCompleted);
        vm.Deliver();
        await Task.WhenAll(stop, retry).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsEmpty(transport.Stops);
        CollectionAssert.AreEqual(expected.ToByteArray(), vm.ActiveMacro!.Events.Single().OriginalProtobufInputEvent.ToByteArray());
        Assert.IsFalse(vm.IsRecording);
        Assert.IsFalse(vm.IsFinalizingRecording);
    }

    private static ProtobufInputEvent Input(ulong delay) => ProtobufInputEvent.Parser.ParseFrom(
        FakeRecordEngine.MakeKeyboardEvent(0x41, false, delay).ToByteArray().Concat(new byte[] { 0xF8, 0x07, 0x7B }).ToArray());

    private sealed class DeferredViewModel(IRecordEngine engine)
        : MainWindowViewModel(engine, new FakePlaybackEngine(), new RunTestLibrary())
    {
        private readonly Queue<Action> _pending = [];
        protected override void InvokeDispatcher(Action action) => _pending.Enqueue(action);
        public void Deliver() { while (_pending.TryDequeue(out var action)) action(); }
    }

    private sealed class RejectableStopEngine(FakeRecordingTransport transport) : IRecordEngine
    {
        private readonly RecordEngine _inner = new(transport);
        public bool RejectStop { get; set; }
        public event RecordEngine.RecordEventsEventHandler? RecordedEvent { add => _inner.RecordedEvent += value; remove => _inner.RecordedEvent -= value; }
        public event RecordEngine.RecordStatusEventHandler? RecordStatus { add => _inner.RecordStatus += value; remove => _inner.RecordStatus -= value; }
        public event Action<RecordingSession, Exception?>? RecordingEnded { add => _inner.RecordingEnded += value; remove => _inner.RecordingEnded -= value; }
        public event Action<RecordingSession, PointerPosition?>? RecordingStarted { add => _inner.RecordingStarted += value; remove => _inner.RecordingStarted -= value; }
        public bool StartRecord(RecordingSession session) => _inner.StartRecord(session);
        public bool StopRecord(RecordingStopCommand? command = null) => !RejectStop && _inner.StopRecord(command);
        public void Dispose() => _inner.Dispose();
        public CapturedWaitSubmission CapturedWait(ProtobufGenerated.WaitCondition? condition, RecordingCaptureGesture gesture, uint messageTime) => _inner.CapturedWait(condition, gesture, messageTime);
    }
}
