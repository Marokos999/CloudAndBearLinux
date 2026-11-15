using System.Windows.Input;

namespace CloudAndBear.Desktop.ViewModels;

// najjednostavnija komanda za dugme: izvrši zadatu akciju, uvek je dozvoljena
public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}