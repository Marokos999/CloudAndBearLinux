using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CloudAndBear.Desktop.Services;

// Stanje sistema: na Linuxu se čita iz /proc i /sys, a na Windowsu (razvoj) samo ono što .NET sam daje.
// Svaka metoda vraća null kad podatak ne postoji (npr. nema baterije), da prikaz ne pukne.
public static class SystemInfo
{
    public static string Kernel() =>
        OperatingSystem.IsLinux() ? Read("/proc/sys/kernel/osrelease") ?? "?" : Environment.OSVersion.ToString();

    // npr. "Debian GNU/Linux 13 (trixie)"; na Windowsu naziv sistema
    public static string OsName()
    {
        var line = OperatingSystem.IsLinux()
            ? Read("/etc/os-release")?.Split('\n').FirstOrDefault(l => l.StartsWith("PRETTY_NAME="))
            : null;
        return line is null ? RuntimeInformation.OSDescription : line["PRETTY_NAME=".Length..].Trim('"');
    }

    // model procesora i broj jezgara, npr. "13th Gen Intel(R) Core(TM) i9-13900H, 20 jezgara"
    public static string CpuModel()
    {
        var model = OperatingSystem.IsLinux()
            ? Read("/proc/cpuinfo")?.Split('\n').FirstOrDefault(l => l.StartsWith("model name"))?.Split(':', 2)[1].Trim()
            : null;
        return $"{model ?? "procesor"}, {Environment.ProcessorCount} jezgara";
    }
    public static TimeSpan Uptime()
    {
        var text = OperatingSystem.IsLinux() ? Read("/proc/uptime") : null;
        return text is not null && double.TryParse(text.Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    // (ukupno, mirovanje) iz prve linije /proc/stat; procenat se dobija iz razlike dva uzorka
    public static (long Total, long Idle)? CpuTimes()
    {
        var line = OperatingSystem.IsLinux() ? Read("/proc/stat")?.Split('\n')[0] : null;
        if (line is null || !line.StartsWith("cpu ")) return null;

        // cpu  user nice system idle iowait irq softirq steal ...
        var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(long.Parse).ToArray();
        return f.Length < 5 ? null : (f.Sum(), f[3] + f[4]);
    }

    // zauzeta i ukupna memorija u bajtovima (zauzeto = ukupno - dostupno)
    public static (long Used, long Total)? Memory()
    {
        var text = OperatingSystem.IsLinux() ? Read("/proc/meminfo") : null;
        if (text is null) return null;

        long? Kb(string key) => text.Split('\n')
            .Where(l => l.StartsWith(key + ":"))
            .Select(l => (long?)long.Parse(l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]))
            .FirstOrDefault();

        return Kb("MemTotal") is { } total && Kb("MemAvailable") is { } free
            ? ((total - free) * 1024, total * 1024)
            : null;
    }

    // zauzet i ukupan prostor glavnog diska u bajtovima
    public static (long Used, long Total)? Disk()
    {
        try
        {
            var root = OperatingSystem.IsLinux() ? "/" : Path.GetPathRoot(Environment.SystemDirectory)!;
            var d = new DriveInfo(root);
            return (d.TotalSize - d.AvailableFreeSpace, d.TotalSize);
        }
        catch
        {
            return null;
        }
    }

    // procenat napunjenosti i stanje prve baterije; null ako je nema
    public static (int Percent, string Status)? Battery()
    {
        const string dir = "/sys/class/power_supply";
        if (!OperatingSystem.IsLinux() || !Directory.Exists(dir)) return null;

        var bat = Directory.GetDirectories(dir, "BAT*").FirstOrDefault();
        if (bat is null || !int.TryParse(Read(bat + "/capacity"), out var percent)) return null;

        var status = Read(bat + "/status") switch
        {
            "Charging" => "puni se",
            "Discharging" => "prazni se",
            "Full" => "puna",
            "Not charging" => "ne puni se",
            var s => s ?? "?"
        };
        return (percent, status);
    }

    // prva IPv4 adresa interfejsa sa datim imenom, ili "—" kad je nema
    public static string Ipv4(string interfaceName)
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name == interfaceName);
        var address = nic?.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
        return address is null ? "—" : address.Address.ToString();
    }

    // aktivni mrežni interfejsi sa IPv4 adresom, npr. "wlp0s20f3  192.168.1.5"
    public static IReadOnlyList<string> Network()
    {
        var lines = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var a in nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                lines.Add($"{nic.Name}  {a.Address}");
        }
        return lines.Count > 0 ? lines : ["nema aktivne mreže"];
    }

    private static string? Read(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }
}
