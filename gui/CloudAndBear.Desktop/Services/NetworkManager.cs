using System.Globalization;
using System.Text;

namespace CloudAndBear.Desktop.Services;

// mrežni adapter (žični ili Wi-Fi) kako ga javlja NetworkManager
public sealed record NetDevice(string Name, string Type, string State, string Connection);

// Wi-Fi mreža u dometu; Signal je 0..100
public sealed record WifiNetwork(string Ssid, int Signal, string Security, bool Active)
{
    public string SignalText => $"{Signal} %";
    public string SecurityText => Security.Length == 0 ? "otvorena" : Security;
    public string ActiveText => Active ? "povezan" : "";
}

// NetworkManager preko nmcli-ja. Terse režim (-t): polja se razdvajaju sa ':', a ':' i '\' u vrednosti dolaze sa '\' ispred.
public static class NetworkManager
{
    public static async Task<(IReadOnlyList<NetDevice> Devices, string? Error)> DevicesAsync()
    {
        var r = await ProcessRunner.RunAsync("nmcli", ["-t", "-f", "DEVICE,TYPE,STATE,CONNECTION", "device", "status"]);
        if (!r.Ok) return ([], ProcessRunner.FirstLine(r));

        var devices = Lines(r.Output)
            .Select(Split)
            .Where(f => f.Length >= 4 && (f[1] is "ethernet" or "wifi") && f[2] != "unmanaged")   // veth* adapteri Dockera su "unmanaged"
            .Select(f => new NetDevice(f[0], f[1], f[2], f[3]))
            .ToList();
        return (devices, null);
    }

    public static async Task<(IReadOnlyList<WifiNetwork> Networks, string? Error)> ScanAsync(bool rescan)
    {
        var r = await ProcessRunner.RunAsync("nmcli",
            ["-t", "-f", "ACTIVE,SSID,SIGNAL,SECURITY", "device", "wifi", "list", "--rescan", rescan ? "yes" : "no"], 40);
        if (!r.Ok) return ([], ProcessRunner.FirstLine(r));

        // ista mreža se vidi preko više pristupnih tačaka: ostaje jedan red sa najjačim signalom
        var best = new Dictionary<string, WifiNetwork>();
        foreach (var f in Lines(r.Output).Select(Split))
        {
            if (f.Length < 4 || f[1].Length == 0) continue;   // skrivene mreže (bez imena) preskačemo

            var net = new WifiNetwork(f[1], int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0, f[3], f[0] == "yes");
            best[net.Ssid] = best.TryGetValue(net.Ssid, out var old)
                ? old with { Signal = Math.Max(old.Signal, net.Signal), Active = old.Active || net.Active }
                : net;
        }
        return (best.Values.OrderByDescending(n => n.Active).ThenByDescending(n => n.Signal).ToList(), null);
    }

    // prazna lozinka znači "koristi sačuvanu vezu" (ili otvorenu mrežu)
    public static async Task<CommandResult> ConnectAsync(string ssid, string? password)
    {
        var withPassword = !string.IsNullOrEmpty(password);

        // Stari ili pokvaren profil iste mreže (npr. od pogrešne lozinke) nmcli ponovo koristi i javlja
        // "802-11-wireless-security.key-mgmt: property is missing", pa se pre unosa nove lozinke briše.
        if (withPassword) await ForgetAsync(ssid);

        var args = new List<string> { "--wait", "30", "device", "wifi", "connect", ssid };
        if (withPassword)
        {
            args.Add("password");
            args.Add(password!);
        }
        var r = await ProcessRunner.RunAsync("nmcli", args, 45);

        if (!r.Ok && withPassword) await ForgetAsync(ssid);   // neuspeo pokušaj ne ostavlja pokvaren profil
        return r;
    }

    // briše sačuvane Wi-Fi profile sa tim imenom
    private static async Task ForgetAsync(string ssid)
    {
        var list = await ProcessRunner.RunAsync("nmcli", ["-t", "-f", "UUID,NAME,TYPE", "connection", "show"]);
        if (!list.Ok) return;

        foreach (var f in Lines(list.Output).Select(Split))
            if (f.Length >= 3 && f[1] == ssid && f[2] == "802-11-wireless")
                await ProcessRunner.RunAsync("nmcli", ["connection", "delete", "uuid", f[0]]);
    }

    public static Task<CommandResult> DisconnectAsync(string device) =>
        ProcessRunner.RunAsync("nmcli", ["device", "disconnect", device], 20);

    // poruka nmcli-ja na srpskom za slučajeve koje korisnik zaista vidi
    public static string Explain(CommandResult r)
    {
        // nmcli pre prave greške ispiše upozorenja (Warning: ...), pa se traži poznata poruka u celom ispisu
        var all = r.Error + "\n" + r.Output;
        if (all.Contains("Secrets were required")) return "Lozinka nedostaje ili nije tačna.";
        if (all.Contains("No network with SSID")) return "Mreža nije u dometu.";
        if (all.Contains("Timeout")) return "Povezivanje je predugo trajalo.";

        var lines = all.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault(l => l.StartsWith("Error")) ?? lines.FirstOrDefault() ?? "nepoznata greška";
    }

    public static string[] Split(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\' && i + 1 < line.Length) sb.Append(line[++i]);
            else if (c == ':')
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return [.. fields];
    }

    private static IEnumerable<string> Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
