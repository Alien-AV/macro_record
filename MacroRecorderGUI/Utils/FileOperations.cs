using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace MacroRecorderGUI.Utils
{
    internal class FileOperations
    {
        internal static void SaveEventsToFile(IEnumerable<ProtobufGenerated.InputEvent> inputEventList)
        {
            var saveFileDialog = new SaveFileDialog();
            if (saveFileDialog.ShowDialog() != true) return;

            var serializedEvents = Macro.SerializeEventsToByteArray(inputEventList);
            File.WriteAllBytes(saveFileDialog.FileName, serializedEvents);
        }

        internal static IEnumerable<ProtobufGenerated.InputEvent> LoadEventsFromFile()
        {
            var openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() != true) return null;

            var serializedEvents = File.ReadAllBytes(openFileDialog.FileName);
            var deserializedEvents = Macro.DeserializeEventsFromByteArray(serializedEvents);
            return deserializedEvents;
        }
    }
}
