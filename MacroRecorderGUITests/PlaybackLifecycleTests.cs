using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;
using ProtobufGenerated;
using RecordPlaybackDLLEnums;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public class PlaybackLifecycleTests
{
    private sealed class FakeNative : IPlaybackNativeApi
    {
        public PlaybackResult Result = PlaybackResult.Running;
        public PlaybackResult StartResult = PlaybackResult.Running;
        public PlaybackResult AbortResult = PlaybackResult.Cancelled;
        public Exception? AbortError;
        public int Starts;
        public int Aborts;
        public byte[] Bytes = [];
        public bool Loop;
        public PlaybackResult Start(byte[] events, bool loop, out ulong sessionId)
        {
            sessionId = (ulong)++Starts;
            Bytes = events.ToArray();
            Loop = loop;
            return StartResult;
        }
        public PlaybackResult Poll(ulong sessionId) => Result;
        public PlaybackResult Abort(ulong sessionId)
        {
            Aborts++;
            if (AbortError is not null) throw AbortError;
            return AbortResult;
        }
        public PlaybackResult SetLoop(ulong sessionId, bool loop) { Loop = loop; return Result; }
    }

    private static KeyboardEvent Key() => new(VirtualKey.A, false);

    private sealed class QueuedViewModel(IPlaybackEngine engine)
        : MainWindowViewModel(new FakeRecordEngine(), engine)
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _callbacks = new();
        protected override void InvokeDispatcher(Action action) => _callbacks.Enqueue(action);
        public void DrainCallbacks()
        {
            while (_callbacks.TryDequeue(out var action)) action();
        }
    }

    private sealed class ControlledUiContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _callbacks = new();
        private readonly SemaphoreSlim _posted = new(0);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            _callbacks.Enqueue(() => callback(state));
            _posted.Release();
        }

        public T Run<T>(Func<T> action)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try { return action(); }
            finally { SetSynchronizationContext(previous); }
        }

        public void Run(Action action) => Run(() => { action(); return true; });
        public Task<bool> WaitForCallbackAsync() => _posted.WaitAsync(TimeSpan.FromSeconds(2));

        public void RunNextCallback()
        {
            Assert.IsTrue(_callbacks.TryDequeue(out var callback));
            Run(callback);
        }

        public void Dispose() => _posted.Dispose();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PolledFailureSurvivesAbortAndCannotReportAfterNewPlayback(bool dispatchFailureBeforeAbort)
    {
        using var ui = new ControlledUiContext();
        var native = new FakeNative { Result = PlaybackResult.InjectionFailed };
        using var vm = ui.Run(() => new MainWindowViewModel(new FakeRecordEngine(), new PlaybackEngine(native)));
        var messages = new List<string>();
        vm.StatusMessageRequested += (_, message) => messages.Add(message);
        var first = ui.Run(() =>
        {
            vm.ActiveMacro!.AddEvent(Key());
            return vm.PlayActiveMacro();
        });

        // Poll has faulted the engine task and posted the real await continuation.
        // The UI queue is held explicitly; no timing sleep controls this ordering.
        Assert.IsTrue(await ui.WaitForCallbackAsync());
        Assert.IsFalse(first.IsCompleted);
        if (dispatchFailureBeforeAbort) ui.RunNextCallback();

        ui.Run(vm.AbortPlayback);
        Assert.AreEqual(0, native.Aborts);
        Assert.IsNull(vm.PlayingMacro);
        Assert.IsFalse(messages.Contains("Playback aborted"));
        Assert.AreEqual(1, messages.Count(message => message.Contains("inject or release")));

        native.Result = PlaybackResult.Running;
        var secondMacro = ui.Run(vm.AddNewTab);
        var second = ui.Run(() =>
        {
            secondMacro.AddEvent(Key());
            return vm.PlayActiveMacro();
        });
        var messagesBeforeOldContinuation = messages.ToArray();
        if (!dispatchFailureBeforeAbort) ui.RunNextCallback();
        await first.WaitAsync(TimeSpan.FromSeconds(2));
        CollectionAssert.AreEqual(messagesBeforeOldContinuation, messages);
        Assert.AreEqual(2, native.Starts);
        Assert.AreSame(secondMacro, vm.PlayingMacro);
        Assert.IsFalse(second.IsCompleted);

        ui.Run(vm.AbortPlayback);
        Assert.IsTrue(await ui.WaitForCallbackAsync());
        ui.RunNextCallback();
        await second.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsNull(vm.PlayingMacro);
    }

    [TestMethod]
    public async Task AbortCleanupFailureIsReportedOnceAndLateCompletionCannotClearRestart()
    {
        var native = new FakeNative { AbortResult = PlaybackResult.InjectionFailed };
        using var vm = new QueuedViewModel(new PlaybackEngine(native));
        var messages = new List<string>();
        vm.StatusMessageRequested += (_, message) => messages.Add(message);
        vm.ActiveMacro!.AddEvent(Key());
        var first = vm.PlayActiveMacro();

        vm.AbortPlayback();
        Assert.IsNull(vm.PlayingMacro);
        Assert.IsFalse(messages.Contains("Playback aborted"));
        Assert.AreEqual(1, messages.Count(message => message.Contains("inject or release")));

        var secondMacro = vm.AddNewTab();
        secondMacro.AddEvent(Key());
        var second = vm.PlayActiveMacro();
        await first.WaitAsync(TimeSpan.FromSeconds(2));
        vm.DrainCallbacks();
        Assert.AreEqual(2, native.Starts);
        Assert.AreSame(secondMacro, vm.PlayingMacro);
        Assert.AreEqual(1, messages.Count(message => message.Contains("inject or release")));

        native.AbortResult = PlaybackResult.Cancelled;
        vm.AbortPlayback();
        await second.WaitAsync(TimeSpan.FromSeconds(2));
        vm.DrainCallbacks();
    }

    [TestMethod]
    public async Task EngineAbortReportsCleanupFailureSynchronouslyAndThroughSessionTask()
    {
        var native = new FakeNative { AbortResult = PlaybackResult.InjectionFailed };
        using var engine = new PlaybackEngine(native);
        var task = engine.PlaybackEventsAsync([Key()]);
        var error = Assert.Throws<InvalidOperationException>(engine.PlaybackEventAbort);
        StringAssert.Contains(error.Message, "inject or release");
        var taskError = await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
        StringAssert.Contains(taskError.Message, "inject or release");
        Assert.AreEqual(1, native.Aborts);
    }

    [TestMethod]
    public async Task CleanupFailureDuringDisposeDoesNotEscapeWindowShutdown()
    {
        var native = new FakeNative { AbortResult = PlaybackResult.InjectionFailed };
        var engine = new PlaybackEngine(native);
        var vm = new QueuedViewModel(engine);
        vm.ActiveMacro!.AddEvent(Key());
        var task = vm.PlayActiveMacro();
        var messages = new List<string>();
        vm.StatusMessageRequested += (_, message) => messages.Add(message);

        vm.Dispose();
        vm.Dispose();
        await task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.DrainCallbacks();
        Assert.AreEqual(1, native.Aborts);
        Assert.IsNull(vm.PlayingMacro);
        Assert.AreEqual(0, messages.Count);
        Assert.ThrowsExactly<ObjectDisposedException>(() => engine.PlaybackEventsAsync([Key()]));
    }

    [TestMethod]
    public async Task AbortInteropFailureKeepsOwnershipUntilSuccessfulRetry()
    {
        var native = new FakeNative { AbortError = new InvalidOperationException("fake interop failure") };
        using var vm = new QueuedViewModel(new PlaybackEngine(native));
        vm.ActiveMacro!.AddEvent(Key());
        var owner = vm.ActiveMacro;
        var task = vm.PlayActiveMacro();
        vm.AbortPlayback();
        Assert.AreSame(owner, vm.PlayingMacro);
        await vm.PlayActiveMacro();
        Assert.AreEqual(1, native.Starts);

        native.AbortError = null;
        vm.AbortPlayback();
        await task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.DrainCallbacks();
        Assert.IsNull(vm.PlayingMacro);
    }

    [TestMethod]
    public async Task EngineSerializesSnapshotAndRejectsOverlapUntilAbort()
    {
        var native = new FakeNative();
        using var engine = new PlaybackEngine(native);
        var key = Key();
        var first = engine.PlaybackEventsAsync([key], true);
        key.TimeSinceLastEvent = 12345;
        Assert.AreEqual(0ul, ProtobufInputEventList.Parser.ParseFrom(native.Bytes).InputEvents[0].TimeSinceLastEvent);
        Assert.IsTrue(native.Loop);
        engine.SetLoopPlayback(false);
        Assert.IsFalse(native.Loop);
        Assert.ThrowsExactly<InvalidOperationException>(() => engine.PlaybackEventsAsync([Key()]));
        Assert.AreEqual(1, native.Starts);
        engine.PlaybackEventAbort();
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await first);
        var second = engine.PlaybackEventsAsync([Key()]);
        native.Result = PlaybackResult.Finished;
        await second.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(2, native.Starts);
        Assert.AreEqual(1, native.Aborts);
    }

    [TestMethod]
    public async Task EngineDisposeCancelsOwnedSessionAndCannotRestart()
    {
        var native = new FakeNative();
        var engine = new PlaybackEngine(native);
        var task = engine.PlaybackEventsAsync([Key()], true);
        engine.Dispose();
        engine.Dispose();
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
        Assert.AreEqual(1, native.Aborts);
        Assert.ThrowsExactly<ObjectDisposedException>(() => engine.PlaybackEventsAsync([Key()]));
    }

    [TestMethod]
    public async Task NativeInjectionFailureBecomesObservedTaskError()
    {
        var native = new FakeNative { Result = PlaybackResult.InjectionFailed };
        using var engine = new PlaybackEngine(native);
        var task = engine.PlaybackEventsAsync([Key()]);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await task.WaitAsync(TimeSpan.FromSeconds(2)));
        StringAssert.Contains(error.Message, "inject or release");
    }

    [TestMethod]
    public async Task AbortAndStaleCompletionCannotRestartOrClearAnotherMacro()
    {
        var record = new FakeRecordEngine();
        var firstDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakePlaybackEngine { Pending = firstDone };
        using var vm = new FakeMainWindowViewModel(record, engine) { LoopPlayback = true };
        var firstMacro = vm.ActiveMacro!;
        firstMacro.AddEvent(Key());
        var first = vm.PlayActiveMacro();
        var secondMacro = vm.AddNewTab();
        secondMacro.AddEvent(Key());
        Assert.AreSame(firstMacro, vm.PlayingMacro);
        await vm.PlayActiveMacro();
        Assert.AreEqual(1, engine.Starts);
        vm.AbortPlayback();
        var secondDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Pending = secondDone;
        var second = vm.PlayActiveMacro();
        firstDone.SetResult();
        await first;
        record.PushStatus(StatusCode.ErrorCouldNotProcessInputData);
        Assert.AreEqual(2, engine.Starts);
        Assert.AreSame(secondMacro, vm.PlayingMacro);
        secondDone.SetResult();
        await second;
        Assert.IsNull(vm.PlayingMacro);
        Assert.AreEqual(2, engine.Starts);
    }

    [TestMethod]
    public async Task LoopKeepsOriginAcrossTabSwitchAndClosingOwnerAborts()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakePlaybackEngine { Pending = pending };
        using var vm = new FakeMainWindowViewModel(new FakeRecordEngine(), engine) { LoopPlayback = true };
        var owner = vm.ActiveMacro!;
        owner.AddEvent(Key());
        var task = vm.PlayActiveMacro();
        var other = vm.AddNewTab();
        Assert.AreSame(owner, vm.PlayingMacro);
        Assert.IsTrue(engine.Loop);
        vm.LoopPlayback = false;
        Assert.IsFalse(engine.Loop);
        vm.CloseTab(other);
        Assert.AreEqual(0, engine.Aborts);
        vm.CloseTab(owner);
        Assert.AreEqual(1, engine.Aborts);
        pending.SetResult();
        await task;
        Assert.IsNull(vm.PlayingMacro);
        Assert.AreEqual(1, engine.Starts);
    }

    [TestMethod]
    public async Task EmptyMacroDoesNotStartAndErrorsAreSurfaced()
    {
        var native = new FakeNative { StartResult = PlaybackResult.InvalidInput };
        using var vm = new FakeMainWindowViewModel(new FakeRecordEngine(), new PlaybackEngine(native));
        var messages = new List<string>();
        vm.StatusMessageRequested += (_, message) => messages.Add(message);
        await vm.PlayActiveMacro();
        Assert.AreEqual(0, native.Starts);
        vm.ActiveMacro!.AddEvent(Key());
        await vm.PlayActiveMacro();
        Assert.AreEqual(1, messages.Count);
        StringAssert.Contains(messages[0], "Could not play macro");
        Assert.IsNull(vm.PlayingMacro);
    }

    [TestMethod]
    public async Task DisposalIgnoresLateCompletionAndRecorderStatus()
    {
        var record = new FakeRecordEngine();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakePlaybackEngine { Pending = pending };
        var vm = new FakeMainWindowViewModel(record, engine) { LoopPlayback = true };
        vm.ActiveMacro!.AddEvent(Key());
        var task = vm.PlayActiveMacro();
        var messages = new List<string>();
        vm.StatusMessageRequested += (_, message) => messages.Add(message);
        vm.Dispose();
        pending.SetResult();
        await task;
        record.PushStatus(StatusCode.ErrorCouldNotProcessInputData);
        Assert.IsTrue(engine.Disposed);
        Assert.AreEqual(1, engine.Starts);
        Assert.AreEqual(0, messages.Count);
    }
}
