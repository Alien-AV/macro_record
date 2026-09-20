using MacroRecorderGUI.Event;
using MacroRecorderGUI.Models;
using MacroRecorderGUI.Utils;

namespace MacroRecorderGUI.ViewModels;

public partial class MainWindowViewModel
{
    internal async Task<(IReadOnlyList<InputEvent> Events, IReadOnlyList<PointerOriginBoundary> Origins)> LoadRecordingPreviewAsync(Guid id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var record = await _libraryStore.LoadAsync(id);
        var document = RecordingDocument.Read(record.MacroBytes);
        return (document.ParseEvents().InputEvents.Select(InputEvent.CreateInputEvent).ToArray(), document.Origins);
    }

    internal Task ExportLegacyRecordingAsync(MacroViewModel macro, string path)
    {
        EnsureLibraryWritable();
        var bytes = RecordingDocument.Read(macro.SnapshotBytes()).ExportLegacy();
        return FileOperations.WriteMacroBytesAsync(path, bytes);
    }
}
