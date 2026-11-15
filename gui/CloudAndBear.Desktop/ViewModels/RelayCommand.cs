using System.Windows.Input;

namespace CloudAndBear.Desktop.ViewModels;

// komanda za dugme: izvrši akciju (običnu ili asinhronu) kad je dozvoljena; stanje dozvole se javlja sa RaiseCanExecuteChanged
public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(() => { execute(); return Task.CompletedTask; }, canExecute)
    {
    }

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter)
    {
        try
        {
            await _execute();
        }
        catch (Exception e)
        {
            System.Diagnostics.Trace.WriteLine($"komanda nije uspela: {e}");
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
