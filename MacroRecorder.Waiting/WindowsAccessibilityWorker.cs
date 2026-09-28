namespace MacroRecorder.Waiting;

/// <summary>
/// One lazy, process-wide MTA with at most one accepted request, including cleanup. A hung provider
/// cannot cause replacement threads or an accumulating queue, even for explicit picker requests.
/// Cancellation is observed between native calls; it never abandons an in-use COM object.
/// </summary>
internal sealed class WindowsAccessibilityWorker
{
    private static readonly Lazy<WindowsAccessibilityWorker> Shared = new(() => new());
    private readonly object _gate = new();
    private readonly AutoResetEvent _ready = new(false);
    private Action? _work;
    private bool _busy;

    private WindowsAccessibilityWorker()
    {
        var thread = new Thread(Loop) { IsBackground = true, Name = "MacroRecorder accessibility" };
        thread.SetApartmentState(ApartmentState.MTA);
        try { thread.Start(); }
        catch { _ready.Dispose(); throw; }
    }

    internal static Task<T> Run<T>(Func<T> operation, Func<T> busy) => Shared.Value.Schedule(operation, busy);

    private Task<T> Schedule<T>(Func<T> operation, Func<T> busy)
    {
        lock (_gate)
        {
            if (_busy) return Task.FromResult(busy());
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _busy = true;
            _work = () =>
            {
                T? value = default;
                Exception? failure = null;
                try { value = operation(); } catch (Exception error) { failure = error; }
                lock (_gate) _busy = false;
                if (failure is OperationCanceledException cancelled) completion.SetCanceled(cancelled.CancellationToken);
                else if (failure is not null) completion.SetException(failure);
                else completion.SetResult(value!);
            };
            _ready.Set();
            return completion.Task;
        }
    }

    private void Loop()
    {
        while (true)
        {
            _ready.WaitOne();
            Action? work;
            lock (_gate) { work = _work; _work = null; }
            work?.Invoke();
        }
    }
}
