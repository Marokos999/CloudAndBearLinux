using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace CloudAndBear.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // PC (razvoj): običan prozor sa istim sadržajem
            desktop.MainWindow = new MainWindow();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // uređaj (DRM): nema prozora, samo jedan pogled preko celog ekrana
            singleView.MainView = new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}