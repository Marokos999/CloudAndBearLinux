using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CloudAndBear.Desktop.Services;

// ExitCode -1 znači da program nije pokrenut (nije instaliran) ili da je isteklo vreme
public readonly record struct CommandResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
}

// Pokreće spoljni program bez ljuske: argumenti se ne tumače, pa lozinke i imena sa razmacima prolaze bez navodnika.
public static partial class ProcessRunner
{
    public static async Task<CommandResult> RunAsync(string file, IEnumerable<string> args, int timeoutSeconds = 20,
        IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["LC_ALL"] = "C";   // izlaz na engleskom, da ga možemo tačno čitati
        if (env is not null)
            foreach (var (key, value) in env) psi.Environment[key] = value;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        Process? p = null;
        try
        {
            p = Process.Start(psi) ?? throw new InvalidOperationException("program se nije pokrenuo");
            p.StandardInput.Close();   // program nikad ne čeka unos
            var output = p.StandardOutput.ReadToEndAsync(cts.Token);
            var error = p.StandardError.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            return new CommandResult(p.ExitCode, (await output).Trim(), (await error).Trim());
        }
        catch (OperationCanceledException)
        {
            try { p?.Kill(true); } catch { /* već je završio */ }
            return new CommandResult(-1, "", $"{file}: isteklo vreme ({timeoutSeconds} s)");
        }
        catch (Win32Exception)
        {
            return new CommandResult(-1, "", $"{file} nije instaliran");
        }
        finally
        {
            p?.Dispose();
        }
    }

    // prva neprazna linija greške (ili izlaza), za prikaz korisniku; kubectl pre prave poruke ispiše tehničke
    // linije oblika "E1115 20:27:12.938201 3277 memcache.go:265] ...", pa se one preskaču
    public static string FirstLine(CommandResult r)
    {
        var text = string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault(l => !LogLine().IsMatch(l)) ?? lines.FirstOrDefault() ?? "nepoznata greška";
    }

    [GeneratedRegex(@"^[EWIF]\d{4} \d{2}:\d{2}:\d{2}")]
    private static partial Regex LogLine();
}
