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

      var card = args.FirstOrDefault(a => a.StartsWith("--card="))?[7..];

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