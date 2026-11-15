using Avalonia.Threading;

namespace CloudAndBear.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private NavItem _selected;
    private string _clock = DateTime.Now.ToString("HH:mm:ss");

    public MainViewModel()
    {
        _selected = Items[0];
        (_selected.Page as IPage)?.OnShown();
        KeyboardLayout.Changed += () => OnPropertyChanged(nameof(LayoutName));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Clock = DateTime.Now.ToString("HH:mm:ss");
        timer.Start();
    }

    public IReadOnlyList<NavItem> Items { get; } =
    [
        new("Kontrolna tabla", new DashboardViewModel()),
        new("Mreža", new NetworkViewModel()),
        new("Kontejneri", new ContainersViewModel()),
        new("Kubernetes", new KubernetesViewModel()),
        new("Sistem", new SystemViewModel()),
    ];

    public NavItem Selected
    {
        get => _selected;
        set
        {
            var old = _selected;
            if (!Set(ref _selected, value)) return;

            // stranica koja se napušta prestaje da osvežava podatke, a nova odmah počinje
            (old.Page as IPage)?.OnHidden();
            (value.Page as IPage)?.OnShown();
            OnPropertyChanged(nameof(CurrentPage));
        }
    }

    public ViewModelBase CurrentPage => _selected.Page;

    // izabrani raspored tastature (US, SR, SR-US); prazno u prozoru na PC-u, gde raspored određuje sistem
    public string LayoutName => KeyboardLayout.Enabled ? KeyboardLayout.Current.Name : "";

    public string Clock
    {
        get => _clock;
        private set => Set(ref _clock, value);
    }

    // strelice gore/dole menjaju izabrani odeljak
    public void Move(int step)
    {
        var index = Math.Clamp(Items.ToList().IndexOf(Selected) + step, 0, Items.Count - 1);
        Selected = Items[index];
    }
}
