using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class WaitProviderLifetimeTests
{
    private sealed class Observer : IWaitObserver, IAsyncDisposable
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<WaitObservation> Read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Disposing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource DisposeFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? OnCancel;
        public bool ThrowOnDispose;
        public Action? DuringDispose;
        public int DisposeCalls;
        public async ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken token)
        {
            token.Register(() => OnCancel?.Invoke()); Started.TrySetResult();
            return await Read.Task;
        }
        public async ValueTask DisposeAsync()
        {
            DisposeCalls++; Disposing.TrySetResult();
            DuringDispose?.Invoke();
            if (ThrowOnDispose) throw new IOException("fake disposal failure");
            await DisposeFinished.Task;
        }
    }

    [TestMethod]
    public async Task TimeoutReturnsButReadCallbacksAndDisposalAllRetainPermit()
    {
        using var callbackGate = new ManualResetEventSlim();
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer { OnCancel = () => { callbackStarted.TrySetResult(); callbackGate.Wait(); } };
        var creations = 0; var runner = new WaitRunner(_ => { creations++; return observer; });
        var condition = MemoryWaitBackendTests.Condition();
        var run = runner.RunAsync(condition, TimeSpan.FromMilliseconds(100), default);
        await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse((await run.WaitAsync(TimeSpan.FromSeconds(2))).Satisfied);
        await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        observer.Read.TrySetResult(new(ObservationState.Match, "late"));
        Assert.IsFalse((await runner.RunAsync(condition, TimeSpan.FromMilliseconds(50), default)).Satisfied);
        Assert.AreEqual(0, observer.DisposeCalls); Assert.AreEqual(1, creations);
        callbackGate.Set();
        await observer.Disposing.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse((await runner.RunAsync(condition, TimeSpan.FromMilliseconds(50), default)).Satisfied);
        Assert.AreEqual(1, creations);
        observer.DisposeFinished.TrySetResult();
    }

    [TestMethod]
    public async Task FailedDisposalRetainsPermitAfterSuccess()
    {
        var observer = new Observer { ThrowOnDispose = true };
        observer.Read.TrySetResult(new(ObservationState.Match, "ready"));
        var creations = 0; var runner = new WaitRunner(_ => { creations++; return observer; });
        Assert.IsTrue((await runner.RunAsync(MemoryWaitBackendTests.Condition(), TimeSpan.FromSeconds(1), default)).Satisfied);
        await observer.Disposing.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse((await runner.RunAsync(MemoryWaitBackendTests.Condition(), TimeSpan.FromMilliseconds(50), default)).Satisfied);
        Assert.AreEqual(1, creations); Assert.AreEqual(1, observer.DisposeCalls);
    }

    [TestMethod]
    public async Task ExplicitChoiceReadsCannotAccumulateAfterCallerCancellation()
    {
        var observer = new Observer(); var runner = new WaitRunner(observer);
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = runner.ExecuteExclusiveAsync(_ => { started.TrySetResult(); return pending.Task; }, TimeSpan.FromMilliseconds(100), default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        var calls = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.ExecuteExclusiveAsync(_ => { calls++; return Task.FromResult(2); }, TimeSpan.FromMilliseconds(50), default));
        Assert.AreEqual(0, calls);
        pending.TrySetResult(1);
    }

    [TestMethod]
    public async Task SynchronouslyBlockedDisposalDoesNotDelayTerminalResultOrReleasePermit()
    {
        using var gate = new ManualResetEventSlim();
        var observer = new Observer { DuringDispose = () => gate.Wait() };
        observer.Read.TrySetResult(new(ObservationState.Match, "ready")); observer.DisposeFinished.TrySetResult();
        var runner = new WaitRunner(_ => observer);
        try
        {
            Assert.IsTrue((await runner.RunAsync(MemoryWaitBackendTests.Condition(), TimeSpan.FromSeconds(1), default).WaitAsync(TimeSpan.FromSeconds(2))).Satisfied);
            await observer.Disposing.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsFalse((await runner.RunAsync(MemoryWaitBackendTests.Condition(), TimeSpan.FromMilliseconds(50), default)).Satisfied);
            Assert.AreEqual(1, observer.DisposeCalls);
        }
        finally { gate.Set(); }
    }
}
