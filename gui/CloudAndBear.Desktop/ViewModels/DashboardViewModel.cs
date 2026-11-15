using Avalonia.Threading;
using CloudAndBear.Desktop.Services;

namespace CloudAndBear.Desktop.ViewModels;

// kontrolna tabla: stanje sistema na jednom mestu, osvežava se na dve sekunde
public sealed class DashboardViewModel : ViewModelBase
{
    private (long Total, long Idle)? _lastCpu;
    private double _cpuPercent, _memoryPercent, _diskPercent;
    private string _cpuText = "", _memoryText = "", _diskText = "", _batteryText = "", _uptimeText = "";
    private IReadOnlyList<string> _network = [];

    public DashboardViewModel()
    {
        Hostname = Environment.MachineName;
        Kernel = SystemInfo.Kernel();

        Refresh();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
    }

    public string Hostname { get; }
    public string Kernel { get; }

    public double CpuPercent { get => _cpuPercent; private set => Set(ref _cpuPercent, value); }
    public string CpuText { get => _cpuText; private set => Set(ref _cpuText, value); }

    public double MemoryPercent { get => _memoryPercent; private set => Set(ref _memoryPercent, value); }
    public string MemoryText { get => _memoryText; private set => Set(ref _memoryText, value); }

    public double DiskPercent { get => _diskPercent; private set => Set(ref _diskPercent, value); }
    public string DiskText { get => _diskText; private set => Set(ref _diskText, value); }

    public string BatteryText { get => _batteryText; private set => Set(ref _batteryText, value); }
    public string UptimeText { get => _uptimeText; private set => Set(ref _uptimeText, value); }
    public IReadOnlyList<string> Network { get => _network; private set => Set(ref _network, value); }

    private void Refresh()
    {
        // procesor: procenat zauzeća je razlika dva uzorka /proc/stat
        var cpu = SystemInfo.CpuTimes();
        if (cpu is { } now && _lastCpu is { } before && now.Total > before.Total)
            CpuPercent = 100.0 * (1 - (double)(now.Idle - before.Idle) / (now.Total - before.Total));
        _lastCpu = cpu;
        CpuText = cpu is null ? "nema podatka" : $"{CpuPercent:0} %";

        if (SystemInfo.Memory() is { } mem)
        {
            MemoryPercent = 100.0 * mem.Used / mem.Total;
            MemoryText = $"{Gb(mem.Used)} / {Gb(mem.Total)}";
        }
        else MemoryText = "nema podatka";

        if (SystemInfo.Disk() is { } disk)
        {
            DiskPercent = 100.0 * disk.Used / disk.Total;
            DiskText = $"{Gb(disk.Used)} / {Gb(disk.Total)}";
        }
        else DiskText = "nema podatka";

        BatteryText = SystemInfo.Battery() is { } bat ? $"{bat.Percent} %  ·  {bat.Status}" : "nema baterije";

        var up = SystemInfo.Uptime();
        UptimeText = $"radi {(int)up.TotalHours} h {up.Minutes} min";

        var net = SystemInfo.Network();
        if (!net.SequenceEqual(_network)) Network = net;   // lista se menja retko, ne crtamo je uzalud
    }

    private static string Gb(long bytes) => $"{bytes / 1073741824.0:0.0} GB";
}
