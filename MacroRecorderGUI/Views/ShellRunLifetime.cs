namespace MacroRecorderGUI.Views;

/// <summary>Owns each requested run through its countdown and asynchronous completion.</summary>
internal sealed class ShellRunLifetime : IDisposable
{
    private RunLease? _current;
    private bool _closed;
    public RunLease Begin()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_current is not null) throw new InvalidOperationException("A run is already active.");
        return _current = new RunLease();
    }
    public bool Owns(RunLease lease) => !_closed && ReferenceEquals(_current, lease);
    public void Cancel() => _current?.Cancel();
    public bool Complete(RunLease lease)
    {
        if (!ReferenceEquals(_current, lease)) return false;
        _current = null;
        lease.Dispose();
        return true;
    }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _current?.Cancel();
        _current?.Dispose();
        _current = null;
    }
}

internal sealed class RunLease : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    public CancellationToken Token { get; }
    public RunLease() => Token = _cancellation.Token;
    public void Cancel() => _cancellation.Cancel();
    public void ThrowIfCancelled() => Token.ThrowIfCancellationRequested();
    public async Task PrepareAsync(Func<Task> operation)
    {
        ThrowIfCancelled();
        await operation();
        ThrowIfCancelled();
    }
    public void Dispose() => _cancellation.Dispose();
}
