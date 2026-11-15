using System.Diagnostics;
using System.Windows.Input;
using CloudAndBear.Desktop.Services;

namespace CloudAndBear.Desktop.ViewModels;

// odeljak Sistem: podaci o računaru i napajanje (restart, isključenje, izlaz u konzolu)
public sealed class SystemViewModel : ViewModelBase
{
    private string? _pending;
    private string _message = "";

    public SystemViewModel()
    {
        Restart = new RelayCommand(() => Pending = "restart");
        Shutdown = new RelayCommand(() => Pending = "shutdown");
        ExitToConsole = new RelayCommand(() => Environment.Exit(0));
        Confirm = new RelayCommand(Run);
        Cancel = new RelayCommand(() => Pending = null);
    }

    public string OsName { get; } = SystemInfo.OsName();
    public string Kernel { get; } = SystemInfo.Kernel();
    public string Hostname { get; } = Environment.MachineName;
    public string Cpu { get; } = SystemInfo.CpuModel();
    public string TimeZone { get; } = TimeZoneInfo.Local.Id;

    public ICommand Restart { get; }
    public ICommand Shutdown { get; }
    public ICommand ExitToConsole { get; }
    public ICommand Confirm { get; }
    public ICommand Cancel { get; }

    // radnja koja čeka potvrdu ("restart" ili "shutdown"), inače null
    public string? Pending
    {
        get => _pending;
        private set
        {
            if (!Set(ref _pending, value)) return;
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(Question));
        }
    }

    public bool IsPending => Pending is not null;
    public string Question => Pending == "restart" ? "Restartovati računar?" : "Isključiti računar?";

    public string Message { get => _message; private set => Set(ref _message, value); }

    // systemctl radi samo na Linuxu; na Windowsu (razvoj) javlja grešku umesto da išta isključi
    private void Run()
    {
        var verb = Pending == "restart" ? "reboot" : "poweroff";
        Pending = null;
        try
        {
            Process.Start("systemctl", verb);
        }
        catch (Exception e)
        {
            Message = e.Message;
        }
    }
}