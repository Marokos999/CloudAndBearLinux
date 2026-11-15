using Avalonia.Threading;

namespace CloudAndBear.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private NavItem _selected;
    private string _clock = DateTime.Now.ToString("HH:mm:ss");

    public MainViewModel()
    {
        _selected = Items[0];

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Clock = DateTime.Now.ToString("HH:mm:ss");
        timer.Start();
    }

    public IReadOnlyList<NavItem> Items { get; } =
    [
        new("Kontrolna tabla", new PlaceholderViewModel("Kontrolna tabla")),
        new("Mreža", new PlaceholderViewModel("Mreža")),
        new("Kontejneri", new PlaceholderViewModel("Kontejneri")),
        new("Kubernetes", new PlaceholderViewModel("Kubernetes")),
        new("Sistem", new PlaceholderViewModel("Sistem")),
    ];

    public NavItem Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value)) OnPropertyChanged(nameof(CurrentPage));
        }
    }

    public ViewModelBase CurrentPage => _selected.Page;

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