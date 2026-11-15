using Avalonia.Controls;
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
    }
}