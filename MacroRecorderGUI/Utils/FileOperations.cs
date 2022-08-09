using System.Collections.Generic;
using System.IO;
using MacroRecorderGUI.Event;
using MacroRecorderGUI.ViewModels;
using Microsoft.Win32;

namespace MacroRecorderGUI.Utils
{
    internal class FileOperations
    {
        private const string MacroFilesFilter = "Macro Files (*.macro)|*.macro|All Files|*.*";

        internal static string SaveEventsToFile(IEnumerable<InputEvent> inputEventList, string name)
        {
            var saveFileDialog = new SaveFileDialog
            {
                FileName = name,
                Filter = MacroFilesFilter,
            };
            if (saveFileDialog.ShowDialog() != true) return null;

            var serializedEvents = SerializeEvents.SerializeEventsToByteArray(inputEventList);
            File.WriteAllBytes(saveFileDialog.FileName, serializedEvents);
            return saveFileDialog.SafeFileName;
        }

        internal static IEnumerable<InputEvent> LoadEventsFromFile(out string name)
        {

            var openFileDialog = new OpenFileDialog()
            {
                Filter = MacroFilesFilter,
            };
            if (openFileDialog.ShowDialog() != true)
            {
                name = null;
                return null;
            }

            var serializedEvents = File.ReadAllBytes(openFileDialog.FileName);
            var deserializedEvents = SerializeEvents.DeserializeEventsFromByteArray(serializedEvents);
            name = openFileDialog.SafeFileName;
            return deserializedEvents;
        }
    }
}
