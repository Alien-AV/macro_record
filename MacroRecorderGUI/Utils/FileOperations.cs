using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using MacroRecorderGUI.Event;

namespace MacroRecorderGUI.Utils;

internal static class FileOperations
{
    internal sealed record LoadedMacro(string Name, IReadOnlyList<InputEvent> Events);

    internal static async Task<string?> SaveEventsToFileAsync(
        IEnumerable<InputEvent> inputEvents,
        string name,
        nint windowHandle)
    {
        var savePicker = new FileSavePicker
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(name)
        };
        savePicker.FileTypeChoices.Add("Macro files", new List<string> { ".macro" });
        WinRT.Interop.InitializeWithWindow.Initialize(savePicker, windowHandle);

        var file = await savePicker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        var serializedEvents = SerializeEvents.SerializeEventsToByteArray(inputEvents);
        await FileIO.WriteBytesAsync(file, serializedEvents);
        return file.Name;
    }

    internal static async Task<LoadedMacro?> LoadEventsFromFileAsync(nint windowHandle)
    {
        var openPicker = new FileOpenPicker();
        openPicker.FileTypeFilter.Add(".macro");
        openPicker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, windowHandle);

        var file = await openPicker.PickSingleFileAsync();
        if (file is null)
        {
            return null;
        }

        var buffer = await FileIO.ReadBufferAsync(file);
        var serializedEvents = new byte[buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer))
        {
            reader.ReadBytes(serializedEvents);
        }

        var events = SerializeEvents.DeserializeEventsFromByteArray(serializedEvents).ToList();
        return new LoadedMacro(file.Name, events);
    }
}
