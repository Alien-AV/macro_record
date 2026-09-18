using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using MacroRecorderGUI.Event;

namespace MacroRecorderGUI.Utils;

internal static class FileOperations
{
    internal sealed record LoadedMacro(string Name, IReadOnlyList<InputEvent> Events);

    internal static async Task<string?> SaveEventsToFileAsync(
        IEnumerable<InputEvent> inputEvents,
        string name,
        WindowId windowId)
    {
        var savePicker = new FileSavePicker(windowId)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(name)
        };
        savePicker.FileTypeChoices.Add("Macro files", new List<string> { ".macro" });

        var file = await savePicker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        var serializedEvents = SerializeEvents.SerializeEventsToByteArray(inputEvents);
        await File.WriteAllBytesAsync(file.Path, serializedEvents);
        return Path.GetFileName(file.Path);
    }

    internal static async Task<LoadedMacro?> LoadEventsFromFileAsync(WindowId windowId)
    {
        var openPicker = new FileOpenPicker(windowId);
        openPicker.FileTypeFilter.Add(".macro");
        openPicker.FileTypeFilter.Add("*");

        var file = await openPicker.PickSingleFileAsync();
        if (file is null)
        {
            return null;
        }

        var serializedEvents = await File.ReadAllBytesAsync(file.Path);
        var events = SerializeEvents.DeserializeEventsFromByteArray(serializedEvents).ToList();
        return new LoadedMacro(Path.GetFileName(file.Path), events);
    }
}
