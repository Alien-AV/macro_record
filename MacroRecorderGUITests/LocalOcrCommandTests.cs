using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using MacroRecorder.Waiting;
using ProtobufGenerated;

namespace MacroRecorderGUITests;

[TestClass]
[DoNotParallelize]
public sealed class LocalOcrCommandTests
{
    private sealed class Reader : TextReader
    {
        public readonly TaskCompletionSource<string> Content = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _position;
        public bool Disposed;
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken token = default)
        {
            var text = await Content.Task.WaitAsync(token);
            var count = Math.Min(buffer.Length, text.Length - _position);
            text.AsMemory(_position, count).CopyTo(buffer); _position += count;
            return count;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class Process : ILocalOcrProcess
    {
        public readonly Reader Stdout = new(), Stderr = new();
        public readonly MemoryStream Stdin = new();
        public readonly TaskCompletionSource Exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource TeardownWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource KillFailed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource DisposedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception Failure = new AggregateException(new Win32Exception(5, "Fake tree termination denial"));
        public int FailFromAttempt = 1;
        public int KillCalls;
        public bool Disposed;
        public bool Start() => true;
        public bool HasExited => Exited.Task.IsCompleted;
        public int ExitCode => 0;
        public Stream Input => Stdin;
        public TextReader Output => Stdout;
        public TextReader Error => Stderr;
        public void Kill()
        {
            if (Interlocked.Increment(ref KillCalls) < FailFromAttempt) return;
            KillFailed.TrySetResult(); throw Failure;
        }
        public Task WaitForExitAsync(CancellationToken token)
        {
            if (!token.CanBeCanceled) TeardownWait.TrySetResult();
            return Exited.Task.WaitAsync(token);
        }
        public void ReleaseAll()
        { Exited.TrySetResult(); Stdout.Content.TrySetResult(""); Stderr.Content.TrySetResult(""); }
        public void Dispose()
        { Disposed = true; Stdin.Dispose(); Stdout.Dispose(); Stderr.Dispose(); DisposedSignal.TrySetResult(); }
    }

    private sealed class Diagnostics : TraceListener
    {
        public readonly ConcurrentQueue<string> Lines = new();
        public override void Write(string? message) { if (message is not null) Lines.Enqueue(message); }
        public override void WriteLine(string? message) => Write(message);
    }

    private sealed class Observer(LocalOcrCommand command) : IWaitObserver, IAsyncDisposable
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed;
        public async ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken token)
        {
            var pending = command.RunAsync("fake.exe", "fake-data", "eng", [1], token);
            Started.TrySetResult();
            var result = await pending;
            return new(ObservationState.Match, result.Text);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class ReadyObserver : IWaitObserver
    {
        public ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken token) =>
            ValueTask.FromResult(new WaitObservation(ObservationState.Match, "fresh observation"));
    }

    [TestMethod]
    [DataRow(false, 1)] [DataRow(true, 1)] [DataRow(false, 2)] [DataRow(true, 2)]
    public async Task TerminationFailureCannotEscapeCancellationOrSkipProcessDrain(bool aggregate, int failFromAttempt)
    {
        var process = new Process { FailFromAttempt = failFromAttempt };
        if (!aggregate) process.Failure = new Win32Exception(5, "Fake process termination denial");
        process.Stdout.Content.SetResult(""); process.Stderr.Content.SetResult("");
        using var diagnostics = new Diagnostics(); Trace.Listeners.Add(diagnostics);
        using var cancellation = new CancellationTokenSource();
        var command = new LocalOcrCommand(_ => process);
        var pending = command.RunAsync("fake.exe", "fake-data", "eng", [1], cancellation.Token);
        try
        {
            cancellation.Cancel(); // Exercises the same synchronous callback boundary as a CancelAfter timer.
            await process.TeardownWait.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsFalse(pending.IsCompleted); Assert.IsFalse(process.Disposed);
            Assert.IsTrue(process.KillCalls >= 2);
            Assert.IsTrue(diagnostics.Lines.Any(line => line.Contains("retaining the observation permit")));
            Assert.IsTrue(diagnostics.Lines.Any(line => line.Contains("restart the app")));
            process.Exited.SetResult();
            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.AreSame(process.Failure, error.InnerException);
            StringAssert.Contains(error.Message, "process permissions before retrying");
            Assert.IsTrue(process.Disposed); Assert.IsTrue(process.Stdout.Disposed); Assert.IsTrue(process.Stderr.Disposed);
        }
        finally
        {
            process.ReleaseAll();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            Trace.Listeners.Remove(diagnostics);
        }
    }

