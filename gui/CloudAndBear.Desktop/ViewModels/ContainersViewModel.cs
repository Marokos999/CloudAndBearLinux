using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CloudAndBear.Desktop.Services;

namespace CloudAndBear.Desktop.ViewModels;

// red u spisku kontejnera; objekat ostaje isti dok kontejner postoji, pa izbor u listi ne nestaje pri osvežavanju
public sealed class ContainerRow(ContainerInfo info) : ViewModelBase
{
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#5fd38d"));
    private static readonly IBrush Gray = new SolidColorBrush(Color.Parse("#6b7885"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#f0b429"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#ff5f5f"));

    public ContainerInfo Info { get; private set; } = info;

    public string Id => Info.Id;
    public bool Running => Info.State == "running";

    public IBrush StateBrush => Info.State switch
    {
        "running" => Green,
        "exited" or "created" => Gray,
        "dead" => Red,
        _ => Amber,   // paused, restarting, removing
    };

    public void Update(ContainerInfo next)
    {
        if (next == Info) return;
        Info = next;
        OnPropertyChanged(nameof(Info));
        OnPropertyChanged(nameof(Running));
        OnPropertyChanged(nameof(StateBrush));
    }
}

// odeljak Kontejneri: Docker spisak, start/stop/restart/brisanje i poslednje linije ispisa izabranog kontejnera
public sealed class ContainersViewModel : ViewModelBase, IPage
{
    private readonly DockerClient _docker = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private ContainerRow? _selected;
    private string _summary = "", _message = "", _problem = "", _logs = "";
    private bool _busy, _loading, _confirmRemove;

    public ContainersViewModel()
    {
        Start = new RelayCommand(() => ActAsync($"Pokrećem", "Pokrenut", id => _docker.StartAsync(id)), () => CanAct && Selected is { Running: false });
        Stop = new RelayCommand(() => ActAsync("Zaustavljam", "Zaustavljen", id => _docker.StopAsync(id)), () => CanAct && Selected is { Running: true });
        Restart = new RelayCommand(() => ActAsync("Restartujem", "Restartovan", id => _docker.RestartAsync(id)), () => CanAct && Selected is { Running: true });
        Remove = new RelayCommand(() => ConfirmRemove = true, () => CanAct && Selected is not null);
        ConfirmYes = new RelayCommand(() => ActAsync("Brišem", "Obrisan", id => _docker.RemoveAsync(id)));
        ConfirmNo = new RelayCommand(() => ConfirmRemove = false);
        Refresh = new RelayCommand(RefreshAsync);
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public ObservableCollection<ContainerRow> Containers { get; } = [];

    public RelayCommand Start { get; }
    public RelayCommand Stop { get; }
    public RelayCommand Restart { get; }
    public RelayCommand Remove { get; }
    public RelayCommand ConfirmYes { get; }
    public RelayCommand ConfirmNo { get; }
    public RelayCommand Refresh { get; }

    public ContainerRow? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            ConfirmRemove = false;
            Logs = "";
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(LogsTitle));
            UpdateCommands();
            _ = LoadLogsAsync();
        }
    }

    public bool HasSelection => Selected is not null;
    public string LogsTitle => Selected is { } r ? $"Ispis: {r.Info.Name}" : "";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string Logs { get => _logs; private set => Set(ref _logs, value); }

    public string Problem
    {
        get => _problem;
        private set
        {
            if (Set(ref _problem, value)) OnPropertyChanged(nameof(HasProblem));
        }
    }

    public bool HasProblem => Problem.Length > 0;

    public bool ConfirmRemove
    {
        get => _confirmRemove;
        private set
        {
            if (!Set(ref _confirmRemove, value)) return;
            OnPropertyChanged(nameof(NotConfirming));
            OnPropertyChanged(nameof(RemoveQuestion));
        }
    }

    public bool NotConfirming => !ConfirmRemove;
    public string RemoveQuestion => Selected is { } r ? $"Obrisati kontejner {r.Info.Name}?" : "";

    private bool CanAct => !_busy;

    public void OnShown()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    public void OnHidden() => _timer.Stop();

    private void UpdateCommands()
    {
        Start.RaiseCanExecuteChanged();
        Stop.RaiseCanExecuteChanged();
        Restart.RaiseCanExecuteChanged();
        Remove.RaiseCanExecuteChanged();
    }

    private async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var list = await _docker.ListAsync();
            Problem = "";
            Sync(list);
            Summary = $"Pokrenuto {list.Count(c => c.State == "running")} od {list.Count}";
            UpdateCommands();
            await LoadLogsAsync();
        }
        catch (Exception e)
        {
            Containers.Clear();
            Summary = "";
            Problem = IsSocketError(e)
                ? "Docker nije pokrenut ili nije instaliran (nema /var/run/docker.sock)."
                : $"Docker: {e.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    // soket ne postoji ili ne prima veze: negde u lancu izuzetaka je SocketException
    private static bool IsSocketError(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
            if (e is System.Net.Sockets.SocketException) return true;
        return false;
    }

    // usklađuje listu sa Dockerom, a postojeći redovi ostaju isti objekti
    private void Sync(IReadOnlyList<ContainerInfo> list)
    {
        for (var i = Containers.Count - 1; i >= 0; i--)
            if (list.All(c => c.Id != Containers[i].Id)) Containers.RemoveAt(i);

        for (var i = 0; i < list.Count; i++)
        {
            var at = Containers.ToList().FindIndex(r => r.Id == list[i].Id);
            if (at < 0) Containers.Insert(i, new ContainerRow(list[i]));
            else
            {
                Containers[at].Update(list[i]);
                if (at != i) Containers.Move(at, i);
            }
        }
    }

    private async Task LoadLogsAsync()
    {
        if (Selected is not { } row) return;
        try
        {
            var text = await _docker.LogsAsync(row.Id);
            if (Selected == row) Logs = text.Length == 0 ? "(nema ispisa)" : text;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // kontejner je u međuvremenu obrisan: sledeće osvežavanje to ispravlja
        }
    }

    private async Task ActAsync(string doing, string done, Func<string, Task> action)
    {
        if (_busy || Selected is not { } row) return;
        var name = row.Info.Name;
        ConfirmRemove = false;
        _busy = true;
        UpdateCommands();
        Message = $"{doing} {name}…";
        try
        {
            await action(row.Id);
            Message = $"{done}: {name}.";
        }
        catch (Exception e)
        {
            Message = e.Message;
        }
        finally
        {
            _busy = false;
        }
        await RefreshAsync();
        UpdateCommands();
    }
}
