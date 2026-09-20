using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using MacroRecorderGUI.Models;

namespace MacroRecorderGUI.Utils;

public static class FileOperations
{
    public static async Task<string?> PickExportPathAsync(string name, WindowId windowId)
    {
        var picker = new FileSavePicker(windowId) { SuggestedFileName = Path.GetFileNameWithoutExtension(name) };
        picker.FileTypeChoices.Add("Macro files", new List<string> { ".macro" });
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public static async Task<string?> PickImportPathAsync(WindowId windowId)
    {
        var picker = new FileOpenPicker(windowId);
        picker.FileTypeFilter.Add(".macro");
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static async Task<byte[]> ReadMacroBytesAsync(string path)
    {
        if (new FileInfo(path).Length > RecordingLibraryStore.MaximumMacroBytes)
            throw new InvalidDataException("Macros must be 64 MB or smaller.");
        var bytes = await File.ReadAllBytesAsync(path);
        _ = SerializeEvents.DeserializeEventsFromByteArray(bytes).ToArray();
        return bytes;
    }

    public static Task WriteMacroBytesAsync(string path, byte[] bytes) => AtomicFile.WriteAsync(path, bytes);
}
