using System;
using System.Linq;
using Avalonia;
using Avalonia.LinuxFramebuffer;

namespace CloudAndBear.Desktop;

internal class Program
{
  public static int Main(string[] args)
  {
    if(args.Contains("--drm"))
    {
      SilenceConsole();

            var card = args.FirstOrDefault(a => a.StartsWith("--card="))?[7..] ?? FindCard();

      return BuildAvaloniaApp().StartLinuxDrm(args, card: card, options: new DrmOutputOptions
      {Scaling = 1.0},
      inputBackend: new KeyboardInputBackend());
    }

    return BuildAvaloniaApp().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
  }

  private static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
      .UseHarfBuzz()
      .WithInterFont()
      .LogToTrace();

    // kartica na kojoj je priključen ekran (laptop ima dve grafike: Avalonia bi uzela prvu, a ekran je na drugoj);
  // unutrašnji ekran (eDP, LVDS) ima prednost nad spoljnim
  private static string? FindCard()
  {
    var connected = Directory.GetDirectories("/sys/class/drm", "card*-*")
      .Where(IsConnected)
      .OrderBy(d => d.Contains("eDP") || d.Contains("LVDS") ? 0 : 1)
      .ThenBy(d => d)
      .FirstOrDefault();

    if (connected is null) return null;

    var name = Path.GetFileName(connected);   // npr. card1-eDP-1
    return "/dev/dri/" + name[..name.IndexOf('-')];
  }

  private static bool IsConnected(string dir)
  {
    try
    {
      return File.ReadAllText(Path.Combine(dir, "status")).Trim() == "connected";
    }
    catch
    {
      return false;
    }
  }

  private static void SilenceConsole()
  {
    new Thread(() =>
    {
      try
      {
        Console.CursorVisible = false;
        while(true) Console.ReadKey(true);
      }
      catch
      {
        // ignored
      }
    })
    {
      IsBackground = true
    }.Start();
  }
}