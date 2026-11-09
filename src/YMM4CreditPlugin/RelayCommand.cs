using System.Windows.Input;

namespace YMM4CreditPlugin;

public sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public RelayCommand(Action execute) : this(_ => execute()) { }

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);
}
