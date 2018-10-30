using System;
using System.Windows.Input;

namespace MacroRecorderGUI.Commands
{
    public class DelegateCommand : ICommand
    {
        private readonly Action _action;

        public DelegateCommand(Action action) => _action = action;

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => _action();

        public event EventHandler CanExecuteChanged;
    }

    public class DelegateCommand<TArg> : ICommand
    {
        private readonly Action<TArg> _action;

        public DelegateCommand(Action<TArg> action) => _action = action;

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => _action((TArg) parameter);

        public event EventHandler CanExecuteChanged;
    }
}
