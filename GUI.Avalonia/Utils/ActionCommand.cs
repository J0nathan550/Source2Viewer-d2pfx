using System.Windows.Input;

namespace GUI.Utils;

sealed class ActionCommand(Action execute) : ICommand
{
#pragma warning disable CS0067 // The command is always executable
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
