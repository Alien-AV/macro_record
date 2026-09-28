using ProtobufGenerated;

namespace MacroRecorder.Waiting;

public static partial class WaitServices
{
    internal static IWaitObserver CreateObserver(WaitCondition condition) => condition.ConditionCase switch
    {
        WaitCondition.ConditionOneofCase.Window or WaitCondition.ConditionOneofCase.Pixel => new WindowPixelObserver(new WindowsWaitDesktop()),
        WaitCondition.ConditionOneofCase.Memory => new MemoryObserver(new WindowsMemoryProcessApi(), () => LocalSettings.MemoryEnabled),
        WaitCondition.ConditionOneofCase.AccessibilityText => new AccessibilityTextObserver(new WindowsWaitDesktop(), new WindowsAccessibilityTextBackend()),
        WaitCondition.ConditionOneofCase.OcrText => new OcrTextObserver(new WindowsWaitDesktop(), new WindowsOcrCapture(), new TesseractOcrBackend(EffectiveOcrOptions())),
        _ => throw new ArgumentException("Unsupported wait provider.")
    };

    internal static WaitLocalOptions EffectiveOcrOptions()
    {
        var options = LocalSettings.Options;
        return options with
        {
            TesseractExecutablePath = options.TesseractExecutablePath.Length > 0 ? options.TesseractExecutablePath : Path.Combine(AppContext.BaseDirectory, "ocr", "tesseract.exe"),
            TessdataDirectory = options.TessdataDirectory.Length > 0 ? options.TessdataDirectory : Path.Combine(AppContext.BaseDirectory, "ocr", "tessdata")
        };
    }

    public static Task<ChoiceResult<ProcessChoice>> GetProcessesAsync(CancellationToken token = default) =>
        ReadChoicesAsync(cancellation => WindowsMemoryProcessApi.List(() => LocalSettings.MemoryEnabled, cancellation), token);

    public static Task<ChoiceResult<ModuleChoice>> GetModulesAsync(ProcessChoice process, CancellationToken token = default) =>
        ReadChoicesAsync<ModuleChoice>(cancellation =>
        {
            if (!LocalSettings.MemoryEnabled) return new(ReadStatus.Error, [], "Memory observation is disabled locally.", false);
            using var bound = WindowsMemoryProcess.Open(process.ProcessId);
            if (bound.Identity != process) return new(ReadStatus.Error, [], "Process identity changed; choose the process again.", false);
            var modules = bound.Modules(cancellation);
            if (!LocalSettings.MemoryEnabled) return new(ReadStatus.Error, [], "Memory permission was revoked.", false);
            return new(ReadStatus.Success, modules);
        }, token);

    private static async Task<ChoiceResult<T>> ReadChoicesAsync<T>(Func<CancellationToken, ChoiceResult<T>> read, CancellationToken token)
    {
        try
        {
            return await WaitRunner.Desktop.ExecuteExclusiveAsync(cancellation => Task.FromResult(read(cancellation)), TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(ReadStatus.Unavailable, [], "Choice enumeration timed out.", false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return new(ReadStatus.Error, [], error.Message, false); }
    }

    public static Task<ChoiceResult<string>> GetOcrLanguagesAsync(CancellationToken token = default) =>
        ReadChoicesAsync(cancellation => TesseractOcrBackend.GetLanguages(EffectiveOcrOptions(), cancellation), token);

    public static async Task<AccessibilityChoicesResult> GetAccessibilityChoicesAsync(WindowSelector selector, CancellationToken token = default)
    {
        var snapshot = selector.Clone();
        var validation = WaitValidation.NewAccessibilityText();
        validation.AccessibilityText.Target = snapshot;
        validation.AccessibilityText.Element.ControlType = 50000;
        try
        {
            WaitValidation.Validate(validation);
            return await WaitRunner.Desktop.ExecuteExclusiveAsync(async cancellation =>
            {
                var found = new WindowsWaitDesktop().FindWindows(snapshot, cancellation);
                if (found.Failure is { } failure) return new AccessibilityChoicesResult(failure == ObservationState.Error ? ReadStatus.Error : ReadStatus.Unavailable, [], found.Detail);
                if (found.Windows.Count != 1) return new(ReadStatus.Unavailable, [], "Choose exactly one available window.");
                var window = found.Windows[0];
                await using var backend = new WindowsAccessibilityTextBackend();
                return await backend.GetChoicesAsync(new(window.Handle, window.ProcessId, window.ProcessCreated), cancellation).ConfigureAwait(false);
            }, TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(ReadStatus.Unavailable, [], "Accessibility choices timed out."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return new(ReadStatus.Error, [], error.Message); }
    }
}
