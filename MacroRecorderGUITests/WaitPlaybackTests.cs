using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitPlaybackTests
{
    private sealed class Native : IPlaybackWaitNativeApi
    {
        public PlaybackResult Result = PlaybackResult.Running;
        public ulong Id;
        public ulong Index;
        public int Starts, Resolves;
        public NativeWaitRequest Request;
        public bool TimeoutBeforeRequest, TimeoutAfterSuccess, FailTerminalQuery;
        public int ActiveQueries;
        public volatile Exception? PollError;
        public Exception? AbortError;
        public TaskCompletionSource? AbortAttempted;
        public PlaybackResult Start(byte[] events, bool loop, out ulong sessionId)
        {
            Result = PlaybackResult.Running; Starts++; sessionId = ++Id;
            Request = new() { Occurrence = 7, EventIndex = Index, RemainingUs = 1_000_000 };
            return Result;
        }
        public PlaybackResult Poll(ulong id) => PollError is { } error ? throw error : TimeoutBeforeRequest ? Result = PlaybackResult.WaitTimedOut : Result;
        public PlaybackResult Abort(ulong id)
        {
            AbortAttempted?.TrySetResult();
            if (AbortError is { } error) throw error;
            return Result = PlaybackResult.Cancelled;
        }
        public PlaybackResult SetLoop(ulong id, bool loop) => Result;
        public PlaybackResult WaitRequest(ulong id, out NativeWaitRequest request)
        {
            if (Result == PlaybackResult.Running) ActiveQueries++;
            else if (FailTerminalQuery) throw new InvalidOperationException("fake diagnostic query failure");
            request = Request; return Result;
        }
        public PlaybackResult ResolveWait(ulong id, ulong occurrence, bool satisfied)
        {
            Assert.AreEqual(Id, id); Assert.AreEqual(Request.Occurrence, occurrence);
            Resolves++; Result = satisfied ? PlaybackResult.Finished : PlaybackResult.WaitFailed;
            if (satisfied && TimeoutAfterSuccess)
            {
                Request = new() { Occurrence = Request.Occurrence + 1, EventIndex = Request.EventIndex + 1 };
                Result = PlaybackResult.WaitTimedOut;
            }
            return PlaybackResult.Running;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RunPermitRemainsHeldAfterObservationReturnsUntilCancellationCallbackFinishes(bool abort)
    {
        using var release = new ManualResetEventSlim();
        using var cancel = new CancellationTokenSource();
        var observing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<WaitObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var runner = new WaitRunner(new Observer(async (condition, token) =>
        {
            Interlocked.Increment(ref calls);
            token.Register(() => { callbackStarted.TrySetResult(); release.Wait(); callbackFinished.TrySetResult(); });
            observing.TrySetResult();
            try { return await response.Task; }
            finally { returned.TrySetResult(); }
        }));
        try
        {
            var first = runner.RunAsync(Wait().Condition, TimeSpan.FromSeconds(2), cancel.Token);
            await observing.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (abort)
            {
                cancel.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(2)));
                await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                response.SetResult(new(ObservationState.Match, "returned after abort"));
            }
            else
            {
                response.SetResult(new(ObservationState.Match, "success before blocked teardown"));
                Assert.IsTrue((await first.WaitAsync(TimeSpan.FromSeconds(2))).Satisfied);
                await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            await returned.Task.WaitAsync(TimeSpan.FromSeconds(2));
            for (var i = 0; i < 3; i++)
                Assert.IsFalse((await runner.RunAsync(Wait().Condition, TimeSpan.FromMilliseconds(30), default).WaitAsync(TimeSpan.FromSeconds(2))).Satisfied);
            Assert.AreEqual(1, calls, "Returned observations must not release the permit while cancellation callbacks remain blocked.");
            release.Set(); await callbackFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsTrue((await runner.RunAsync(Wait().Condition, TimeSpan.FromSeconds(2), default)).Satisfied);
            Assert.AreEqual(2, calls);
        }
        finally { release.Set(); response.TrySetResult(new(ObservationState.Match, "released")); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TerminalIdentityAttributesUnobservedTimeoutRatherThanPreviousWait(bool afterSuccess)
    {
        var native = new Native { Index = 1, TimeoutBeforeRequest = !afterSuccess, TimeoutAfterSuccess = afterSuccess };
        var calls = 0;
        using var engine = new PlaybackEngine(native, new Observer((condition, token) =>
        { Interlocked.Increment(ref calls); return ValueTask.FromResult(new WaitObservation(ObservationState.Match, "prior success")); }));
        var timed = Wait().Condition; timed.TimeoutUs = 1000; timed.Window.Target.Title = "Unobserved short wait";
        var previous = Wait().Condition; previous.Window.Target.Title = "Previous successful wait";
        var events = new List<InputEvent> { new MouseEvent(0, 0, MacroRecorderGUI.Common.MouseActionTypeFlags.Move) };
        if (afterSuccess) events.Add(new WaitConditionEvent(previous));
        events.Add(new WaitConditionEvent(timed));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PlaybackEventsAsync(events).WaitAsync(TimeSpan.FromSeconds(2)));
        StringAssert.Contains(error.Message, afterSuccess ? "event 3:" : "event 2:");
        StringAssert.Contains(error.Message, "Unobserved short wait");
        StringAssert.Contains(error.Message, "no observation received");
        Assert.IsFalse(error.Message.Contains("Previous successful wait")); Assert.IsFalse(error.Message.Contains("prior success"));
        Assert.AreEqual(afterSuccess ? 1 : 0, calls);
        if (afterSuccess) Assert.IsTrue(native.ActiveQueries > 0); else Assert.AreEqual(0, native.ActiveQueries);
    }

    [TestMethod]
    public async Task FailedTerminalIdentityQueryCompletesWithUnattributedError()
    {
        var native = new Native { TimeoutBeforeRequest = true, FailTerminalQuery = true };
        using var engine = new PlaybackEngine(native, new Observer((condition, token) => throw new AssertFailedException("No observation expected")));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PlaybackEventsAsync([Wait()]).WaitAsync(TimeSpan.FromSeconds(2)));
        StringAssert.Contains(error.Message, "Terminal wait identity is unavailable"); Assert.IsFalse(error.Message.Contains("event 1"));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ThrowingOrBlockedObserverCancellationCannotStrandAbortOrNativeTimeout(bool timeout, bool blocks)
    {
        var native = new Native();
        using var release = new ManualResetEventSlim();
        var observing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<WaitObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var engine = new PlaybackEngine(native, new Observer((condition, token) =>
        {
            token.Register(() =>
            {
                callbackStarted.TrySetResult();
                try { if (blocks) release.Wait(); else throw new InvalidOperationException("fake cancellation callback failed"); }
                finally { callbackFinished.TrySetResult(); }
            });
            observing.TrySetResult(); return new(response.Task);
        }));
        try
        {
            var playback = engine.PlaybackEventsAsync([Wait()]); await observing.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (timeout) native.Result = PlaybackResult.WaitTimedOut;
            else await Task.Run(engine.PlaybackEventAbort).WaitAsync(TimeSpan.FromSeconds(2));
            if (timeout) await Assert.ThrowsAsync<InvalidOperationException>(() => playback.WaitAsync(TimeSpan.FromSeconds(2)));
            else await Assert.ThrowsAsync<OperationCanceledException>(() => playback.WaitAsync(TimeSpan.FromSeconds(2)));
            await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(0, native.Resolves);
            var next = engine.PlaybackEventsAsync([Wait()]);
            await Task.Run(engine.PlaybackEventAbort).WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<OperationCanceledException>(() => next);
        }
        finally { release.Set(); response.TrySetResult(new(ObservationState.Match, "late")); await callbackFinished.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
    }

    private sealed class FailedAbortEngine : IPlaybackEngine, IWaitPlaybackProgress
    {
        public readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Aborts;
        public WaitProgress? CurrentWait => new("fake wait", TimeSpan.Zero, TimeSpan.FromSeconds(30), TimeSpan.Zero, "pending");
        public Task PlaybackEventsAsync(IEnumerable<InputEvent> events, bool loop = false) => Completion.Task;
        public void PlaybackEventAbort()
        {
            if (++Aborts == 1) throw new InvalidOperationException("fake abort interop failure");
            Completion.TrySetCanceled();
        }
        public void SetLoopPlayback(bool loop) { }
        public void Dispose() { }
    }
    [TestMethod]
    public async Task WaitingSubscriberAbortFailureRetainsOwnershipUntilSuccessfulRetry()
    {
        var engine = new FailedAbortEngine(); var workflow = new PlaybackWorkflow(engine);
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workflow.StateChanged += state =>
        {
            if (state.Phase == PlaybackPhase.Waiting) { attempted.TrySetResult(); workflow.Abort(); }
        };
        var playback = workflow.PlayAsync([Wait()], new() { Countdown = TimeSpan.Zero });
        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        // Synchronize with the failed callback by reading State under the workflow gate.
        Assert.AreEqual(PlaybackPhase.Stopping, workflow.State.Phase);
        Assert.IsTrue(workflow.IsActive); Assert.IsFalse(playback.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => workflow.PlayAsync([Wait()], new() { Countdown = TimeSpan.Zero }));
        workflow.Abort();
        await Assert.ThrowsAsync<OperationCanceledException>(() => playback.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.IsFalse(workflow.IsActive); Assert.AreEqual(2, engine.Aborts);
    }
    private sealed class Observer(Func<WaitCondition, CancellationToken, ValueTask<WaitObservation>> observe) : IWaitObserver
    {
        public ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken token) => observe(condition, token);
    }
    private static WaitConditionEvent Wait()
    {
        var condition = ConditionalWaitTests.Condition(); condition.StableForUs = 0;
        return new(condition);
    }

    [TestMethod]
    public async Task PollAndAbortInteropFailureCancelsObserverWithoutResolvingRetainedNativeWait()
    {
        var native = new Native
        {
            AbortError = new InvalidOperationException("fake abort interop failure"),
            AbortAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var observing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<WaitObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var engine = new PlaybackEngine(native, new Observer((condition, token) =>
        {
            token.Register(() => cancelled.TrySetResult());
            observing.TrySetResult();
            return new(response.Task);
        }));
        var task = engine.PlaybackEventsAsync([Wait()]);
        try
        {
            await observing.Task.WaitAsync(TimeSpan.FromSeconds(2));
            native.PollError = new InvalidOperationException("fake poll interop failure");
            await native.AbortAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            engine.SetLoopPlayback(false);
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsFalse(task.IsCompleted);
            response.SetResult(new(ObservationState.Match, "late result after failed abort"));

            // Native timeouts remain authoritative when abort could not join.
            native.Result = PlaybackResult.WaitTimedOut;
            native.PollError = null;
            var error = await Assert.ThrowsExactlyAsync<AggregateException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
            StringAssert.Contains(error.Message, "native deadline");
            StringAssert.Contains(error.Message, "poll interop failure");
            StringAssert.Contains(error.Message, "abort interop failure");
            Assert.AreEqual(0, native.Resolves);
        }
        finally { native.AbortError = null; response.TrySetResult(new(ObservationState.Match, "released")); }
    }

    [TestMethod]
    public async Task NativeIndexUsesFinalSnapshotAndConditionIsClonedBeforeObservation()
    {
        var native = new Native { Index = 1 };
        var observed = new TaskCompletionSource<WaitCondition>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var engine = new PlaybackEngine(native, new Observer((condition, token) =>
        { observed.TrySetResult(condition); return ValueTask.FromResult(new WaitObservation(ObservationState.Match, "fake")); }));
        var wait = Wait();
        var task = engine.PlaybackEventsAsync([new MouseEvent(4, 5, MacroRecorderGUI.Common.MouseActionTypeFlags.Move), wait]);
        var changed = wait.Condition; changed.Window.Target.Title = "edited"; wait.SetCondition(changed);
        await task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("Export", (await observed.Task).Window.Target.Title);
        Assert.AreEqual(1, native.Starts); Assert.AreEqual(1, native.Resolves);
    }

    [TestMethod]
    public async Task CancelledObservationCannotResolveAfterAbortOrNewSession()
    {
        var native = new Native();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<WaitObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var engine = new PlaybackEngine(native, new Observer((condition, token) =>
        { Interlocked.Increment(ref calls); started.TrySetResult(); return new(response.Task); }));
        var first = engine.PlaybackEventsAsync([Wait()]);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        engine.PlaybackEventAbort(); await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        var second = engine.PlaybackEventsAsync([Wait()]);
        engine.PlaybackEventAbort(); await Assert.ThrowsAsync<OperationCanceledException>(() => second);
        response.SetResult(new(ObservationState.Match, "late"));
        await Task.Delay(30);
        Assert.AreEqual(0, native.Resolves); Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task AmbiguousTargetFailureReportsConditionAndStopsNativeSession()
    {
        var native = new Native();
        using var engine = new PlaybackEngine(native, new Observer((condition, token) =>
            ValueTask.FromResult(new WaitObservation(ObservationState.Error, "Ambiguous target: 2 windows"))));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PlaybackEventsAsync([Wait()]));
        StringAssert.Contains(error.Message, "event 1"); StringAssert.Contains(error.Message, "Export"); StringAssert.Contains(error.Message, "Ambiguous");
        Assert.AreEqual(1, native.Resolves);
    }

    [TestMethod]
    public async Task NativeDeadlineCompletesEngineEvenWhenObserverIgnoresCancellation()
    {
        var native = new Native(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<WaitObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var engine = new PlaybackEngine(native, new Observer((condition, token) => { started.TrySetResult(); return new(response.Task); }));
        var task = engine.PlaybackEventsAsync([Wait()]); await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        native.Result = PlaybackResult.WaitTimedOut;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
        StringAssert.Contains(error.Message, "native deadline"); Assert.AreEqual(0, native.Resolves);
        response.SetResult(new(ObservationState.Match, "late"));
    }
}
