using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace CloudAndBear.Desktop;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        Clock.Text = DateTime.Now.ToString("HH:mm:ss");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Clock.Text = DateTime.Now.ToString("HH:mm:ss");
        timer.Start();

        PointerMoved += (_, e) =>
        {
            var p = e.GetPosition(this);
            Info.Text = $"miš: {p.X:0}, {p.Y:0}";
        };

        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not { } top) return;
            top.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Environment.Exit(0);   // privremeno
                if (e.Key == Key.Back && Typed.Text?.Length > 0) Typed.Text = Typed.Text[..^1];
                Info.Text = $"taster: {e.Key}";
            };
            top.TextInput += (_, e) => Typed.Text += e.Text;
        };
    }
}