using MacroRecorder.Waiting;
using ProtobufGenerated;

namespace MacroRecorderGUI.Models;

/// <summary>Presentation adapter for the library's explicit, bounded authoring APIs.</summary>
internal sealed class WindowsWaitSourceQueries : IWaitSourceQueries
{
    public async Task<WaitSourceChoices<AccessibilityChoice>> AccessibilityAsync(WindowSelector target, CancellationToken token)
    {
        var result = await WaitServices.GetAccessibilityChoicesAsync(target, token).ConfigureAwait(false);
        return new(result.Status, result.Choices, result.Detail);
    }

    public async Task<WaitSourceChoices<WaitProcessChoice>> ProcessesAsync(CancellationToken token)
    {
        var result = await WaitServices.GetProcessesAsync(token).ConfigureAwait(false);
        return new(result.Status, result.Choices.Select(process => new WaitProcessChoice(
            $"{process.ExecutablePath} · PID {process.ProcessId}", process.ExecutablePath, process.ProcessId, process.ProcessCreated)).ToArray(), result.Detail, result.IsComplete);
    }

    public async Task<WaitSourceChoices<WaitModuleChoice>> ModulesAsync(WaitProcessChoice process, CancellationToken token)
    {
        var result = await WaitServices.GetModulesAsync(new(process.ProcessId, process.ProcessCreated, process.ExecutablePath), token).ConfigureAwait(false);
        return new(result.Status, result.Choices.Select(module => new WaitModuleChoice(
            $"{module.Path} · {module.FileVersion} · base 0x{module.BaseAddress:X} · {module.Size} bytes", module.Path, module.FileVersion)).ToArray(), result.Detail, result.IsComplete);
    }

    public async Task<WaitSourceChoices<string>> LanguagesAsync(CancellationToken token)
    {
        var result = await WaitServices.GetOcrLanguagesAsync(token).ConfigureAwait(false);
        return new(result.Status, result.Choices, result.Detail, result.IsComplete);
    }
}
