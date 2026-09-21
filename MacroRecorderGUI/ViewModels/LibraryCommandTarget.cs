using MacroRecorderGUI.Views;

namespace MacroRecorderGUI.ViewModels;

/// <summary>Load the explicitly requested library document without changing editor ownership.</summary>
internal static class LibraryCommandTarget
{
    public static async Task<MacroViewModel> OpenAsync(MainWindowViewModel viewModel, Guid id, RunLease lease)
    {
        var previous = viewModel.ActiveMacro;
        var version = previous?.ChangeVersion;
        if (previous is { IsDirty: true } && (previous.ChangeVersion > 0 || previous.SavedAt is not null))
            await lease.PrepareAsync(() => viewModel.SaveRecordingAsync(previous, lease.Token));
        ValidatePrevious();
        MacroViewModel? target = null;
        await lease.PrepareAsync(async () => target = await viewModel.OpenRecordingAsync(id, select: false, lease.Token));
        ValidatePrevious();
        return target!;

        void ValidatePrevious()
        {
            lease.ThrowIfCancelled();
            if (previous is not null && (!viewModel.MacroTabs.Contains(previous) || previous.ChangeVersion != version))
                throw new InvalidOperationException("The open recording changed or closed while saving. Try again when ready.");
        }
    }
}
