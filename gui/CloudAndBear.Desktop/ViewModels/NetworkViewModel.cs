using Avalonia.Threading;
using CloudAndBear.Desktop.Services;

namespace CloudAndBear.Desktop.ViewModels;

// red u tabeli veza
public sealed record DeviceRow(string Name, string Type, string State, string Connection, string Address)
{
    public string TypeText => Type == "wifi" ? "Wi-Fi" : "žični";

    public string StateText => State.Split(' ')[0] switch
    {
        "connected" => "povezan",
        "connecting" => "povezuje se",
        "disconnected" => "nije povezan",
        "unavailable" => "nedostupan",
        "unmanaged" => "nije pod kontrolom",
        _ => State,
    };
}

// odeljak Mreža: adapteri i Wi-Fi (spisak mreža, povezivanje, prekid veze) preko NetworkManagera
public sealed class NetworkViewModel : ViewModelBase, IPage
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private IReadOnlyList<DeviceRow> _devices = [];
    private IReadOnlyList<WifiNetwork> _networks = [];
    private WifiNetwork? _selected;
    private string _password = "", _status = "", _problem = "";
    private bool _busy, _hasWifi, _restoring, _scannedOnce;
    private string _wifiDevice = "";

    public NetworkViewModel()
    {
        Scan = new RelayCommand(RescanAsync, () => !Busy && HasWifi);
        Connect = new RelayCommand(ConnectAsync, () => !Busy && Selected is not null);
        Disconnect = new RelayCommand(DisconnectAsync, () => !Busy && Selected is { Active: true });
        _timer.Tick += async (_, _) => await RefreshDevicesAsync();
    }

    public RelayCommand Scan { get; }
    public RelayCommand Connect { get; }
    public RelayCommand Disconnect { get; }

    public IReadOnlyList<DeviceRow> Devices { get => _devices; private set => Set(ref _devices, value); }
    public IReadOnlyList<WifiNetwork> Networks { get => _networks; private set => Set(ref _networks, value); }

    // izabrana Wi-Fi mreža; prazna vrednost od liste dok se ona osvežava se ignoriše (izbor se vraća po imenu mreže)
    public WifiNetwork? Selected
    {
        get => _selected;
        set
        {
            if (value is null && _restoring) return;
            SetSelected(value);
        }
    }

    public string Password { get => _password; set => Set(ref _password, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    public string Problem
    {
        get => _problem;
        private set
        {
            if (Set(ref _problem, value)) OnPropertyChanged(nameof(HasProblem));
        }
    }

    public bool HasProblem => Problem.Length > 0;

    public bool HasWifi
    {
        get => _hasWifi;
        private set
        {
            if (Set(ref _hasWifi, value)) UpdateCommands();
        }
    }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value)) UpdateCommands();
        }
    }

    public bool HasSelection => Selected is not null;
    public bool NeedsPassword => Selected is { Security.Length: > 0 };
    public bool CanDisconnect => Selected is { Active: true };
    public string SelectedTitle => Selected is { } n ? $"Mreža: {n.Ssid}" : "";

    public void OnShown()
    {
        _timer.Start();
        _ = RefreshDevicesAsync();
        if (!_scannedOnce)
        {
            _scannedOnce = true;
            _ = ScanCoreAsync(rescan: true, report: true);
        }
    }

    public void OnHidden() => _timer.Stop();

    private void SetSelected(WifiNetwork? value)
    {
        _selected = value;
        Password = "";
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(NeedsPassword));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(SelectedTitle));
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        Scan.RaiseCanExecuteChanged();
        Connect.RaiseCanExecuteChanged();
        Disconnect.RaiseCanExecuteChanged();
    }

    // adapteri i njihove adrese; ide na svakih pet sekundi dok je stranica otvorena
    private async Task RefreshDevicesAsync()
    {
        var (devices, error) = await NetworkManager.DevicesAsync();
        if (error is not null)
        {
            Problem = error.Contains("not running") ? "NetworkManager nije pokrenut." : error;
            HasWifi = false;
            return;
        }

        Problem = devices.Count == 0 ? "Nema mrežnih adaptera." : "";
        var rows = devices.Select(d => new DeviceRow(d.Name, d.Type, d.State, d.Connection, SystemInfo.Ipv4(d.Name))).ToList();
        if (!rows.SequenceEqual(Devices)) Devices = rows;

        var wifi = devices.FirstOrDefault(d => d.Type == "wifi");
        _wifiDevice = wifi?.Name ?? "";
        HasWifi = wifi is not null;
        if (wifi is null && Networks.Count == 0 && !Busy) Status = "Nema Wi-Fi adaptera.";
    }

    private Task RescanAsync() => ScanCoreAsync(rescan: true, report: true);

    private async Task ScanCoreAsync(bool rescan, bool report)
    {
        if (Busy) return;
        Busy = true;
        if (report) Status = "Skeniram Wi-Fi mreže…";
        try
        {
            var (list, error) = await NetworkManager.ScanAsync(rescan);
            if (error is not null)
            {
                if (report) Status = error;
                return;
            }

            var keep = Selected?.Ssid;
            _restoring = true;
            Networks = list;
            _restoring = false;
            SetSelected(list.FirstOrDefault(n => n.Ssid == keep));   // lista je zamenjena: izbor se vraća po imenu
            if (report) Status = list.Count == 0 ? "Nema mreža u dometu." : $"Nađeno mreža: {list.Count}.";
        }
        finally
        {
            _restoring = false;
            Busy = false;
        }
    }

    private async Task ConnectAsync()
    {
        if (Busy || Selected is not { } net) return;
        var password = Password;
        Busy = true;
        Status = $"Povezujem se na {net.Ssid}…";
        try
        {
            var r = await NetworkManager.ConnectAsync(net.Ssid, password);
            Status = r.Ok ? $"Povezano na {net.Ssid}." : NetworkManager.Explain(r);
        }
        finally
        {
            Busy = false;
        }
        await RefreshDevicesAsync();
        await RefreshAfterChangeAsync();
    }

    private async Task DisconnectAsync()
    {
        if (Busy || _wifiDevice.Length == 0) return;
        Busy = true;
        Status = "Prekidam vezu…";
        try
        {
            var r = await NetworkManager.DisconnectAsync(_wifiDevice);
            Status = r.Ok ? "Veza je prekinuta." : NetworkManager.Explain(r);
        }
        finally
        {
            Busy = false;
        }
        await RefreshDevicesAsync();
        await RefreshAfterChangeAsync();
    }

    // posle povezivanja ili prekida: spisak bez novog skeniranja, da oznaka "povezan" bude tačna (poruka ostaje)
    private Task RefreshAfterChangeAsync() => ScanCoreAsync(rescan: false, report: false);
}
