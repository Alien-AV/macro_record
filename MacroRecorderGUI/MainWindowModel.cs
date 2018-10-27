using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MacroRecorderGUI.Utils;
using ProtobufGenerated;

namespace MacroRecorderGUI
{
    public class MainWindowModel
    {
//        private static readonly Lazy<MainWindowModel> LazyInstance = new Lazy<MainWindowModel>(() => new MainWindowModel());
//        public static MainWindowModel Instance => LazyInstance.Value;
        public MainWindowModel()
        {
            MacroTabs = new ObservableCollection<MacroTab> {new MacroTab(MainWindow._currentMacro)};
        }

        public class MacroTab
        {
            public Macro Macro { get; }

            public MacroTab(Macro macro)
            {
                Macro = macro;
            }

            public string Name { get; set; } = "name";
        }
        public ObservableCollection<MacroTab> MacroTabs { get; set; }
    }
}