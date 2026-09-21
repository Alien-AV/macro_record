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
        public PlaybackResult Start(byte[] events, bool loop, out ulong sessionId)
        {
            Result = PlaybackResult.Running; Starts++; sessionId = ++Id;
            Request = new() { Occurrence = 7, EventIndex = Index, RemainingUs = 1_000_000 };
            return Result;
        }
        public PlaybackResult Poll(ulong id) => Result;
        public PlaybackResult Abort(ulong id) => Result = PlaybackResult.Cancelled;
        public PlaybackResult SetLoop(ulong id, bool loop) => Result;
        public PlaybackResult WaitRequest(ulong id, out NativeWaitRequest request) { request = Request; return Result; }
        public PlaybackResult ResolveWait(ulong id, ulong occurrence, bool satisfied)
        {
            Assert.AreEqual(Id, id); Assert.AreEqual(Request.Occurrence, occurrence);
            Resolves++; Result = satisfied ? PlaybackResult.Finished : PlaybackResult.WaitFailed;
            return PlaybackResult.Running;
        }
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
