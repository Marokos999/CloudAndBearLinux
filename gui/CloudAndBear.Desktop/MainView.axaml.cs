using Avalonia.Controls;
using Avalonia.Input;
using CloudAndBear.Desktop.ViewModels;

namespace CloudAndBear.Desktop;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        var vm = new MainViewModel();
        DataContext = vm;

        // na uređaju fokus ne mora biti na ovom pogledu: tastere i miša slušamo na celom ekranu (TopLevel)
        AttachedToVisualTree += (_, _) =>
        {
            Focus();   // bez ovoga se prvi pritisnuti taster samo potroši na dodelu fokusa
            if (TopLevel.GetTopLevel(this) is not { } top) return;

            top.KeyDown += (_, e) =>
            {
                switch (e.Key)
                {
                    case Key.Q when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt): Environment.Exit(0); break;   // Ctrl+Alt+Q: izlaz
                    case Key.Up: vm.Move(-1); break;
                    case Key.Down: vm.Move(1); break;
                }
            };

            // na uređaju (nije prozor) crtamo sopstveni kursor, jer ga Avalonia ne crta
            if (top is not Window)
                top.PointerMoved += (_, e) =>
                {
                    var p = e.GetPosition(this);
                    Canvas.SetLeft(MouseCursor, p.X);
                    Canvas.SetTop(MouseCursor, p.Y);
                    MouseCursor.IsVisible = true;
                };
        };
    }
}
