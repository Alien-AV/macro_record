using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Google.Protobuf;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;
using RecordPlaybackDLLEnums;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class NativeRecordingTransportTests
{
    [TestMethod]
    public void BorrowedInputPreservesPayloadUnknownFieldsAndSessionAfterBufferIsFreed()
    {
        var native = new FakeNative();
        using var transport = new NativeRecordingTransport(native);
        var expected = new ProtobufInputEvent
        {
            TimeSinceLastEvent = ulong.MaxValue,
            MouseEvent = new() { X = int.MinValue, Y = int.MaxValue, ActionType = 0x801, WheelRotation = uint.MaxValue, RelativePosition = true }
        };
        // Unknown fields must survive without imposing the current key/mouse schema's size.
        using var bytes = new MemoryStream();
        expected.WriteTo(bytes);
        using (var coded = new CodedOutputStream(bytes, leaveOpen: true))
        {
            coded.WriteTag(100, WireFormat.WireType.LengthDelimited);
            coded.WriteBytes(ByteString.CopyFrom(Enumerable.Repeat((byte)0xa5, 9000).ToArray()));
        }
        var payload = bytes.ToArray();
        ProtobufInputEvent? received = null;
        ulong receivedId = 0;
        transport.Input += (id, input) => { receivedId = id; received = input; };

        native.Send(payload, ulong.MaxValue);

        Assert.AreEqual(ulong.MaxValue, receivedId);
        Assert.IsNotNull(received);
        CollectionAssert.AreEqual(payload, received.ToByteArray());
    }

    [TestMethod]
    public void CommandsAndBoundariesPreserveNativeScalarsAndUnknownOrigin()
    {
        var native = new FakeNative();
        using var transport = new NativeRecordingTransport(native);
        var boundaries = new List<(ulong, RecordingBoundary, RecordingStartKeys, RecordingStartKeys, PointerPosition?)>();
        transport.Boundary += (id, kind, keys, idle, origin) => boundaries.Add((id, kind, keys, idle, origin));
        transport.Start(ulong.MaxValue, RecordingStopGestures.ControlW | RecordingStopGestures.ControlR);
        transport.Stop(ulong.MaxValue, new RecordingStopCommand(RecordingStopGestures.ControlW, uint.MaxValue));
        transport.Stop(7, null);
        native.Boundary(ulong.MaxValue, RecordingBoundary.Started, RecordingStartKeys.LeftControl, RecordingStartKeys.Q, int.MinValue, int.MaxValue, 1);
        native.Boundary(7, RecordingBoundary.Stopped, RecordingStartKeys.None, RecordingStartKeys.None, 42, 55, 0);
        native.Boundary(8, RecordingBoundary.Failed, RecordingStartKeys.None, RecordingStartKeys.None, 42, 55, 2);

        CollectionAssert.AreEqual(new[] { (ulong.MaxValue, RecordingStopGestures.ControlW | RecordingStopGestures.ControlR) }, native.Starts);
        CollectionAssert.AreEqual(new[] { (ulong.MaxValue, RecordingStopGestures.ControlW, uint.MaxValue), (7ul, RecordingStopGestures.None, 0u) }, native.Stops);
        Assert.AreEqual((ulong.MaxValue, RecordingBoundary.Started, RecordingStartKeys.LeftControl, RecordingStartKeys.Q, (PointerPosition?)new PointerPosition(int.MinValue, int.MaxValue)), boundaries[0]);
        Assert.IsNull(boundaries[1].Item5);
        Assert.IsNull(boundaries[2].Item5);
    }

    [TestMethod]
    public void RejectedInitializationDoesNotShutdownAnotherNativeOwnerAndCanBeRetried()
    {
        var native = new FakeNative();
        using var owner = new NativeRecordingTransport(native);
        native.InitializeResult = false;
        Assert.ThrowsExactly<InvalidOperationException>(() => new NativeRecordingTransport(native));
        Assert.AreEqual(0, native.ShutdownCalls);
        owner.Start(7, RecordingStopGestures.None);
        owner.Dispose();
        native.InitializeResult = true;
        using var next = new NativeRecordingTransport(native);
        next.Start(8, RecordingStopGestures.None);
        Assert.HasCount(2, native.Starts);
    }

    [TestMethod]
    public void RejectedStartAndStopRemainRetryableAndDisposalIsIdempotent()
    {
        var native = new FakeNative { StartResult = false, StopResult = false };
        var transport = new NativeRecordingTransport(native);
        Assert.ThrowsExactly<InvalidOperationException>(() => transport.Start(7, RecordingStopGestures.None));
        native.StartResult = true;
        transport.Start(7, RecordingStopGestures.None);
        Assert.ThrowsExactly<InvalidOperationException>(() => transport.Stop(7, null));
        native.StopResult = true;
        transport.Stop(7, null);
        transport.Dispose();
        transport.Dispose();
        Assert.AreEqual(1, native.ShutdownCalls);
        Assert.ThrowsExactly<ObjectDisposedException>(() => transport.Start(8, RecordingStopGestures.None));
        Assert.ThrowsExactly<ObjectDisposedException>(() => transport.Stop(8, null));
        Assert.HasCount(2, native.Starts);
        Assert.HasCount(2, native.Stops);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnsuccessfulInitializationReleasesItsManagedRoot(bool interopThrows)
    {
        var native = new FakeNative { InitializeResult = false,
            InitializeError = interopThrows ? new DllNotFoundException("fake missing library") : null };
        RejectInitialization(native);
        Collect();
        Assert.IsFalse(native.LastInitializingOwner!.IsAlive);
        Assert.AreEqual(0, native.ShutdownCalls, "Failed initialization owns no native threads or callbacks to shut down.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RejectInitialization(FakeNative native)
    {
        if (native.InitializeError is not null)
            Assert.ThrowsExactly<DllNotFoundException>(() => new NativeRecordingTransport(native));
        else
            Assert.ThrowsExactly<InvalidOperationException>(() => new NativeRecordingTransport(native));
    }

    [TestMethod]
    public async Task MalformedPayloadFailsOnlyItsSessionAndCannotBecomeSuccessfulPartialCapture()
    {
        var native = new FakeNative();
        using var capture = new RecordingCapture(new NativeRecordingTransport(native));
        var first = new RecordingSession();
        var second = new RecordingSession();
        var received = new List<ProtobufInputEvent>();
        capture.Input += (_, input) => received.Add(input);
        capture.Start(first);
        native.Begin(first.Id);
        native.Send([0x12, 0xff], first.Id);
        Assert.IsTrue(capture.IsRecording, "A callback failure is not proof that native capture stopped.");
        Assert.IsFalse(first.Completion.IsCompleted);
        CollectionAssert.AreEqual(new[] { (first.Id, RecordingStopGestures.None, 0u) }, native.Stops);
        native.Send(Key(), first.Id);
        native.End(first.Id);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => first.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.IsFalse(capture.IsRecording);
        Assert.IsEmpty(received);
        capture.Start(second);
        native.Begin(second.Id);
        native.Send(Key(), second.Id);
        native.End(second.Id);
        await second.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.HasCount(1, received);
    }

    [TestMethod]
    public void InvalidBufferMetadataFailsBeforeReadingOrAllocating()
    {
        foreach (var (pointer, size) in new (nint, int)[]
            { (0, 3), (1, -1), (1, 0), (1, RecordingLibraryStore.MaximumMacroBytes + 1), (1, int.MaxValue) })
        {
            var native = new FakeNative();
            using var transport = new NativeRecordingTransport(native);
            var failures = new List<ulong>();
            var statuses = new List<StatusCode>();
            transport.Boundary += (id, kind, _, _, _) => { if (kind == RecordingBoundary.Failed) failures.Add(id); };
            transport.Status += statuses.Add;
            native.Input(pointer, size, 7);
            Assert.IsEmpty(failures, "A rejected buffer cannot stand in for the native terminal boundary.");
            native.End(7);
            CollectionAssert.AreEqual(new[] { 7ul }, failures);
            CollectionAssert.AreEqual(new[] { StatusCode.ErrorCouldNotProcessInputData }, statuses);
        }
    }

    [TestMethod]
    public async Task ThrowingInputSubscriberCannotEscapeCallbackOrLeaveCaptureSuccessful()
    {
        var native = new FakeNative();
        using var capture = new RecordingCapture(new NativeRecordingTransport(native));
        var session = new RecordingSession();
        capture.Input += (_, _) => throw new InvalidOperationException("subscriber failure");
        capture.Start(session);
        native.Begin(session.Id);
        native.Send(Key(), session.Id);
        Assert.IsTrue(capture.IsRecording);
        Assert.IsFalse(session.Completion.IsCompleted);
        native.End(session.Id);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.IsFalse(capture.IsRecording);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RejectedFailureStopKeepsOwnershipUntilExplicitRetryAndRealBoundary(bool interopThrows)
    {
        var native = new FakeNative { StopResult = false,
            StopError = interopThrows ? new InvalidOperationException("fake stop failure") : null };
        using var capture = new RecordingCapture(new NativeRecordingTransport(native));
        var session = new RecordingSession();
        capture.Start(session);
        native.Begin(session.Id);
        native.Send([0x12, 0xff], session.Id);
        Assert.IsTrue(capture.IsRecording);
        Assert.IsFalse(session.Completion.IsCompleted);
        Assert.AreEqual(0, native.ShutdownCalls, "A callback may never join its own thread.");
        Assert.IsFalse(capture.Start(new RecordingSession()));
        native.StopResult = true;
        native.StopError = null;
        Assert.IsTrue(capture.Stop());
        Assert.IsFalse(session.Completion.IsCompleted);
        native.End(session.Id);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.HasCount(2, native.Stops);
    }

    [TestMethod]
    public async Task ThrowingStartedSubscriberRequestsStopAndWaitsForNativeEnd()
    {
        var native = new FakeNative();
        using var capture = new RecordingCapture(new NativeRecordingTransport(native));
        var session = new RecordingSession();
        capture.Started += (_, _) => throw new InvalidOperationException("started subscriber failure");
        capture.Start(session);
        native.Begin(session.Id);
        Assert.IsTrue(capture.IsRecording);
        Assert.IsFalse(session.Completion.IsCompleted);
        Assert.HasCount(1, native.Stops);
        native.End(session.Id);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public async Task ThrowingEndedSubscriberFaultsCompletionWithoutEscapingTheNativeCallback()
    {
        var native = new FakeNative();
        using var capture = new RecordingCapture(new NativeRecordingTransport(native));
        var session = new RecordingSession();
        var error = new InvalidOperationException("ended subscriber failure");
        capture.Ended += (_, _) => throw error;
        capture.Start(session);
        native.Begin(session.Id);
        native.End(session.Id);
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreSame(error, failure);
        Assert.IsFalse(capture.IsRecording);
        Assert.IsEmpty(native.Stops, "The terminal boundary already proves the native session stopped.");
    }

    [TestMethod]
    public void ThrowingStatusSubscriberCannotEscapeOrPreventOtherStatusObservers()
    {
        var native = new FakeNative();
        using var transport = new NativeRecordingTransport(native);
        var statuses = new List<StatusCode>();
        transport.Status += _ => throw new InvalidOperationException("status subscriber failure");
        transport.Status += statuses.Add;
        native.Status(StatusCode.ErrorCouldNotProcessInputData);
        native.Send([0x12, 0xff], 7);
        Assert.HasCount(2, statuses);
        Assert.HasCount(1, native.Stops);
        Assert.AreEqual(0, native.ShutdownCalls);
    }

    [TestMethod]
    public void CallbackFailureKeepsViewModelBlockedUntilNativeBoundaryAndQueuedUiDrain()
    {
        var native = new FakeNative();
        using var vm = new DeferredViewModel(new RecordEngine(new NativeRecordingTransport(native)));
        var messages = new List<string>();
        vm.StatusMessageRequested += (_, message) => messages.Add(message);
        Assert.IsTrue(vm.StartRecording());
        var id = native.Starts.Single().Item1;
        native.Begin(id);
        native.Send(Key(), id);
        native.Send([0x12, 0xff], id);
        vm.Deliver();
        Assert.IsTrue(vm.IsRecording);
        Assert.IsFalse(vm.CanPlay);
        Assert.IsFalse(vm.StartRecording());
        Assert.HasCount(1, vm.ActiveMacro!.Events);
        Assert.IsTrue(messages.Any(message => message.Contains(nameof(StatusCode.ErrorCouldNotProcessInputData))));
        native.Send(Key(), id);
        native.End(id);
        Assert.IsTrue(vm.IsRecording, "Native completion must still drain its queued UI callback.");
        Assert.IsFalse(vm.CanPlay);
        vm.Deliver();
        Assert.IsFalse(vm.IsRecording);
        Assert.IsFalse(vm.IsFinalizingRecording);
        Assert.HasCount(1, vm.ActiveMacro.Events);
        StringAssert.Contains(messages.Last(), "Could not record");
    }

    [TestMethod]
    public void FailedShutdownRetainsCallbackRootsAndCanBeRetriedThroughCaptureOwner()
    {
        var native = new FakeNative { ShutdownError = new InvalidOperationException("join unavailable") };
        using var transport = new NativeRecordingTransport(native);
        var capture = new RecordingCapture(transport);
        var session = new RecordingSession();
        var received = new List<ProtobufInputEvent>();
        capture.Input += (_, input) => received.Add(input);
        capture.Start(session);
        native.Begin(session.Id);
        Assert.ThrowsExactly<InvalidOperationException>(capture.Dispose);
        Assert.IsTrue(capture.IsRecording);
        Assert.IsFalse(session.Completion.IsCompleted);
        Assert.ThrowsExactly<ObjectDisposedException>(() => transport.Start(7, RecordingStopGestures.None));
        native.ShutdownError = null;
        native.OnShutdown = () =>
        {
            // A real join must let the collector finish without holding the capture lock.
            var tail = Task.Run(() => { native.Send(Key(), session.Id); native.End(session.Id); });
            Assert.IsTrue(tail.Wait(TimeSpan.FromSeconds(2)), "Joining held the capture lock across callbacks.");
        };
        capture.Dispose();
        Assert.AreEqual(2, native.ShutdownCalls);
        Assert.IsTrue(session.Completion.IsCompletedSuccessfully);
        Assert.HasCount(1, received);
    }

    [TestMethod]
    public async Task ShutdownRetryThroughViewModelMustJoinBeforeReportingSuccess()
    {
        var native = new FakeNative { ShutdownError = new InvalidOperationException("join unavailable") };
        using var transport = new NativeRecordingTransport(native);
        using var vm = new DeferredViewModel(new RecordEngine(transport));
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(vm.ShutdownAsync);
            Assert.AreEqual(1, native.ShutdownCalls);
            native.ShutdownError = null;
            await vm.ShutdownAsync();
            Assert.AreEqual(2, native.ShutdownCalls, "Shutdown cannot report success before retrying the native join.");
            await vm.ShutdownAsync();
            Assert.AreEqual(2, native.ShutdownCalls);
        }
        finally { native.ShutdownError = null; }
    }

    [TestMethod]
    public void ViewModelKeepsCaptureCallbacksAttachedUntilSuccessfulJoin()
    {
        var native = new FakeNative { ShutdownError = new InvalidOperationException("join unavailable") };
        using var transport = new NativeRecordingTransport(native);
        using var vm = new DeferredViewModel(new RecordEngine(transport));
        try
        {
            Assert.IsTrue(vm.StartRecording());
            var id = native.Starts.Single().Item1;
            native.Begin(id);
            vm.Deliver();
            Assert.ThrowsExactly<InvalidOperationException>(vm.Dispose);
            native.Send(Key(), id);
            vm.Deliver();
            Assert.HasCount(1, vm.ActiveMacro!.Events, "Failed shutdown must retain the destination for callbacks that still belong to native capture.");
            Assert.IsTrue(vm.IsRecording);
            native.ShutdownError = null;
            native.OnShutdown = () => native.End(id);
            vm.Dispose();
            Assert.AreEqual(2, native.ShutdownCalls);
        }
        finally { native.ShutdownError = null; }
    }

    private sealed class DeferredViewModel(IRecordEngine engine)
        : MainWindowViewModel(engine, new FakePlaybackEngine(), new RunTestLibrary(), new FakePointerEnvironment())
    {
        private readonly Queue<Action> _pending = [];
        protected override void InvokeDispatcher(Action action) => _pending.Enqueue(action);
        public void Deliver() { while (_pending.TryDequeue(out var action)) action(); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeOwnershipRootsCallbacksUntilJoinAndReleasesThemAfterwards(bool failedJoin)
    {
        var native = new FakeNative();
        var weakOwner = CreateWeakOwner(native);
        try
        {
            if (failedJoin)
            {
                native.ShutdownError = new InvalidOperationException("join unavailable");
                Assert.ThrowsExactly<InvalidOperationException>(() => DisposeWeakOwner(weakOwner));
            }
            Collect();
            Assert.IsTrue(weakOwner.IsAlive, "Native callbacks need a managed root until their threads join.");
        }
        finally
        {
            native.ShutdownError = null;
            if (weakOwner.IsAlive) DisposeWeakOwner(weakOwner);
        }
        Collect();
        Assert.IsFalse(weakOwner.IsAlive, "A successful join must release the callback root.");
        Assert.AreEqual(failedJoin ? 2 : 1, native.ShutdownCalls);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWeakOwner(FakeNative native) => new(new NativeRecordingTransport(native));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DisposeWeakOwner(WeakReference owner) => ((NativeRecordingTransport)owner.Target!).Dispose();

    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static byte[] Key() => new ProtobufInputEvent { TimeSinceLastEvent = 47, KeyboardEvent = new() { VirtualKeyCode = 65 } }.ToByteArray();

    private sealed class FakeNative : NativeRecordingTransport.INativeApi
    {
        private WeakReference<NativeRecordingTransport.InputCallback>? _input;
        private WeakReference<NativeRecordingTransport.BoundaryCallback>? _boundary;
        private WeakReference<NativeRecordingTransport.StatusCallback>? _status;
        public bool InitializeResult = true;
        public bool StartResult = true;
        public bool StopResult = true;
        public Exception? InitializeError;
        public Exception? StopError;
        public Exception? ShutdownError;
        public WeakReference? LastInitializingOwner;
        public Action? OnShutdown;
        public int ShutdownCalls;
        public List<(ulong, RecordingStopGestures)> Starts { get; } = [];
        public List<(ulong, RecordingStopGestures, uint)> Stops { get; } = [];

        public bool Initialize(NativeRecordingTransport.InputCallback input, NativeRecordingTransport.StatusCallback status, NativeRecordingTransport.BoundaryCallback boundary)
        {
            LastInitializingOwner = new(input.Target);
            if (InitializeError is { } error) throw error;
            if (!InitializeResult) return false;
            // Native function pointers do not root reverse-P/Invoke delegates.
            _input = new(input); _status = new(status); _boundary = new(boundary);
            return true;
        }

        public bool Start(ulong sessionId, RecordingStopGestures stopGestures, uint[] captureGestures) { Starts.Add((sessionId, stopGestures)); return StartResult; }
        public bool CapturedWait(ulong sessionId, uint modifiers, uint key, uint messageTime, byte[] condition) => true;
        public bool Stop(ulong sessionId, RecordingStopGestures gesture, uint messageTime)
        { Stops.Add((sessionId, gesture, messageTime)); if (StopError is { } error) throw error; return StopResult; }
        public void Shutdown()
        {
            ShutdownCalls++;
            Collect();
            Assert.IsTrue(_input!.TryGetTarget(out _));
            Assert.IsTrue(_boundary!.TryGetTarget(out _));
            Assert.IsTrue(_status!.TryGetTarget(out _));
            OnShutdown?.Invoke();
            if (ShutdownError is { } error) throw error;
            _input = null; _boundary = null; _status = null;
        }

        public void Input(nint buffer, int size, ulong id) { Assert.IsTrue(_input!.TryGetTarget(out var callback)); callback(buffer, size, id); }
        public void Boundary(ulong id, RecordingBoundary kind, RecordingStartKeys keys, RecordingStartKeys idle, int x, int y, uint valid)
        { Assert.IsTrue(_boundary!.TryGetTarget(out var callback)); callback(id, kind, keys, idle, x, y, valid); }
        public void Status(StatusCode status) { Assert.IsTrue(_status!.TryGetTarget(out var callback)); callback(status); }
        public void Begin(ulong id) => Boundary(id, RecordingBoundary.Started, RecordingStartKeys.None, RecordingStartKeys.None, -10, 20, 1);
        public void End(ulong id) => Boundary(id, RecordingBoundary.Stopped, RecordingStartKeys.None, RecordingStartKeys.None, 0, 0, 0);
        public void Send(byte[] payload, ulong id)
        {
            var buffer = Marshal.AllocHGlobal(payload.Length);
            try
            {
                Marshal.Copy(payload, 0, buffer, payload.Length);
                Input(buffer, payload.Length, id);
                Marshal.Copy(new byte[payload.Length], 0, buffer, payload.Length);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
}
