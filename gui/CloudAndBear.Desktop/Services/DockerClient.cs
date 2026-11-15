using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloudAndBear.Desktop.Services;

public sealed record ContainerInfo(string Id, string Name, string Image, string State, string Status, string Ports);

// Docker Engine API preko unix soketa (/var/run/docker.sock): bez docker programa, samo HTTP.
// GUI radi kao root, pa soket može da se čita; običnom nalogu treba grupa docker.
public sealed partial class DockerClient
{
    private const string SocketPath = "/var/run/docker.sock";

    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        ConnectCallback = async (_, ct) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    })
    {
        BaseAddress = new Uri("http://docker/"),
        Timeout = TimeSpan.FromSeconds(40),
    };

    public async Task<string> VersionAsync()
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync("version"));
        return doc.RootElement.TryGetProperty("Version", out var v) ? v.GetString() ?? "?" : "?";
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListAsync()
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync("containers/json?all=true"));
        var list = new List<ContainerInfo>();
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            var names = c.GetProperty("Names");
            var ports = c.TryGetProperty("Ports", out var p)
                ? p.EnumerateArray().Select(PortText).Distinct().ToArray()   // IPv4 i IPv6 daju isti red dvaput
                : [];
            list.Add(new ContainerInfo(
                c.GetProperty("Id").GetString()![..12],
                names.GetArrayLength() > 0 ? names[0].GetString()!.TrimStart('/') : "?",
                c.GetProperty("Image").GetString() ?? "?",
                c.GetProperty("State").GetString() ?? "?",
                c.GetProperty("Status").GetString() ?? "",
                string.Join(", ", ports)));
        }
        return list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task StartAsync(string id) => PostAsync($"containers/{id}/start");

    public Task StopAsync(string id) => PostAsync($"containers/{id}/stop?t=10");

    public Task RestartAsync(string id) => PostAsync($"containers/{id}/restart?t=10");

    public async Task RemoveAsync(string id)
    {
        using var r = await _http.DeleteAsync($"containers/{id}");
        await CheckAsync(r);
    }

    // poslednjih nekoliko linija ispisa kontejnera
    public async Task<string> LogsAsync(string id, int lines = 8)
    {
        var bytes = await _http.GetByteArrayAsync($"containers/{id}/logs?stdout=1&stderr=1&tail={lines}");
        var text = Clean(Decode(bytes));
        return string.Join('\n', text.Split('\n').TakeLast(lines).Select(l => l.Length > 160 ? l[..160] + "…" : l));
    }

    private async Task PostAsync(string path)
    {
        using var r = await _http.PostAsync(path, null);
        await CheckAsync(r);
    }

    // 304 znači "već je u tom stanju" (npr. start pokrenutog kontejnera): nije greška
    private static async Task CheckAsync(HttpResponseMessage r)
    {
        if (r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.NotModified) return;

        var body = await r.Content.ReadAsStringAsync();
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            message = doc.RootElement.GetProperty("message").GetString();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // telo nije JSON sa porukom
        }
        throw new InvalidOperationException(message ?? r.StatusCode.ToString());
    }

    private static string PortText(JsonElement p)
    {
        var type = p.TryGetProperty("Type", out var t) ? t.GetString() : "tcp";
        var inner = p.GetProperty("PrivatePort").GetInt32();
        return p.TryGetProperty("PublicPort", out var pub) ? $"{pub.GetInt32()}->{inner}/{type}" : $"{inner}/{type}";
    }

    // Bez TTY-a Docker šalje okvire: 8 bajtova zaglavlja (tip toka, tri nule, veličina) pa sadržaj; sa TTY-em čist tekst
    private static string Decode(byte[] b)
    {
        var framed = b.Length >= 8 && b[0] <= 2 && b[1] == 0 && b[2] == 0 && b[3] == 0;
        if (!framed) return Encoding.UTF8.GetString(b);

        var sb = new StringBuilder();
        var i = 0;
        while (i + 8 <= b.Length)
        {
            var size = (b[i + 4] << 24) | (b[i + 5] << 16) | (b[i + 6] << 8) | b[i + 7];
            i += 8;
            var n = Math.Clamp(size, 0, b.Length - i);
            sb.Append(Encoding.UTF8.GetString(b, i, n));
            i += n;
        }
        return sb.ToString();
    }

    private static string Clean(string text) => Ansi().Replace(text.Replace("\r", ""), "").TrimEnd();

    [GeneratedRegex("\u001B\\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex Ansi();
}
