namespace CloudAndBear.Desktop;

// Raspored tastature: koje slovo daje koji taster. US je osnova (tabela u KeyboardInputBackend-u); ostali rasporedi
// zamenjuju samo tastere koji se razlikuju. AltGr je desni Alt. Promena rasporeda je Alt+Shift (levi Alt).
public sealed class KeyboardLayout
{
    // slova koja taster daje: bez Shift-a, sa Shift-om, sa AltGr i sa AltGr+Shift (kad nije zadato, važi AltGr)
    public readonly record struct Letters(string Normal, string Shifted, string? AltGr = null, string? ShiftAltGr = null);

    private readonly Dictionary<int, Letters> _keys;

    private KeyboardLayout(string name, Dictionary<int, Letters> keys)
    {
        Name = name;
        _keys = keys;
    }

    public string Name { get; }

    public bool TryGet(int code, out Letters letters) => _keys.TryGetValue(code, out letters);

    // ---- rasporedi (brojevi su Linux kodovi tastera: 2..11 cifre, 16.. gornji red, 30.. srednji, 44.. donji) ----

    // slova kao na američkoj tastaturi
    public static readonly KeyboardLayout Us = new("US", new());

    // srpska latinica kao na Windowsu (QWERTZ): z i y zamenjuju mesta, š đ č ć ž su na [ ] ; ' \, a interpunkcija je drugačija
    public static readonly KeyboardLayout Serbian = new("SR", new()
    {
        [2] = new("1", "!", "~"),
        [3] = new("2", "\""),
        [4] = new("3", "#", "^"),
        [5] = new("4", "$"),
        [6] = new("5", "%"),
        [7] = new("6", "&"),
        [8] = new("7", "/", "`"),
        [9] = new("8", "("),
        [10] = new("9", ")"),
        [11] = new("0", "="),
        [12] = new("'", "?"),
        [13] = new("+", "*"),
        [16] = new("q", "Q", "\\"),
        [17] = new("w", "W", "|"),
        [18] = new("e", "E", "€"),
        [21] = new("z", "Z"),
        [26] = new("š", "Š", "÷"),
        [27] = new("đ", "Đ", "×"),
        [33] = new("f", "F", "["),
        [34] = new("g", "G", "]"),
        [39] = new("č", "Č"),
        [40] = new("ć", "Ć", "ß"),
        [43] = new("ž", "Ž", "¤"),
        [44] = new("y", "Y"),
        [47] = new("v", "V", "@"),
        [48] = new("b", "B", "{"),
        [49] = new("n", "N", "}"),
        [50] = new("m", "M", "§"),
        [51] = new(",", ";", "<"),
        [52] = new(".", ":", ">"),
        [53] = new("-", "_"),
        [86] = new("<", ">"),
    });

    // američka tastatura sa srpskim slovima: sve je kao na US, samo [ ] ; ' \ daju š đ č ć ž, a AltGr vraća staro značenje
    public static readonly KeyboardLayout SerbianUs = new("SR-US", new()
    {
        [26] = new("š", "Š", "[", "{"),
        [27] = new("đ", "Đ", "]", "}"),
        [39] = new("č", "Č", ";", ":"),
        [40] = new("ć", "Ć", "'", "\""),
        [43] = new("ž", "Ž", "\\", "|"),
    });

    public static IReadOnlyList<KeyboardLayout> All { get; } = [Us, Serbian, SerbianUs];

    // ---- izabrani raspored ----

    // true samo kad tastaturu čita naša pozadina (na uređaju); u prozoru na PC-u raspored određuje sistem
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            Changed?.Invoke();   // oznaka u gornjoj traci se pojavljuje čim pozadina počne da radi
        }
    }

    private static bool _enabled;

    public static KeyboardLayout Current { get; private set; } = Load();

    public static event Action? Changed;

    // sledeći raspored po redu; izbor se pamti za sledeće pokretanje
    public static void Next()
    {
        var index = All.ToList().IndexOf(Current);
        Current = All[(index + 1) % All.Count];
        Save();
        Changed?.Invoke();
    }

    // stanje aplikacije: systemd (StateDirectory=) javlja folder u STATE_DIRECTORY, inače /var/lib/cloudandbear;
    // ne ide u kućni folder jer GUI radi i bez njega (sudo bez HOME daje prazno ime, pa bi fajl završio u radnom folderu)
    private static string ConfigFile =>
        Path.Combine(Environment.GetEnvironmentVariable("STATE_DIRECTORY")?.Split(':')[0] ?? "/var/lib/cloudandbear", "layout");

    private static KeyboardLayout Load()
    {
        try
        {
            var name = File.ReadAllText(ConfigFile).Trim();
            return All.FirstOrDefault(l => l.Name == name) ?? Us;
        }
        catch
        {
            return Us;
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
            File.WriteAllText(ConfigFile, Current.Name);
        }
        catch
        {
            // nema prava ili mesta: raspored važi do izlaska iz aplikacije
        }
    }
}
