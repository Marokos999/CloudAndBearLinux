# Vodič: prvi GUI (Avalonia, direktno na ekran, bez X11/Wayland-a)

Cilj prvog koraka: **tvoj prozor preko celog ekrana na laptopu**, sa satom i pokazivačem, bez ikakvog drugog GUI-ja u sistemu. Kod pišeš ti; ovde je ono što sam proverila i tačni koraci.

## Šta je proveravano, a šta nije

Provera je rađena 2026-11-15 na PC-u (Windows) i u VirtualBox VM-u sa našim live ISO-om (Debian 13, kernel 6.12, grafika VMSVGA).

| Proveravano | Rezultat |
|---|---|
| `dotnet build` (PC) | 0 grešaka, 0 upozorenja |
| `dotnet publish -r linux-x64 --self-contained` | 222 fajla, 103 MB |
| 9 paketa iz poglavlja 3 u live sistemu | dovoljni: `ldd` ne javlja nijednu biblioteku koja fali |
| pokretanje `--drm` u VM-u | **radi**: tamna pozadina, naslov, sat koji ide svake sekunde, font iz aplikacije (i `š`), bez X11 i Waylanda |
| izlaz | radi: `SIGINT` (kao `Ctrl+C`) i `SIGTERM` zatvaraju aplikaciju, konzola `tty1` se vraća |
| ispravka koja je ovde nađena | bez `.UseHarfBuzz()` aplikacija pada pri pokretanju (vidi `Program.cs`) |
| tastatura (sopstvena pozadina unosa, poglavlje „Tastatura") | radi: slova, `Shift`+cifre, `Backspace`, `Esc`; konzola iza GUI-ja ne dobija tastere |

| Nije proveravano ili ne radi | Zašto |
|---|---|
| tastatura laptopa, srpski raspored | samo US raspored, i to u VM-u; vidi „Tastatura" |
| miš / touchpad | VM je radio bez miša; `libinput` pointer kod Avalonie postoji, ali nije probano |
| hibridna grafika laptopa (Intel + NVIDIA) | u VM-u je jedna grafika, i to softverska; birač kartice (`--card=`) nije probano |
| `dotnet run` (prozor na PC-u) | nisam ga pokretala, jer bi otvorio prozor na tvom ekranu dok radiš |
| `Ctrl+C` kroz pravi `ssh -t` | proveren ekvivalent (`SIGINT` na proces u prvom planu) |

Prvo pokretanje na laptopu je pravi test za grafiku i miša. Ako nešto ne radi, ide u tabelu „Ako ne radi" na kraju.

## 1. Preduslovi na PC-u

- .NET SDK 10 (kod tebe je instaliran `10.0.204`). Provera: `dotnet --version`.
- Avalonia šabloni **ne treba da se instaliraju**: projekat pravimo ručno (8 fajlova), da znaš šta svaki red radi.
- Avalonia **12.1.6** (poslednja stabilna u trenutku pisanja; svih pet paketa mora da ima istu verziju).

## 2. Projekat

Sve ide u `gui\CloudAndBear.Desktop` unutar repoa.

```powershell
cd "D:\CloudAndBear Linux\CloudAndBearLinux"
mkdir gui\CloudAndBear.Desktop
cd gui\CloudAndBear.Desktop
```

### `gui\.gitignore`

```
bin/
obj/
publish/
```

### `CloudAndBear.Desktop.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>CloudAndBear.Desktop</RootNamespace>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <!-- bez ICU biblioteke na uređaju (slika je minimalna) -->
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.1.6" />
    <PackageReference Include="Avalonia.Desktop" Version="12.1.6" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.6" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="12.1.6" />
    <PackageReference Include="Avalonia.LinuxFramebuffer" Version="12.1.6" />
  </ItemGroup>

</Project>
```

Čemu služi svaki paket:

- `Avalonia.Desktop`: običan prozor na Windowsu, za razvoj na PC-u.
- `Avalonia.LinuxFramebuffer`: crtanje direktno na ekran (DRM/KMS), bez X11/Wayland-a, za laptop.
- `Avalonia.Fonts.Inter`: font **u aplikaciji**. Slika nema sistemskih fontova, pa bi bez ovoga tekst bio nevidljiv.
- `InvariantGlobalization`: aplikacija ne traži ICU u slici. Cena: nema regionalnog sortiranja (`ć`, `š`) i formatiranja, što za sada ne smeta.

### `Program.cs`

```csharp
using Avalonia;
using Avalonia.LinuxFramebuffer;

namespace CloudAndBear.Desktop;

internal static class Program
{
    public static int Main(string[] args)
    {
        // na uređaju (bez X11/Wayland-a): crta direktno preko DRM/KMS
        if (args.Contains("--drm"))
        {
            SilenceConsole();
            var card = args.FirstOrDefault(a => a.StartsWith("--card="))?[7..];   // npr. --card=/dev/dri/card1
            return BuildAvaloniaApp().StartLinuxDrm(args, card: card, options: new DrmOutputOptions { Scaling = 1.0 });
        }

        // na PC-u (razvoj): običan prozor
        return BuildAvaloniaApp().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHarfBuzz()     // oblikovanje teksta: na uređaju nema UsePlatformDetect(), pa se podešava ručno
            .WithInterFont()   // font je u aplikaciji: slika nema sistemskih fontova
            .LogToTrace();

    // sakriva kursor konzole i guta tastere da ne završe u shell-u iza GUI-ja
    private static void SilenceConsole()
    {
        new Thread(() =>
        {
            try
            {
                Console.CursorVisible = false;
                while (true) Console.ReadKey(true);
            }
            catch
            {
                // nema konzole (npr. pokrenuto iz systemd servisa): nema šta da se utiša
            }
        })
        { IsBackground = true }.Start();
    }
}
```

Zašto dva puta: na uređaju nema prozora ni prozorskog sistema, pa Avalonia dobija samo **jedan pogled** (`ISingleViewApplicationLifetime`), a na PC-u običan prozor (`IClassicDesktopStyleApplicationLifetime`). Ceo izgled zato živi u jednom `MainView` pogledu koji koriste oba.

`.UseHarfBuzz()` je dodato jer je test u VM-u pao sa greškom `No text shaping system configured`: `UsePlatformDetect()` to radi sam, ali na uređaju se ne poziva.

### `App.axaml`

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="CloudAndBear.Desktop.App"
             RequestedThemeVariant="Dark">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

### `App.axaml.cs`

```csharp
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
```

### `MainView.axaml`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="CloudAndBear.Desktop.MainView"
             Focusable="True">
  <Grid Background="#101820">
    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="14">
      <TextBlock Text="CloudAndBear" FontSize="56" FontWeight="SemiBold"
                 Foreground="#9ecbff" HorizontalAlignment="Center" />
      <TextBlock x:Name="Clock" FontSize="32" Foreground="#e0e0e0" HorizontalAlignment="Center" />
      <TextBlock x:Name="Info" FontSize="18" Foreground="#98c379" HorizontalAlignment="Center"
                 Text="Pomeri miša ili dodirni ekran. Izlaz: Ctrl+C u SSH sesiji." />
    </StackPanel>
  </Grid>
</UserControl>
```

### `MainView.axaml.cs`

```csharp
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
```

Namerno nema obrade tastature: u DRM režimu je Avalonia ne dobija (vidi poglavlje „Tastatura").

### `MainWindow.axaml`

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="using:CloudAndBear.Desktop"
        x:Class="CloudAndBear.Desktop.MainWindow"
        Title="CloudAndBear" Width="960" Height="540">
  <local:MainView />
</Window>
```

### `MainWindow.axaml.cs`

```csharp
using Avalonia.Controls;

namespace CloudAndBear.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
```

### Provera na PC-u

```powershell
dotnet build -c Release
dotnet run
```

`build` treba da završi sa `Build succeeded` i 0 upozorenja (to je provereno). `dotnet run` treba da otvori običan prozor sa tamnom pozadinom, naslovom, satom i tekstom koji reaguje na miša. `dotnet run` nisam pokretala (otvorio bi prozor na tvom ekranu dok radiš), pa ako se prozor ne otvori, javi grešku.

## 3. Jednokratno: biblioteke na laptopu

Aplikacija je objavljena sa svim svojim .NET-om (`--self-contained`), ali u sistemu moraju postojati ove sistemske biblioteke. Spisak sam izvadila iz izgrađenih fajlova (šta traže `libSkiaSharp.so` i `Avalonia.LinuxFramebuffer.dll`):

| Biblioteka | Debian 13 paket |
|---|---|
| `libGL.so.1`, `libfontconfig.so.1` (Skia) | `libgl1`, `libfontconfig1` |
| `libEGL.so.1`, `libgbm.so.1`, `libdrm.so.2` (DRM) | `libegl1`, `libgbm1`, `libdrm2` |
| `libinput.so.10`, `libevdev.so.2` (miš, dodir) | `libinput10`, `libevdev2` |
| Mesa upravljački programi (Intel `iris`, `nouveau`) | `libgl1-mesa-dri`, `libegl-mesa0` |

Svi paketi postoje u trixie pod ovim imenima. Na laptopu (SSH):

```bash
sudo apt-get install -y libgl1 libfontconfig1 libdrm2 libegl1 libgbm1 libinput10 libevdev2 libgl1-mesa-dri libegl-mesa0
```

U VM-u je ovih 9 paketa bilo dovoljno. Poruka `VMware: No 3D enabled` u ispisu aplikacije je bezopasna (VM nema 3D, pa Mesa crta softverski).

Napomena: ovi paketi povlače i **X11 klijentske biblioteke** (`libx11-6`, `libx11-xcb1`, `libxcb*`) i Mesa. To nisu X server ni prozorski sistem, ništa od toga se ne pokreće, a tvoje pravilo („nikakvih drugih GUI-a") ostaje u snazi. Ali kad `cloudandbear-desktop` uđe u sliku, provera GUI paketa u workflow-u `iso.yml` mora da se prilagodi (sada bi označila `libgl1` i `mesa`).

### Neobavezno: da li DRM uopšte radi (pre naše aplikacije)

```bash
sudo apt-get install -y kmscube
sudo kmscube
```

Na ekranu laptopa treba da se vrti kocka. `Ctrl+C` izlazi. Ako se kocka vrti, DRM/GBM/EGL rade i ostaje samo naša aplikacija. Posle: `sudo apt-get remove -y kmscube && sudo apt-get autoremove -y` (povlači GStreamer, a nama ne treba). (`kmscube` nisam probala.)

## 4. Objava za Linux

Iz foldera `gui\CloudAndBear.Desktop`:

```powershell
dotnet publish -c Release -r linux-x64 --self-contained -o ..\publish
```

Rezultat je `gui\publish\` (oko 103 MB, 222 fajla): izvršni fajl `CloudAndBear.Desktop`, `libSkiaSharp.so` i ostalo. Nije potreban .NET na laptopu.

## 5. Prenos i pokretanje (ciklus razvoja)

Svaki put iz foldera `gui` (adresa laptopa je ona koju je `ip -br a` pokazao; ako se promeni, promeni samo prvu liniju):

```powershell
cd "D:\CloudAndBear Linux\CloudAndBearLinux\gui"
$L = "marokos@192.168.1.5"
ssh $L "rm -rf ~/cab-gui"
scp -r publish "${L}:cab-gui"
ssh $L "chmod +x ~/cab-gui/CloudAndBear.Desktop"
ssh -t $L "sudo ~/cab-gui/CloudAndBear.Desktop --drm"
```

Redom: briše staru kopiju, prenosi novu, vraća pravo izvršavanja (Windows ga ne čuva) i pokreće aplikaciju. `ssh -t` je potreban zbog lozinke za `sudo` i zbog konzole.

Pokretanje ide pod `sudo` zato što sesija preko SSH-a nema pristup `/dev/dri` i uređajima za unos. Kasnije će servis na `tty1` raditi bez `sudo`.

Šta treba da vidiš **na ekranu laptopa**: tamnu pozadinu (`#101820`), plavo „CloudAndBear", sat koji ide svake sekunde i zeleni tekst ispod. Pomeranjem miša ili touchpada tekst treba da pokazuje koordinate (ovo nije provereno).

**Izlaz:** `Ctrl+C` u SSH tabu, ili (kad dodaš poglavlje „Tastatura") `Esc`.

Bez poglavlja „Tastatura" tasteri idu u konzolu iza GUI-ja (u VM-u su se posle izlaza pojavili kao `aabababab` na promptu); sa njim ne.

### Ako ekran ostane crn, ili se javi greška oko izlaza

Laptop ima dve grafike (Intel i NVIDIA), pa Avalonia možda uzme pogrešnu karticu. Pronađi onu na kojoj je ekran laptopa (red sa `eDP` i `connected`):

```bash
for c in /sys/class/drm/card*-*; do echo "$c: $(cat $c/status)"; done
ls -l /dev/dri/
```

Ako je ekran na `card1-eDP-1`, pokreni sa:

```powershell
ssh -t $L "sudo ~/cab-gui/CloudAndBear.Desktop --drm --card=/dev/dri/card1"
```

## Tastatura (sopstvena pozadina unosa)

Avalonijin podrazumevani unos u DRM režimu (`LibInputBackend`) obrađuje samo **miša i dodir**, a tastaturu uopšte ne (provereno čitanjem biblioteke i u VM-u: tasteri stignu do `/dev/input`, ali `KeyDown` se nikad ne pokrene). Zato pišemo svoju pozadinu unosa: miš i dodir ostaju od Avalonie, a tastaturu čitamo iz `/dev/input` i šaljemo Avaloniji. Usput isključujemo obradu tastature na terminalu (`K_OFF`), da tasteri ne cure u shell iza GUI-ja.

### 1. `CloudAndBear.Desktop.csproj`

Ulazni tipovi (`RawKeyEventArgs`, `KeyboardDevice`) su kod Avalonie „privatni API", pa treba prekidač. U prvu `PropertyGroup` dodaj:

```xml
    <!-- ulazni tipovi (RawKeyEventArgs...) su kod Avalonie privatni API -->
    <AvaloniaAccessUnstablePrivateApis>true</AvaloniaAccessUnstablePrivateApis>
    <NoWarn>$(NoWarn);AVA3001</NoWarn>
```

### 2. Novi fajl `KeyboardInputBackend.cs`

(u istom folderu kao `Program.cs`)

```csharp
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LinuxFramebuffer.Input;
using Avalonia.LinuxFramebuffer.Input.LibInput;
using Avalonia.Threading;
using Microsoft.Win32.SafeHandles;

namespace CloudAndBear.Desktop;

// Unos na uređaju: miš i dodir ostaju Avalonijin LibInputBackend, a tastaturu čitamo sami iz /dev/input
// (LibInputBackend tastaturu ne obrađuje). Raspored tastature je za sada samo US.
public sealed class KeyboardInputBackend : IInputBackend
{
    private readonly LibInputBackend _pointer = new();
    private readonly KeyboardDevice _keyboard = new();
    private Action<RawInputEventArgs>? _onInput;
    private IInputRoot? _root;
    private RawInputModifiers _mods;
    private bool _caps;

    public void Initialize(IScreenInfoProvider info, Action<RawInputEventArgs> onInput)
    {
        _onInput = onInput;
        _pointer.Initialize(info, onInput);
        SilenceTerminal();

        foreach (var path in Directory.GetFiles("/dev/input", "event*").Where(IsKeyboard))
            new Thread(() => Read(path)) { IsBackground = true }.Start();
    }

    public void SetInputRoot(IInputRoot root)
    {
        _root = root;
        _pointer.SetInputRoot(root);
    }

    // ---- konzola iza GUI-ja ne sme da dobija tastere ----

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(IntPtr fd, ulong request, ref int arg);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(IntPtr fd, ulong request, IntPtr arg);

    private const ulong KDGKBMODE = 0x4B44;   // pročitaj režim tastature terminala
    private const ulong KDSKBMODE = 0x4B45;   // postavi režim tastature terminala
    private const int K_OFF = 4;              // terminal ne obrađuje tastere

    private static SafeFileHandle? _tty;
    private static int _oldMode;

    // virtuelni terminal prestaje da obrađuje tastere (inače bi isli i u shell iza GUI-ja); pri izlasku se vraća
    private static void SilenceTerminal()
    {
        try
        {
            _tty = File.OpenHandle("/dev/tty0", FileMode.Open, FileAccess.ReadWrite);
            var h = _tty.DangerousGetHandle();
            if (ioctl(h, KDGKBMODE, ref _oldMode) != 0 || ioctl(h, KDSKBMODE, (IntPtr)K_OFF) != 0) return;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreTerminal();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreTerminal();
        }
        catch
        {
            // nema /dev/tty0 ili nema prava: tasteri će i dalje ići u konzolu
        }
    }

    private static void RestoreTerminal()
    {
        if (_tty is null || _tty.IsClosed) return;
        ioctl(_tty.DangerousGetHandle(), KDSKBMODE, (IntPtr)_oldMode);
        _tty.Dispose();
    }

    // ---- čitanje tastature ----

    // tastatura ima taster A (KEY_A = 30); čita se iz sysfs-a, bez ioctl poziva
    private static bool IsKeyboard(string path)
    {
        try
        {
            var words = File.ReadAllText($"/sys/class/input/{Path.GetFileName(path)}/device/capabilities/key")
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return (Convert.ToUInt64(words[^1], 16) & (1UL << 30)) != 0;
        }
        catch
        {
            return false;
        }
    }

    private void Read(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0);
            var ev = new byte[24];   // struct input_event: 16 bajtova vreme, 2 tip, 2 kod, 4 vrednost
            while (file.Read(ev, 0, ev.Length) == ev.Length)
            {
                if (BitConverter.ToUInt16(ev, 16) == 1)   // EV_KEY
                    OnKey(BitConverter.ToUInt16(ev, 18), BitConverter.ToInt32(ev, 20));
            }
        }
        catch
        {
            // uređaj nestao ili nema pravo čitanja: preskačemo ga
        }
    }

    private void OnKey(int code, int value)   // value: 0 pušten, 1 pritisnut, 2 ponavljanje
    {
        var root = _root;
        if (root is null || !Keys.TryGetValue(code, out var k)) return;
        var down = value != 0;

        if (k.Modifier != RawInputModifiers.None)
            _mods = down ? _mods | k.Modifier : _mods & ~k.Modifier;
        if (code == 58 && value == 1) _caps = !_caps;   // Caps Lock

        var text = down ? TextFor(k) : null;
        var mods = _mods;
        var time = (ulong)Environment.TickCount64;
        var type = down ? RawKeyEventType.KeyDown : RawKeyEventType.KeyUp;

        Dispatcher.UIThread.Post(() =>
        {
            _onInput?.Invoke(new RawKeyEventArgs(_keyboard, time, root, type, k.Key, mods, k.Physical, text, KeyDeviceType.Keyboard));
            if (text is not null)
                _onInput?.Invoke(new RawTextInputEventArgs(_keyboard, time, root, text));
        });
    }

    // slovo koje taster daje (null za Ctrl/Alt/Win kombinacije i tastere bez slova)
    private string? TextFor(KeyInfo k)
    {
        if ((_mods & (RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.Meta)) != 0) return null;
        var letter = k.Normal is { Length: 1 } n && char.IsLetter(n[0]);
        return (_mods.HasFlag(RawInputModifiers.Shift) ^ (_caps && letter)) ? k.Shifted : k.Normal;
    }

    // ---- tabela tastera ----

    private readonly record struct KeyInfo(Key Key, PhysicalKey Physical, string? Normal, string? Shifted,
        RawInputModifiers Modifier = RawInputModifiers.None);

    // Linux kod tastera -> Avalonijin Key, fizički taster i slova (US raspored)
    private static readonly Dictionary<int, KeyInfo> Keys = BuildKeys();

    private static Dictionary<int, KeyInfo> BuildKeys()
    {
        var m = new Dictionary<int, KeyInfo>();

        void Letters(int first, string letters)
        {
            for (var i = 0; i < letters.Length; i++)
            {
                var up = char.ToUpper(letters[i]).ToString();
                m[first + i] = new(Enum.Parse<Key>(up), Enum.Parse<PhysicalKey>(up), letters[i].ToString(), up);
            }
        }
        Letters(16, "qwertyuiop");
        Letters(30, "asdfghjkl");
        Letters(44, "zxcvbnm");

        const string shiftedDigits = "!@#$%^&*()";
        for (var i = 0; i < 10; i++)   // kodovi 2..11 su 1,2,...,9,0
        {
            var d = (i + 1) % 10;
            m[2 + i] = new(Enum.Parse<Key>("D" + d), Enum.Parse<PhysicalKey>("Digit" + d), d.ToString(), shiftedDigits[i].ToString());
        }

        void P(int code, Key key, PhysicalKey phys, string normal, string shifted) => m[code] = new(key, phys, normal, shifted);
        P(12, Key.OemMinus, PhysicalKey.Minus, "-", "_");
        P(13, Key.OemPlus, PhysicalKey.Equal, "=", "+");
        P(26, Key.OemOpenBrackets, PhysicalKey.BracketLeft, "[", "{");
        P(27, Key.OemCloseBrackets, PhysicalKey.BracketRight, "]", "}");
        P(39, Key.OemSemicolon, PhysicalKey.Semicolon, ";", ":");
        P(40, Key.OemQuotes, PhysicalKey.Quote, "'", "\"");
        P(41, Key.OemTilde, PhysicalKey.Backquote, "`", "~");
        P(43, Key.OemPipe, PhysicalKey.Backslash, "\\", "|");
        P(51, Key.OemComma, PhysicalKey.Comma, ",", "<");
        P(52, Key.OemPeriod, PhysicalKey.Period, ".", ">");
        P(53, Key.OemQuestion, PhysicalKey.Slash, "/", "?");
        P(57, Key.Space, PhysicalKey.Space, " ", " ");

        void S(int code, Key key, PhysicalKey phys, RawInputModifiers mod = RawInputModifiers.None) => m[code] = new(key, phys, null, null, mod);
        S(1, Key.Escape, PhysicalKey.Escape);
        S(14, Key.Back, PhysicalKey.Backspace);
        S(15, Key.Tab, PhysicalKey.Tab);
        S(28, Key.Enter, PhysicalKey.Enter);
        S(58, Key.CapsLock, PhysicalKey.CapsLock);
        S(102, Key.Home, PhysicalKey.Home);
        S(103, Key.Up, PhysicalKey.ArrowUp);
        S(104, Key.PageUp, PhysicalKey.PageUp);
        S(105, Key.Left, PhysicalKey.ArrowLeft);
        S(106, Key.Right, PhysicalKey.ArrowRight);
        S(107, Key.End, PhysicalKey.End);
        S(108, Key.Down, PhysicalKey.ArrowDown);
        S(109, Key.PageDown, PhysicalKey.PageDown);
        S(110, Key.Insert, PhysicalKey.Insert);
        S(111, Key.Delete, PhysicalKey.Delete);
        S(42, Key.LeftShift, PhysicalKey.ShiftLeft, RawInputModifiers.Shift);
        S(54, Key.RightShift, PhysicalKey.ShiftRight, RawInputModifiers.Shift);
        S(29, Key.LeftCtrl, PhysicalKey.ControlLeft, RawInputModifiers.Control);
        S(97, Key.RightCtrl, PhysicalKey.ControlRight, RawInputModifiers.Control);
        S(56, Key.LeftAlt, PhysicalKey.AltLeft, RawInputModifiers.Alt);
        S(100, Key.RightAlt, PhysicalKey.AltRight, RawInputModifiers.Alt);
        S(125, Key.LWin, PhysicalKey.MetaLeft, RawInputModifiers.Meta);
        S(126, Key.RWin, PhysicalKey.MetaRight, RawInputModifiers.Meta);

        for (var i = 0; i < 10; i++)   // F1..F10 su kodovi 59..68
            S(59 + i, Enum.Parse<Key>("F" + (i + 1)), Enum.Parse<PhysicalKey>("F" + (i + 1)));
        S(87, Key.F11, PhysicalKey.F11);
        S(88, Key.F12, PhysicalKey.F12);

        return m;
    }
}
```

### 3. `Program.cs`

U `StartLinuxDrm` poziv dodaj poslednji argument `inputBackend`:

```csharp
return BuildAvaloniaApp().StartLinuxDrm(args, card: card, options: new DrmOutputOptions { Scaling = 1.0 },
    inputBackend: new KeyboardInputBackend());
```

### 4. Probni prikaz u `MainView` (privremeno, da se tastatura vidi)

U `MainView.axaml`, ispod `Info` bloka (pre `</StackPanel>`):

```xml
      <TextBlock x:Name="Typed" FontSize="28" Foreground="#e0e0e0" HorizontalAlignment="Center" />
```

U `MainView.axaml.cs` dodaj `using Avalonia.Input;` na vrh, a u konstruktor posle `PointerMoved` bloka:

```csharp
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
```

Slušanje ide na `TopLevel` jer na uređaju fokus ne mora biti na tvom pogledu.

### Šta je provereno (VM, 2026-11-15)

- Tasteri stižu do aplikacije: `Shift+h e l l o Shift+1` na ekranu daje `Hello!`, a `taster: D1` pokazuje poslednji taster.
- `Backspace` briše slovo, `Esc` zatvara aplikaciju (privremeno, preko `Environment.Exit`).
- Konzola iza GUI-ja **više ne dobija tastere** (pre toga je na promptu ostajalo `Hello!`).
- Posle izlaza tastatura konzole radi (ukucano `xzc`). Jednom se u VM-u prvi taster posle izlaza izgubio; nije ponovljeno.

### Šta nije provereno ili ne radi

- pravi laptop (u VM-u je tastatura PS/2, laptop ima drugu)
- samo **US raspored**; srpska latinica (`č ć š đ ž`) tek treba
- tastatura priključena u hodu (čitaju se samo uređaji koji postoje pri pokretanju)
- dok GUI radi, `Alt+F1..F6` (prebacivanje terminala) i `Ctrl+Alt+Del` ne rade (terminal u `K_OFF`); spas je SSH
- ako se aplikacija ubije sa `SIGKILL` ili sruši, tastatura konzole ostaje isključena: iz SSH-a `sudo kbd_mode -u -C /dev/tty0` (nije probano)
- `Alt`/`Ctrl` kombinacije ne daju tekst (namerno); `Tab` i `Enter` ne šalju tekst, samo `KeyDown`
## Ako ne radi

| Šta vidiš | Šta znači / šta raditi |
|---|---|
| `No text shaping system configured. Consider calling UseHarfBuzz()` | fali `.UseHarfBuzz()` u `BuildAvaloniaApp` (vidi `Program.cs`) |
| `cannot open shared object file: libX.so` | fali biblioteka iz poglavlja 3; proveri sa `ldd ~/cab-gui/libSkiaSharp.so \| grep "not found"` |
| `Permission denied` na `/dev/dri` ili `/dev/input` | pokrenuto bez `sudo` iz SSH-a |
| crn ekran, a program radi | pogrešna kartica, vidi gore (`--card=`) |
| program odmah izađe bez poruke | pokreni sa `ssh -t $L "sudo ~/cab-gui/CloudAndBear.Desktop --drm"` i pošalji ceo ispis |
| tekst se ne vidi, a pozadina je tu | font nije učitan: proveri da `.WithInterFont()` postoji u `BuildAvaloniaApp` |
| miš (touchpad) ne reaguje | nije provereno; javi, pa gledamo da li `libinput` vidi uređaj |
| tasteri se i dalje pojavljuju u konzoli | `SilenceTerminal` nije uspeo (nema `/dev/tty0` ili nije pokrenuto sa `sudo`) |
| tastatura konzole ne radi posle pada aplikacije | iz SSH-a: `sudo kbd_mode -u -C /dev/tty0` (nije probano) |

## Sledeći koraci (kad prvo pokretanje radi)

1. Tastatura za srpsku latinicu (`č ć š đ ž`), čist izlaz umesto `Esc`/`Environment.Exit`, i tastatura priključena u hodu.
2. Tvoj stvarni izgled umesto probnog pogleda (sve u `MainView`).
3. Čist izlaz iz aplikacije umesto gašenja signalom.
4. Systemd servis koji pokreće aplikaciju na `tty1` umesto `getty` (da ne treba `sudo`, nego prava sesije).
5. Paket `cloudandbear-desktop` (`.deb`) sa zavisnostima iz poglavlja 3, pa u sopstveni apt repozitorijum i u sliku.
6. Prilagoditi proveru GUI paketa u workflow-u za biblioteke iz poglavlja 3.
