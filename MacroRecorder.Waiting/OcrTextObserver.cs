using ProtobufGenerated;

namespace MacroRecorder.Waiting;

internal sealed record OcrFrame(int Width, int Height, byte[] Bgra, string Identity);
internal sealed record OcrCaptureResult(ReadStatus Status, OcrFrame? Frame = null, string Detail = "");
internal interface IOcrCapture { OcrCaptureResult Capture(OcrRegion region, ObservedWindow? window, CancellationToken token); }
internal interface IOcrBackend { ValueTask<TextReadResult> ReadAsync(OcrFrame frame, string language, CancellationToken token); }

internal sealed class OcrTextObserver(IWindowPixelDesktop desktop, IOcrCapture capture, IOcrBackend backend) : IWaitObserver, IAsyncDisposable
{
    private string? _identity;
    public async ValueTask<WaitObservation> ObserveAsync(WaitCondition condition, CancellationToken cancellationToken)
    {
        var ocr = condition.OcrText ?? throw new ArgumentException("OCR condition is required.");
        cancellationToken.ThrowIfCancellationRequested();
        ObservedWindow? window = null;
        if (ocr.Region.Target is { } target)
        {
            var found = desktop.FindWindows(target, cancellationToken);
            if (found.Failure is { } failure) return new(failure, found.Detail);
            if (found.Windows.Count == 0) return new(ObservationState.Unavailable, "OCR target window is unavailable.");
            if (found.Windows.Count != 1) return new(ObservationState.Error, "OCR target window is ambiguous.");
            window = found.Windows[0];
        }
        var sample = capture.Capture(ocr.Region, window, cancellationToken);
        if (sample.Status != ReadStatus.Success || sample.Frame is null)
            return new(sample.Status == ReadStatus.Error ? ObservationState.Error : ObservationState.Unavailable, sample.Detail);
        _identity ??= sample.Frame.Identity;
        if (_identity != sample.Frame.Identity) return new(ObservationState.Error, "The bound OCR target changed.");
        var result = await backend.ReadAsync(sample.Frame, ocr.Language, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return TextPredicates.Observe(result, ocr.Predicate);
    }
    public ValueTask DisposeAsync() => backend is IAsyncDisposable disposable ? disposable.DisposeAsync() : ValueTask.CompletedTask;
}
