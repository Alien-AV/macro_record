using System.Collections.ObjectModel;
using System.Windows.Input;
using MacroRecorderGUI.Event;

namespace MacroRecorderGUI.ViewModels.DesignTimeViewModels
{
    public class DesignTimeMainWindowViewModel : ViewModelBase, IMainWindowViewModel
    {
        public ObservableCollection<MacroViewModel> MacroTabs { get; set; }
        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { _selectedTabIndex = value; OnPropertyChanged();}
        }
        public MacroViewModel ActiveMacro => (SelectedTabIndex != -1)? MacroTabs[SelectedTabIndex] : null;

        public DesignTimeMainWindowViewModel()
        {
            MacroTabs = new ObservableCollection<MacroViewModel> { new MacroViewModel("DesignTimeDummyMacro", null), new MacroViewModel("DesignTimeDummyMacro2", null) };
            ActiveMacro.Events.Add(new KeyboardEvent(Key.A,false){TimeSinceLastEvent = 100});
            ActiveMacro.Events.Add(new KeyboardEvent(Key.A,true){TimeSinceLastEvent = 200});
            ActiveMacro.Events.Add(new MouseEvent(1,2,0x1){TimeSinceLastEvent = 300});
        }
    }
}
