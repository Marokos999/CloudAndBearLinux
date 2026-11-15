using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CloudAndBear.Desktop.Services;

namespace CloudAndBear.Desktop.ViewModels;

// boja stanja pod-a i čvora: zeleno radi, sivo završeno, žuto čeka, crveno puca
internal static class StatusColors
{
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#5fd38d"));
    private static readonly IBrush Gray = new SolidColorBrush(Color.Parse("#6b7885"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#f0b429"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#ff5f5f"));

    public static IBrush For(string status) => status switch
    {
        "Running" or "Ready" => Green,
        "Completed" or "Succeeded" => Gray,
        "Pending" or "ContainerCreating" or "PodInitializing" or "Terminating" or "Unknown" => Amber,
        _ when status.StartsWith("Init:") => Amber,
        _ => Red,   // CrashLoopBackOff, ImagePullBackOff, Error, Failed, NotReady...
    };
}

public sealed record PodRow(PodInfo Pod)
{
    public IBrush Brush => StatusColors.For(Pod.Status);
}

public sealed record NodeRow(NodeInfo Node)
{
    public IBrush Brush => StatusColors.For(Node.Status.Split(',')[0]);
}

// odeljak Kubernetes (samo čitanje): konteksti iz kubeconfig-a, čvorovi i podovi izabranog konteksta
public sealed class KubernetesViewModel : ViewModelBase, IPage
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private IReadOnlyList<string> _contexts = [];
    private string? _selectedContext;
    private string _summary = "", _problem = "";
    private bool _loading, _restoring, _contextsLoaded;

    public KubernetesViewModel()
    {
        Refresh = new RelayCommand(() => LoadAsync(reloadContexts: true));
        _timer.Tick += async (_, _) => await LoadAsync(reloadContexts: false);
    }

    public RelayCommand Refresh { get; }

    public ObservableCollection<NodeRow> Nodes { get; } = [];
    public ObservableCollection<PodRow> Pods { get; } = [];

    public IReadOnlyList<string> Contexts { get => _contexts; private set => Set(ref _contexts, value); }

    // izabrani kontekst; promena odmah učitava čvorove i podove tog klastera
    public string? SelectedContext
    {
        get => _selectedContext;
        set
        {
            if (value is null && _restoring) return;
            if (!Set(ref _selectedContext, value)) return;
            OnPropertyChanged(nameof(HasContext));
            Nodes.Clear();
            Pods.Clear();
            Summary = "";
            if (value is not null) _ = LoadAsync(reloadContexts: false);
        }
    }

    public bool HasContext => SelectedContext is not null;
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string KubeconfigText => Kube.KubeconfigPath is { } p ? $"kubeconfig: {p}" : "kubeconfig: nije nađen (~/.kube/config)";

    public string Problem
    {
        get => _problem;
        private set
        {
            if (Set(ref _problem, value)) OnPropertyChanged(nameof(HasProblem));
        }
    }

    public bool HasProblem => Problem.Length > 0;

    public void OnShown()
    {
        _timer.Start();
        _ = LoadAsync(reloadContexts: !_contextsLoaded);
    }

    public void OnHidden() => _timer.Stop();

    private async Task LoadAsync(bool reloadContexts)
    {
        if (_loading) return;
        _loading = true;
        string? loadedFor = null;
        try
        {
            if (reloadContexts && !await LoadContextsAsync()) return;
            if (SelectedContext is not { } context) return;

            loadedFor = context;
            var nodesTask = Kube.NodesAsync(context);
            var podsTask = Kube.PodsAsync(context);
            var (nodes, nodesError) = await nodesTask;
            var (pods, podsError) = await podsTask;

            if (context != SelectedContext) return;   // u međuvremenu je izabran drugi kontekst
            if (nodesError is not null || podsError is not null)
            {
                Problem = nodesError ?? podsError!;
                Nodes.Clear();
                Pods.Clear();
                Summary = "";
                return;
            }

            Problem = "";
            Replace(Nodes, nodes.Select(n => new NodeRow(n)).ToList());
            Replace(Pods, pods.Select(p => new PodRow(p)).ToList());
            var running = pods.Count(p => p.Status == "Running");
            Summary = $"Čvorova: {nodes.Count}  ·  podova: {pods.Count} (radi: {running})";
        }
        finally
        {
            _loading = false;
            // kontekst je promenjen dok se učitavao prethodni: učitaj onaj koji je sada izabran
            if (loadedFor is not null && SelectedContext is not null && SelectedContext != loadedFor)
                _ = LoadAsync(reloadContexts: false);
        }
    }

    private async Task<bool> LoadContextsAsync()
    {
        var (contexts, current, error) = await Kube.ContextsAsync();
        if (error is not null)
        {
            Problem = error;
            Contexts = [];
            return false;
        }

        _contextsLoaded = true;
        if (contexts.Count == 0)
        {
            Problem = "U kubeconfig-u nema konteksta.";
            Contexts = [];
            return false;
        }

        var keep = SelectedContext is { } s && contexts.Contains(s) ? s : current ?? contexts[0];
        _restoring = true;
        Contexts = contexts;
        _restoring = false;
        if (keep != SelectedContext) SelectedContext = keep;   // pokreće učitavanje; ovde ga ne dupliramo
        else OnPropertyChanged(nameof(SelectedContext));       // lista je zamenjena, a izbor ostaje isti
        return true;
    }

    // menja samo ono što se razlikuje, da lista ne "skače" pri svakom osvežavanju
    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (i >= target.Count) target.Add(source[i]);
            else if (!EqualityComparer<T>.Default.Equals(target[i], source[i])) target[i] = source[i];
        }
        while (target.Count > source.Count) target.RemoveAt(target.Count - 1);
    }
}
