using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;

namespace MacroRecorderGUI
{
    internal class FileOperations
    {
        internal static void SaveEventsToFile(IEnumerable<InputEvent> inputEventList)
        {
            var saveFileDialog = new SaveFileDialog();
            if (saveFileDialog.ShowDialog() != true) return;

            var serializedEvents = MainWindow.SerializeEventsToByteArray(inputEventList);
            File.WriteAllBytes(saveFileDialog.FileName, serializedEvents);
        }

        internal static IEnumerable<InputEvent> LoadEventsFromFile()
        {
            var openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() != true) return null;

            var serializedEvents = File.ReadAllBytes(openFileDialog.FileName);
            var deserializedEvents = MainWindow.DeserializeEventsFromByteArray(serializedEvents);
            return deserializedEvents;
        }
    }
}
