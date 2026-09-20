using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;
using MacroRecorderGUI.ViewModels;
using Windows.System;

namespace MacroRecorderGUITests;

[TestClass]
public sealed class DesignedPlaybackWorkflowTests
{
    private static InputEvent[] Inputs() => [new KeyboardEvent(VirtualKey.A, false) { TimeSinceLastEvent = 1001 },
        new KeyboardEvent(VirtualKey.A, true) { TimeSinceLastEvent = 2000000 }];
    private static PlaybackOptions Options(int repeats = 1, double speed = 1) => new() { Countdown = TimeSpan.Zero, RepeatCount = repeats, Speed = speed };

    [TestMethod]
    public async Task CountdownCancellationNeverStartsNativePlayback()
    {
        var engine = new ControlledEngine();
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new PlaybackWorkflow(engine, (_, token) =>
        {
            delayStarted.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var task = workflow.PlayAsync(Inputs(), new());
        await delayStarted.Task;
        Assert.AreEqual(PlaybackPhase.Countdown, workflow.State.Phase);
        workflow.Abort();
        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        Assert.AreEqual(0, engine.Starts.Count);
        Assert.AreEqual(0, engine.Aborts);
        Assert.AreEqual(PlaybackPhase.Cancelled, workflow.State.Phase);
    }

    [TestMethod]
    public async Task EveryRepeatUsesSameScaledSnapshotWithoutMutatingOriginal()
    {
        var engine = new ControlledEngine();
        var workflow = new PlaybackWorkflow(engine);
        var inputs = Inputs();
        var before = SerializeEvents.SerializeEventsToByteArray(inputs);
        var task = workflow.PlayAsync(inputs, Options(3, 2));
        CollectionAssert.AreEqual(before, SerializeEvents.SerializeEventsToByteArray(inputs));
        inputs[0].TimeSinceLastEvent = 99999;
        for (var i = 0; i < 3; i++)
        {
            await engine.Started.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(501ul, engine.Starts[i][0].TimeSinceLastEvent);
            Assert.AreEqual(1000000ul, engine.Starts[i][1].TimeSinceLastEvent);
            Assert.IsFalse(engine.Loops[i]);
            engine.Completions[i].TrySetResult();
        }
        await task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(3, engine.Starts.Count);
        Assert.AreEqual(PlaybackPhase.Completed, workflow.State.Phase);
        Assert.AreEqual(3, workflow.State.CurrentRepeat);
    }

    [TestMethod]
    public async Task AbortAtBetweenRepeatBoundaryPreventsNextStart()
    {
        var engine = new ControlledEngine();
        var workflow = new PlaybackWorkflow(engine);
        workflow.StateChanged += state => { if (state.Phase == PlaybackPhase.BetweenRepeats) workflow.Abort(); };
        var task = workflow.PlayAsync(Inputs(), Options(5));
        engine.Completions[0].TrySetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(1, engine.Starts.Count);
        Assert.AreEqual(0, engine.Aborts);
    }

    [TestMethod]
    public async Task NativeFaultCannotStartAnotherRepeat()
    {
        var engine = new ControlledEngine();
        var workflow = new PlaybackWorkflow(engine);
        var task = workflow.PlayAsync(Inputs(), Options(5));
        engine.Completions[0].TrySetException(new InvalidOperationException("injection failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(1, engine.Starts.Count);
        Assert.AreEqual(PlaybackPhase.Failed, workflow.State.Phase);
        Assert.AreEqual("injection failed", workflow.State.Error);
        Assert.IsFalse(workflow.IsActive);
    }

    [TestMethod]
    public async Task ActiveAbortRetainsOwnershipWhenInteropFailsAndCanBeRetried()
    {
        var engine = new ControlledEngine { AbortError = new InvalidOperationException("abort interop failed") };
        var workflow = new PlaybackWorkflow(engine);
        var task = workflow.PlayAsync(Inputs(), Options(5));
        Assert.Throws<InvalidOperationException>(workflow.Abort);
        Assert.IsTrue(workflow.IsActive);
        Assert.AreEqual(PlaybackPhase.Stopping, workflow.State.Phase);
        Assert.Throws<InvalidOperationException>(() => workflow.PlayAsync(Inputs(), Options()));
        engine.AbortError = null;
        workflow.Abort();
        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        engine.Completions[0].TrySetResult();
        Assert.AreEqual(1, engine.Starts.Count);
    }

    [TestMethod]
    public async Task AbortCleanupFaultIsSynchronousAndRetainedInTask()
    {
        var engine = new ControlledEngine { AbortError = new PlaybackStoppedException("release failed") };
        var workflow = new PlaybackWorkflow(engine);
        var task = workflow.PlayAsync(Inputs(), Options(5));
        Assert.Throws<PlaybackStoppedException>(workflow.Abort);
        await Assert.ThrowsAsync<PlaybackStoppedException>(() => task);
        Assert.AreEqual(PlaybackPhase.Failed, workflow.State.Phase);
        engine.Completions[0].TrySetException(new PlaybackStoppedException("release failed"));
        Assert.AreEqual(1, engine.Starts.Count);
    }

    [TestMethod]
    public async Task ImmediateStartFaultIsObservedAndNeverRepeats()
    {
        var engine = new ControlledEngine { StartError = new InvalidOperationException("start failed") };
        var workflow = new PlaybackWorkflow(engine);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.PlayAsync(Inputs(), Options(5)));
        Assert.IsFalse(workflow.IsActive);
        Assert.AreEqual(PlaybackPhase.Failed, workflow.State.Phase);
    }

    [TestMethod]
    public void InvalidOptionsAndOverflowFailBeforeEngineStart()
    {
        var engine = new ControlledEngine();
        var workflow = new PlaybackWorkflow(engine);
        foreach (var options in new[] { Options(0), Options(1001), Options(speed: double.NaN), Options(speed: double.PositiveInfinity), Options(speed: 0),
            Options() with { Countdown = TimeSpan.FromSeconds(-1) }, Options() with { Countdown = TimeSpan.FromMinutes(2) } })
            Assert.Throws<ArgumentOutOfRangeException>(() => workflow.PlayAsync(Inputs(), options));
        var inputs = Inputs(); inputs[0].TimeSinceLastEvent = ulong.MaxValue;
        Assert.Throws<ArgumentOutOfRangeException>(() => workflow.PlayAsync(inputs, Options()));
        Assert.AreEqual(0, engine.Starts.Count);
    }

    [TestMethod]
    public async Task RecordingAndPlaybackAreExclusiveAndStopRegistrationGatesBoth()
    {
        var record = new FakeRecordEngine();
        var engine = new ControlledEngine();
        using var vm = new MainWindowViewModel(record, engine);
        vm.ActiveMacro!.AddEvent(Inputs()[0]);
        Assert.IsTrue(vm.StartRecording());
        await vm.PlayActiveMacro(Options());
        Assert.AreEqual(0, engine.Starts.Count);
        await vm.StopRecordingAsync();
        var play = vm.PlayActiveMacro(Options());
        Assert.IsFalse(vm.StartRecording(clear: true));
        Assert.AreEqual(1, vm.ActiveMacro.Events.Count);
        vm.AbortPlayback();
        await play;
        engine.Completions[0].TrySetResult();
        vm.SetEmergencyStopAvailability(false, "Registration failed");
        Assert.IsFalse(vm.StartRecording());
        await vm.PlayActiveMacro(Options());
        Assert.AreEqual(1, engine.Starts.Count);
    }

    [TestMethod]
    public async Task InlineCountdownCancellationCannotClearReplacementStartedByStateCallback()
    {
        var engine = new ControlledEngine();
        var workflow = new PlaybackWorkflow(engine, (_, token) =>
        {
            var source = new TaskCompletionSource();
            token.Register(() => source.TrySetCanceled());
            return source.Task;
        });
        Task? replacement = null;
        var phases = new List<PlaybackPhase>();
        var replace = true;
        workflow.StateChanged += state =>
        {
            phases.Add(state.Phase);
            if (state.Phase != PlaybackPhase.Cancelled || !replace) return;
            replace = false;
            replacement = workflow.PlayAsync(Inputs(), Options());
        };
        var original = workflow.PlayAsync(Inputs(), new());
        workflow.Abort();
        await Assert.ThrowsAsync<OperationCanceledException>(() => original);
        Assert.IsNotNull(replacement);
        Assert.IsFalse(replacement.IsCompleted);
        Assert.IsTrue(workflow.IsActive);
        Assert.AreEqual(PlaybackPhase.Playing, workflow.State.Phase);
        CollectionAssert.AreEqual(new[] { PlaybackPhase.Countdown, PlaybackPhase.Cancelled, PlaybackPhase.Playing }, phases);
        Assert.AreEqual(1, engine.Starts.Count);
        Assert.AreEqual(0, engine.Aborts);
        workflow.Abort();
        await Assert.ThrowsAsync<OperationCanceledException>(() => replacement);
        Assert.AreEqual(1, engine.Aborts);
        engine.Completions[0].TrySetResult();
    }

    [TestMethod]
    public async Task AbortInsidePlayingNotificationNeverStartsInput()
    {
        var engine = new ControlledEngine();
        var workflow = new PlaybackWorkflow(engine);
        workflow.StateChanged += state => { if (state.Phase == PlaybackPhase.Playing) workflow.Abort(); };
        await Assert.ThrowsAsync<OperationCanceledException>(() => workflow.PlayAsync(Inputs(), Options()));
        Assert.AreEqual(0, engine.Starts.Count);
        Assert.IsFalse(workflow.IsActive);
    }

    [TestMethod]
    public async Task ReentrantAbortFromStoppingWithDefaultDelayCannotClearReplacement()
    {
        var engine = new ControlledEngine();
        var workflow = new PlaybackWorkflow(engine);
        Task? replacement = null;
        var nestedAbort = false;
        var replace = true;
        workflow.StateChanged += state =>
        {
            if (state.Phase == PlaybackPhase.Stopping && !nestedAbort)
            {
                nestedAbort = true;
                workflow.Abort();
            }
            if (state.Phase == PlaybackPhase.Cancelled && replace)
            {
                replace = false;
                replacement = workflow.PlayAsync(Inputs(), Options());
            }
        };
        var original = workflow.PlayAsync(Inputs(), new() { Countdown = TimeSpan.FromSeconds(30) });
        workflow.Abort();
        await Assert.ThrowsAsync<OperationCanceledException>(() => original);
        Assert.IsNotNull(replacement);
        Assert.IsFalse(replacement.IsCompleted);
        Assert.IsTrue(workflow.IsActive);
        Assert.AreEqual(PlaybackPhase.Playing, workflow.State.Phase);
        Assert.AreEqual(1, engine.Starts.Count);
        Assert.AreEqual(0, engine.Aborts);
        workflow.Abort();
        await Assert.ThrowsAsync<OperationCanceledException>(() => replacement);
        engine.Completions[0].TrySetResult();
    }

    [TestMethod]
    public async Task FiniteRunCannotBeConvertedIntoNativeInfiniteLoop()
    {
        var engine = new FakePlaybackEngine { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var vm = new MainWindowViewModel(new FakeRecordEngine(), engine);
        vm.ActiveMacro!.AddEvent(Inputs()[0]);
        var play = vm.PlayActiveMacro(Options(3));
        vm.LoopPlayback = false;
        vm.LoopPlayback = true;
        Assert.AreEqual(0, engine.LoopUpdates);
        Assert.IsFalse(engine.Loop);
        Assert.IsFalse(vm.LoopPlayback);
        vm.AbortPlayback();
        engine.Pending.TrySetResult();
        await play;
        vm.LoopPlayback = true;
        Assert.AreEqual(1, engine.LoopUpdates);
        Assert.IsTrue(engine.Loop, "The existing parameterless API still supports its live loop control.");
        vm.LoopPlayback = false;
        Assert.IsFalse(engine.Loop);
    }

    [TestMethod]
    public async Task AbortReenteredDuringEngineStartRetainsOwnershipUntilCleanupSucceeds()
    {
        var engine = new ControlledEngine { AbortError = new InvalidOperationException("interop cleanup failed") };
        var workflow = new PlaybackWorkflow(engine);
        engine.OnStart = workflow.Abort;
        var task = workflow.PlayAsync(Inputs(), Options(3));
        Assert.IsTrue(workflow.IsActive);
        Assert.IsFalse(task.IsCompleted);
        Assert.AreEqual(PlaybackPhase.Stopping, workflow.State.Phase);
        Assert.Throws<InvalidOperationException>(() => workflow.PlayAsync(Inputs(), Options()));
        engine.AbortError = null;
        workflow.Abort();
        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        engine.Completions[0].TrySetResult();
        Assert.AreEqual(1, engine.Starts.Count);
    }

    private sealed class ControlledEngine : IPlaybackEngine
    {
        public List<InputEvent[]> Starts { get; } = [];
        public List<bool> Loops { get; } = [];
        public List<TaskCompletionSource> Completions { get; } = [];
        public SemaphoreSlim Started { get; } = new(0);
        public int Aborts { get; private set; }
        public Exception? AbortError { get; set; }
        public Exception? StartError { get; set; }
        public Action? OnStart { get; set; }
        public Task PlaybackEventsAsync(IEnumerable<InputEvent> events, bool loop = false)
        {
            if (StartError is not null) throw StartError;
            Starts.Add(events.Select(input => InputEvent.CreateInputEvent(input.OriginalProtobufInputEvent.Clone())).ToArray());
            Loops.Add(loop);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Completions.Add(completion);
            Started.Release();
            OnStart?.Invoke();
            return completion.Task;
        }
        public void PlaybackEventAbort() { Aborts++; if (AbortError is not null) throw AbortError; }
        public void SetLoopPlayback(bool loop) => throw new AssertFailedException("Finite workflows must not alter native looping.");
        public void Dispose() { foreach (var completion in Completions) completion.TrySetCanceled(); }
    }
}