    [TestMethod]
    public async Task CancelledOcrRetainsPermitUntilExitAndBothPipesFinish()
    {
        var process = new Process(); var observer = new Observer(new(_ => process)); var creations = 0;
        var runner = new WaitRunner(_ => Interlocked.Increment(ref creations) == 1 ? observer : new ReadyObserver());
        var condition = WaitValidation.NewOcrText(); condition.StableForUs = 0;
        condition.OcrText.Predicate.Comparison = TextComparison.TextNotEquals;
        try
        {
            var run = runner.RunAsync(condition, TimeSpan.FromMilliseconds(100), default);
            await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsFalse((await run.WaitAsync(TimeSpan.FromSeconds(2))).Satisfied);
            await process.KillFailed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await StillOwned();
            process.Exited.SetResult();
            await StillOwned();
            process.Stdout.Content.SetResult("late text that cannot match a cancelled wait");
            await StillOwned();
            process.Stderr.Content.SetResult("");
            await process.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsTrue((await runner.RunAsync(condition, TimeSpan.FromSeconds(2), default)).Satisfied);
            Assert.IsTrue(observer.Disposed); Assert.AreEqual(2, creations);
        }
        finally { process.ReleaseAll(); await process.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(2)); }

        async Task StillOwned()
        {
            Assert.IsFalse((await runner.RunAsync(condition, TimeSpan.FromMilliseconds(50), default)).Satisfied);
            Assert.AreEqual(1, creations); Assert.IsFalse(process.Disposed); Assert.IsFalse(observer.Disposed);
        }
    }

    [TestMethod]
    public async Task OutputOverflowTerminationFailureStillDrainsOtherPipeAndProcess()
    {
        var process = new Process(); process.Stdout.Content.SetResult(new string('x', TextPredicates.MaximumCharacters + 3));
        var pending = new LocalOcrCommand(_ => process).RunAsync("fake.exe", "fake-data", "eng", [1], default);
        try
        {
            await process.KillFailed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsFalse(pending.IsCompleted); Assert.IsFalse(process.Disposed);
            process.Exited.SetResult();
            Assert.IsFalse(pending.IsCompleted); Assert.IsFalse(process.Disposed);
            process.Stderr.Content.SetResult("");
            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.AreSame(process.Failure, error.InnerException); Assert.IsTrue(process.Disposed);
        }
        finally
        {
            process.ReleaseAll();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
    }

    [TestMethod]
    public async Task SuccessfulCommandUsesHiddenLocalProcessAndClosesAllResources()
    {
        var process = new Process(); process.Stdout.Content.SetResult("Ready\n"); process.Stderr.Content.SetResult(""); process.Exited.SetResult();
        var command = new LocalOcrCommand(start =>
        {
            Assert.IsFalse(start.UseShellExecute); Assert.IsTrue(start.CreateNoWindow);
            Assert.IsTrue(start.RedirectStandardInput && start.RedirectStandardOutput && start.RedirectStandardError);
            Assert.AreEqual("1", start.Environment["OMP_THREAD_LIMIT"]);
            CollectionAssert.AreEqual(new[] { "stdin", "stdout", "--tessdata-dir", "fake-data", "-l", "eng", "--psm", "6" }, start.ArgumentList.ToArray());
            return process;
        });
        var result = await command.RunAsync("fake.exe", "fake-data", "eng", [1, 2, 3], default);
        Assert.AreEqual(new OcrCommandResult(0, "Ready\n", ""), result);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, process.Stdin.ToArray());
        Assert.AreEqual(0, process.KillCalls); Assert.IsTrue(process.Disposed);
    }
}
