using MacroRecorder.Waiting;
using ProtobufGenerated;

namespace MacroRecorderGUI.Models;

internal sealed record WaitSourceChoices<T>(ReadStatus Status, IReadOnlyList<T> Items, string Detail = "", bool IsComplete = true);
internal sealed record WaitProcessChoice(string Label, string ExecutablePath, uint ProcessId = 0, ulong ProcessCreated = 0);
internal sealed record WaitModuleChoice(string Label, string Path, string FileVersion);

/// <summary>Explicit authoring queries. Implementations must not inspect during construction.</summary>
internal interface IWaitSourceQueries
{
    Task<WaitSourceChoices<AccessibilityChoice>> AccessibilityAsync(WindowSelector target, CancellationToken token);
    Task<WaitSourceChoices<WaitProcessChoice>> ProcessesAsync(CancellationToken token);
    Task<WaitSourceChoices<WaitModuleChoice>> ModulesAsync(WaitProcessChoice process, CancellationToken token);
    Task<WaitSourceChoices<string>> LanguagesAsync(CancellationToken token);
}
