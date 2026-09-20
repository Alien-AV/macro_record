using MacroRecorderGUI.Models;
using MacroRecorderGUI.ViewModels;

namespace MacroRecorderGUI.Views;

internal sealed record RecordingRun(MacroViewModel Macro, RecordingOptions Options, bool Clear);
internal sealed record PlaybackRun(MacroViewModel Macro, PlaybackOptions Options);

/// <summary>The caller acquires its run lease synchronously, before any preparation can yield.</summary>
internal static class DirectRunPreparation
{
    public static async Task<RecordingRun?> RecordAsync(MainWindowViewModel viewModel, RunPreferences preferences, RunLease run,
        bool intoExisting, Func<string, Task<bool?>> chooseExistingMode)
    {
        var previous = viewModel.ActiveMacro;
        var existing = intoExisting ? previous ?? throw new InvalidOperationException("Open a recording first.") : null;
        var version = previous?.ChangeVersion;
        var snapshot = preferences.IsLoaded ? preferences.Recording : null;
        await run.PrepareAsync(() => preferences.InitializeAsync(run.Token));
        var options = snapshot ?? preferences.Recording;
        await run.PrepareAsync(viewModel.InitializeLibraryAsync);
        var clear = false;
        if (existing is not null)
        {
            ValidateTarget(viewModel, existing, version);
            bool? choice = null;
            await run.PrepareAsync(async () => choice = await chooseExistingMode(existing.Name));
            if (choice is null) return null;
            clear = choice.Value;
        }
        await run.PrepareAsync(() => SaveIfNeededAsync(viewModel, previous, run.Token));
        if (previous is not null) ValidateTarget(viewModel, previous, version);
        MacroViewModel? target = existing;
        if (target is null)
            await run.PrepareAsync(async () => target = await viewModel.CreateDraftAsync(viewModel.NewRecordingName(), run.Token));
        return new(target!, options, clear);
    }

    public static async Task<PlaybackRun> PlayAsync(MainWindowViewModel viewModel, RunPreferences preferences, RunLease run,
        MacroViewModel macro)
    {
        var id = macro.RecordingId;
        var version = macro.ChangeVersion;
        var snapshot = preferences.IsLoaded ? preferences.PlaybackFor(id) : null;
        await run.PrepareAsync(() => preferences.InitializeAsync(run.Token));
        var options = snapshot ?? preferences.PlaybackFor(id);
        await run.PrepareAsync(() => SaveIfNeededAsync(viewModel, macro, run.Token));
        ValidateTarget(viewModel, macro, version);
        if (macro.RecordingId != id) throw new InvalidOperationException("The requested recording changed while preparing playback.");
        return new(macro, options);
    }

    public static async Task EditRecordingOptionsAsync(RunPreferences preferences, RunLease lease,
        Func<RecordingOptions, Task<RecordingOptions?>> showDialog)
    {
        await lease.PrepareAsync(() => preferences.InitializeAsync(lease.Token));
        RecordingOptions? choice = null;
        await lease.PrepareAsync(async () => choice = await showDialog(preferences.Recording));
        if (choice is not null) await lease.PrepareAsync(() => preferences.SaveRecordingAsync(choice, lease.Token));
    }

    public static async Task EditPlaybackOptionsAsync(RunPreferences preferences, RunLease lease, Guid id,
        Func<PlaybackOptions, Task<PlaybackOptions?>> showDialog)
    {
        await lease.PrepareAsync(() => preferences.InitializeAsync(lease.Token));
        PlaybackOptions? choice = null;
        await lease.PrepareAsync(async () => choice = await showDialog(preferences.PlaybackFor(id)));
        if (choice is not null) await lease.PrepareAsync(() => preferences.SavePlaybackAsync(id, choice, lease.Token));
    }

    private static Task SaveIfNeededAsync(MainWindowViewModel viewModel, MacroViewModel? macro, CancellationToken cancellationToken)
        => macro is { IsDirty: true } && (macro.ChangeVersion > 0 || macro.SavedAt is not null)
            ? viewModel.SaveRecordingAsync(macro, cancellationToken) : Task.CompletedTask;

    private static void ValidateTarget(MainWindowViewModel viewModel, MacroViewModel macro, long? version)
    {
        if (!viewModel.MacroTabs.Contains(macro) || macro.ChangeVersion != version)
            throw new InvalidOperationException("The recording changed or closed while preparing. Start again when ready.");
    }
}
