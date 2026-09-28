using ProtobufGenerated;

namespace MacroRecorder.Waiting;

internal sealed class AccessibilityTextObserver(IWindowPixelDesktop desktop, IAccessibilityTextBackend backend) : IWaitObserver, IAsyncDisposable
{
    private string? _windowIdentity;
    public async ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken cancellationToken)
    {
        var definition = condition.AccessibilityText ?? throw new ArgumentException("Accessibility condition is required.");
        var found = desktop.FindWindows(definition.Target, cancellationToken);
        if (found.Failure is { } failure) return new(failure, found.Detail);
        if (found.Windows.Count == 0) return new(ObservationState.Unavailable, "Target window is unavailable.");
        if (found.Windows.Count != 1) return new(ObservationState.Error, "Accessibility target is ambiguous.");
        var window = found.Windows[0];
        _windowIdentity ??= window.Identity;
        if (_windowIdentity != window.Identity) return new(ObservationState.Error, "The bound accessibility window changed.");
        var read = await backend.ReadAsync(new(window.Handle, window.ProcessId, window.ProcessCreated), definition, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return TextPredicates.Observe(read, definition.Predicate);
    }
    public ValueTask DisposeAsync() => backend.DisposeAsync();
}
