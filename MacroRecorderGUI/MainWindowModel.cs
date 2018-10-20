using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using MacroRecorderGUI.Utils;

namespace MacroRecorderGUI
{
    internal sealed class MainWindowModel
    {
        private static readonly Lazy<MainWindowModel> LazyInstance =
            new Lazy<MainWindowModel>(() => new MainWindowModel());

        public static MainWindowModel Instance => LazyInstance.Value;

        private MainWindowModel()
        {
        }
    }
}