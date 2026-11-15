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
| touchpad laptopa (tap-to-click, skrol sa dva prsta) | virtuelni miš u VM-u radi (poglavlje „Miš i touchpad"), pravi touchpad nije probán |
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

**Izlaz:** `Ctrl+C` u SSH tabu, a posle poglavlja „Pokretanje pri dizanju" `Ctrl+Alt+Q`.

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

## Miš i touchpad (sopstvena obrada preko libinput-a)

Prvo pokretanje na laptopu je pokazalo da **miš i touchpad ne rade**. Uzrok je u Avaloniji: njen DRM unos uvozi iz `libinput` samo *apsolutno* pozicioniranje (ekrani osetljivi na dodir), dugmad i skrol, a funkcije za *relativno* pomeranje (`libinput_event_pointer_get_dx` / `_dy`), kojim rade miš i touchpad, nema. Provereno: u VM-u sam napravila virtuelni miš (`/dev/uinput`), `libinput` u aplikaciji je čitao njegove događaje, a Avalonia ih nikad nije prosledila.

Zato pišemo i pokazivač sami (isto kao tastaturu), i **menjamo** Avalonijin `LibInputBackend` našim. Cena: ekrani osetljivi na dodir tada ne rade (laptop ga nema).

### 1. Novi fajl `PointerInput.cs`

(u istom folderu kao `Program.cs`)

```csharp
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LinuxFramebuffer.Input;
using Avalonia.Threading;

namespace CloudAndBear.Desktop;

// Miš i touchpad preko libinput-a. Avalonijin LibInputBackend zna samo apsolutne uređaje (ekrane osetljive na dodir),
// a relativno pomeranje (miš, touchpad) ne obrađuje, pa to radimo sami.
public sealed class PointerInput
{
    private const string LibInput = "libinput.so.10";

    [DllImport(LibInput)] private static extern IntPtr libinput_path_create_context(IntPtr iface, IntPtr userData);
    [DllImport(LibInput)] private static extern IntPtr libinput_path_add_device(IntPtr li, string path);
    [DllImport(LibInput)] private static extern int libinput_device_config_tap_set_enabled(IntPtr device, int enable);
    [DllImport(LibInput)] private static extern int libinput_get_fd(IntPtr li);
    [DllImport(LibInput)] private static extern int libinput_dispatch(IntPtr li);
    [DllImport(LibInput)] private static extern IntPtr libinput_get_event(IntPtr li);
    [DllImport(LibInput)] private static extern int libinput_event_get_type(IntPtr ev);
    [DllImport(LibInput)] private static extern IntPtr libinput_event_get_pointer_event(IntPtr ev);
    [DllImport(LibInput)] private static extern double libinput_event_pointer_get_dx(IntPtr p);
    [DllImport(LibInput)] private static extern double libinput_event_pointer_get_dy(IntPtr p);
    [DllImport(LibInput)] private static extern uint libinput_event_pointer_get_button(IntPtr p);
    [DllImport(LibInput)] private static extern int libinput_event_pointer_get_button_state(IntPtr p);
    [DllImport(LibInput)] private static extern int libinput_event_pointer_has_axis(IntPtr p, int axis);
    [DllImport(LibInput)] private static extern double libinput_event_pointer_get_scroll_value(IntPtr p, int axis);
    [DllImport(LibInput)] private static extern double libinput_event_pointer_get_scroll_value_v120(IntPtr p, int axis);
    [DllImport(LibInput)] private static extern void libinput_event_destroy(IntPtr ev);

    [DllImport("libc")] private static extern int open(string path, int flags);
    [DllImport("libc")] private static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int poll(ref PollFd fds, ulong count, int timeoutMs);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    private delegate int OpenRestricted(IntPtr path, int flags, IntPtr userData);
    private delegate void CloseRestricted(int fd, IntPtr userData);

    // moraju ostati u poljima, inače ih sakupljač smeća ukloni dok ih libinput još poziva
    private static readonly OpenRestricted OpenCallback = (path, flags, _) => open(Marshal.PtrToStringUTF8(path)!, flags);
    private static readonly CloseRestricted CloseCallback = (fd, _) => close(fd);

    private readonly MouseDevice _mouse = new(new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true));
    private IScreenInfoProvider? _info;
    private Action<RawInputEventArgs>? _onInput;
    private IInputRoot? _root;
    private Point _pos;
    private RawInputModifiers _buttons;

    public void Initialize(IScreenInfoProvider info, Action<RawInputEventArgs> onInput)
    {
        _info = info;
        _onInput = onInput;
        _pos = new Point(info.ScaledSize.Width / 2, info.ScaledSize.Height / 2);
        new Thread(Run) { IsBackground = true }.Start();
    }

    public void SetInputRoot(IInputRoot root) => _root = root;

    private void Run()
    {
        var iface = Marshal.AllocHGlobal(2 * IntPtr.Size);   // struct libinput_interface { open_restricted, close_restricted }
        Marshal.WriteIntPtr(iface, 0, Marshal.GetFunctionPointerForDelegate(OpenCallback));
        Marshal.WriteIntPtr(iface, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(CloseCallback));

        var li = libinput_path_create_context(iface, IntPtr.Zero);
        if (li == IntPtr.Zero) return;

        foreach (var path in Directory.GetFiles("/dev/input", "event*"))
        {
            var device = libinput_path_add_device(li, path);
            if (device != IntPtr.Zero) libinput_device_config_tap_set_enabled(device, 1);   // dodir touchpada je klik
        }

        var fd = new PollFd { Fd = libinput_get_fd(li), Events = 1 };   // POLLIN
        while (true)
        {
            if (poll(ref fd, 1, -1) < 0 && Marshal.GetLastPInvokeError() != 4) break;   // 4 = EINTR, nastavi

            libinput_dispatch(li);
            IntPtr ev;
            while ((ev = libinput_get_event(li)) != IntPtr.Zero)
            {
                OnEvent(ev);
                libinput_event_destroy(ev);
            }
        }
    }

    private void OnEvent(IntPtr ev)
    {
        var type = libinput_event_get_type(ev);
        if (type is < 400 or > 406) return;   // samo događaji pokazivača

        var p = libinput_event_get_pointer_event(ev);
        switch (type)
        {
            case 400:   // POINTER_MOTION: relativno pomeranje
                Move(libinput_event_pointer_get_dx(p), libinput_event_pointer_get_dy(p));
                break;
            case 402:   // POINTER_BUTTON
                Button(libinput_event_pointer_get_button(p), libinput_event_pointer_get_button_state(p) == 1);
                break;
            case 404 or 405 or 406:   // točkić miša, skrol sa dva prsta
                Scroll(p, type);
                break;
        }
    }

    private void Move(double dx, double dy)
    {
        var size = _info!.ScaledSize;
        _pos = new Point(Math.Clamp(_pos.X + dx, 0, size.Width - 1), Math.Clamp(_pos.Y + dy, 0, size.Height - 1));
        Send(RawPointerEventType.Move);
    }

    private void Button(uint button, bool down)
    {
        var (type, flag) = button switch
        {
            0x110 => (down ? RawPointerEventType.LeftButtonDown : RawPointerEventType.LeftButtonUp, RawInputModifiers.LeftMouseButton),
            0x111 => (down ? RawPointerEventType.RightButtonDown : RawPointerEventType.RightButtonUp, RawInputModifiers.RightMouseButton),
            0x112 => (down ? RawPointerEventType.MiddleButtonDown : RawPointerEventType.MiddleButtonUp, RawInputModifiers.MiddleMouseButton),
            _ => (RawPointerEventType.Move, RawInputModifiers.None)
        };
        if (flag == RawInputModifiers.None) return;

        _buttons = down ? _buttons | flag : _buttons & ~flag;
        Send(type);
    }

    private void Scroll(IntPtr p, int type)
    {
        double Value(int axis) =>
            libinput_event_pointer_has_axis(p, axis) == 0 ? 0
            : type == 404 ? libinput_event_pointer_get_scroll_value_v120(p, axis) / 120.0   // točkić: 1 = jedan klik
            : libinput_event_pointer_get_scroll_value(p, axis) / 15.0;                      // dva prsta: pikseli

        var root = _root;
        var onInput = _onInput;
        if (root is null || onInput is null) return;

        // libinput: pozitivno = nadole/udesno; Avalonia: pozitivno = nagore/ulevo
        var delta = new Vector(-Value(1), -Value(0));
        var time = (ulong)Environment.TickCount64;
        var pos = _pos;
        var buttons = _buttons;
        Dispatcher.UIThread.Post(() => onInput(new RawMouseWheelEventArgs(_mouse, time, root, pos, delta, buttons)));
    }

    private void Send(RawPointerEventType type)
    {
        var root = _root;
        var onInput = _onInput;
        if (root is null || onInput is null) return;

        var time = (ulong)Environment.TickCount64;
        var pos = _pos;
        var buttons = _buttons;
        Dispatcher.UIThread.Post(() => onInput(new RawPointerEventArgs(_mouse, time, root, type, pos, buttons)));
    }
}
```

### 2. `KeyboardInputBackend.cs`: dve izmene

Obriši red:

```csharp
using Avalonia.LinuxFramebuffer.Input.LibInput;
```

i zameni polje `_pointer`:

```csharp
    private readonly PointerInput _pointer = new();
```

(ostali redovi koji pozivaju `_pointer.Initialize(...)` i `_pointer.SetInputRoot(...)` ostaju isti.)

### 3. Probni prikaz klika i točkića u `MainView.axaml.cs` (privremeno)

U konstruktor, iznad `AttachedToVisualTree`:

```csharp
        PointerPressed += (_, e) => Info.Text = $"klik: {e.GetCurrentPoint(this).Properties.PointerUpdateKind}";
        PointerWheelChanged += (_, e) => Info.Text = $"točkić: {e.Delta.Y}";
```

### Šta je provereno (VM, virtuelni miš, 2026-11-15)

- pomeranje: `miš: 725, 443` pa `738, 449`
- levi klik: `klik: LeftButtonPressed`
- točkić: `točkić: -1` (nadole)
- Avalonia **ne crta kursor miša** u DRM režimu (na slikama ga nema): kad dodaš prave ekrane, crtaj sopstveni kursor

### Šta nije provereno

- **pravi touchpad** laptopa: tap-to-click je uključen u kodu (`libinput_device_config_tap_set_enabled`), a skrol sa dva prsta ima konverziju u piksele (`/ 15`) koja je pogođena napamet
- desni i srednji taster, horizontalni skrol
- brzina kursora (podrazumevano ubrzanje `libinput`-a), prirodni skrol
- miš priključen u hodu (uređaji se čitaju samo pri pokretanju)

## Izgled ekrana, korak 1: okvir (traka, meni, navigacija)

Odluke (Marko, 2026-11-15): **jedna aplikacija preko celog ekrana** (traka gore, meni levo, sadržaj desno) i **kontrolna tabla kao početni ekran**. Prvi korak je samo okvir: traka sa nazivom i satom, meni sa pet odeljaka, i sadržaj izabranog odeljka. Svi odeljci su za sada prazni, a kontrolna tabla sa pravim podacima je korak 2.

Pravilo koje važi dalje: **svaki odeljak je par ViewModel + View**. `MainViewModel` drži listu odeljaka (`NavItem`), a `DataTemplate` u `MainView.axaml` kaže koji se View crta za koji ViewModel. Novi odeljak = nov ViewModel, nov View i jedan red u `DataTemplates`.

Struktura (novi folderi `ViewModels` i `Views`, a `MainView.*` ostaju gde jesu):

```
CloudAndBear.Desktop\
├── MainView.axaml / MainView.axaml.cs        (zamenjuju se)
├── ViewModels\
│   ├── ViewModelBase.cs
│   ├── NavItem.cs
│   ├── PlaceholderViewModel.cs
│   └── MainViewModel.cs
└── Views\
    ├── PlaceholderView.axaml
    └── PlaceholderView.axaml.cs
```

### `ViewModels\ViewModelBase.cs`

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CloudAndBear.Desktop.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // postavi polje i javi promenu samo ako se vrednost stvarno promenila
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
```

### `ViewModels\NavItem.cs`

```csharp
namespace CloudAndBear.Desktop.ViewModels;

// stavka menija: naziv i stranica koja se prikazuje kad je izabrana
public sealed record NavItem(string Title, ViewModelBase Page);
```

### `ViewModels\PlaceholderViewModel.cs`

```csharp
namespace CloudAndBear.Desktop.ViewModels;

// stranica koja još nije napravljena
public sealed class PlaceholderViewModel(string title) : ViewModelBase
{
    public string Title { get; } = title;
}
```

### `ViewModels\MainViewModel.cs`

```csharp
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
```

### `Views\PlaceholderView.axaml`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CloudAndBear.Desktop.ViewModels"
             x:Class="CloudAndBear.Desktop.Views.PlaceholderView"
             x:DataType="vm:PlaceholderViewModel">
  <StackPanel Spacing="12">
    <TextBlock Text="{Binding Title}" FontSize="32" FontWeight="SemiBold" Foreground="#9ecbff" />
    <TextBlock Text="Ovaj odeljak još nije napravljen." FontSize="18" Foreground="#8a99a8" />
  </StackPanel>
</UserControl>
```

### `Views\PlaceholderView.axaml.cs`

```csharp
using Avalonia.Controls;

namespace CloudAndBear.Desktop.Views;

public partial class PlaceholderView : UserControl
{
    public PlaceholderView() => InitializeComponent();
}
```

### `MainView.axaml`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CloudAndBear.Desktop.ViewModels"
             xmlns:views="using:CloudAndBear.Desktop.Views"
             x:Class="CloudAndBear.Desktop.MainView"
             x:DataType="vm:MainViewModel"
             Focusable="True"
             FocusAdorner="{x:Null}">

  <!-- koja stranica (ViewModel) se crta kojim pogledom (View) -->
  <UserControl.DataTemplates>
    <DataTemplate DataType="vm:PlaceholderViewModel">
      <views:PlaceholderView />
    </DataTemplate>
  </UserControl.DataTemplates>

  <DockPanel Background="#101820">

    <!-- gornja traka -->
    <Border DockPanel.Dock="Top" Height="56" Padding="24,0" Background="#0b1118">
      <Grid ColumnDefinitions="*,Auto">
        <TextBlock Text="C&amp;B" FontSize="26" FontWeight="SemiBold" Foreground="#9ecbff" VerticalAlignment="Center" />
        <TextBlock Grid.Column="1" Text="{Binding Clock}" FontSize="22" Foreground="#e0e0e0" VerticalAlignment="Center" />
      </Grid>
    </Border>

    <!-- meni levo -->
    <Border DockPanel.Dock="Left" Width="240" Background="#0d141c">
      <ListBox ItemsSource="{Binding Items}" SelectedItem="{Binding Selected}" Margin="12" Background="Transparent">
        <ListBox.ItemTemplate>
          <DataTemplate x:DataType="vm:NavItem">
            <TextBlock Text="{Binding Title}" FontSize="18" Padding="8,6" />
          </DataTemplate>
        </ListBox.ItemTemplate>
      </ListBox>
    </Border>

    <!-- sadržaj izabrane stranice -->
    <ContentControl Content="{Binding CurrentPage}" Margin="32" />

  </DockPanel>
</UserControl>
```

### `MainView.axaml.cs`

```csharp
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

        // na uređaju fokus ne mora biti na ovom pogledu: tastere slušamo na celom ekranu (TopLevel)
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
        };
    }
}
```
Dve zamke koje je VM pokazao (već su u kodu iznad):

- `Focus()` u `AttachedToVisualTree` i `FocusAdorner="{x:Null}"`: bez toga se **prvi pritisnuti taster samo potroši** na dodelu fokusa, a oko ekrana se pojavi okvir fokusa.
- strelice se obrađuju na `TopLevel`-u (kao i ranije `Esc`), a ne na `ListBox`-u: na uređaju fokus nije uvek na njemu.

### Šta je provereno (VM, 2026-11-15)

- ekran se crta: traka sa `C&B` i satom, meni sa istaknutom stavkom, naslov odeljka
- strelice gore/dole menjaju izabrani odeljak (i prvi pritisak odmah radi)
- prelazak mišem ističe stavku, a klik je bira (`Kontejneri` je promenio sadržaj)
- `Esc` zatvara aplikaciju

### Šta nije provereno

- pravi laptop (rezolucija 1920x1080 i hibridna grafika; u VM-u je 1280x800)
- `dotnet run` prozor na PC-u (veličina 960x540 iz `MainWindow.axaml` treba da stane)
- veličina slova na pravom ekranu (`Scaling = 1.0` u `Program.cs`; ako je sitno, probaj `1.25`)

## Izgled ekrana, korak 2: kontrolna tabla

Prvi odeljak menija dobija prave podatke: procesor (procenat), memorija, disk `/`, baterija, sistem (ime računara, kernel, vreme rada) i mreža (aktivni interfejsi sa IPv4 adresom). Osvežava se na dve sekunde.

Podela posla: **`SystemInfo`** (nov folder `Services`) samo čita sistem (`/proc`, `/sys`, `DriveInfo`, `NetworkInterface`) i vraća `null` kad podatka nema, **`DashboardViewModel`** računa i drži tekst za prikaz, **`DashboardView`** ga crta kao kartice. Na Windowsu (`dotnet run` za razvoj) rade disk, ime računara i mreža, a procesor, memorija i baterija pokazuju „nema podatka" / „nema baterije", pa prozor na PC-u ne puca.

Novi folder: `Services`. Novi fajlovi:

### `Services\SystemInfo.cs`

```csharp
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CloudAndBear.Desktop.Services;

// Stanje sistema: na Linuxu se čita iz /proc i /sys, a na Windowsu (razvoj) samo ono što .NET sam daje.
// Svaka metoda vraća null kad podatak ne postoji (npr. nema baterije), da prikaz ne pukne.
public static class SystemInfo
{
    public static string Kernel() =>
        OperatingSystem.IsLinux() ? Read("/proc/sys/kernel/osrelease") ?? "?" : Environment.OSVersion.ToString();

    public static TimeSpan Uptime()
    {
        var text = OperatingSystem.IsLinux() ? Read("/proc/uptime") : null;
        return text is not null && double.TryParse(text.Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    // (ukupno, mirovanje) iz prve linije /proc/stat; procenat se dobija iz razlike dva uzorka
    public static (long Total, long Idle)? CpuTimes()
    {
        var line = OperatingSystem.IsLinux() ? Read("/proc/stat")?.Split('\n')[0] : null;
        if (line is null || !line.StartsWith("cpu ")) return null;

        // cpu  user nice system idle iowait irq softirq steal ...
        var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(long.Parse).ToArray();
        return f.Length < 5 ? null : (f.Sum(), f[3] + f[4]);
    }

    // zauzeta i ukupna memorija u bajtovima (zauzeto = ukupno - dostupno)
    public static (long Used, long Total)? Memory()
    {
        var text = OperatingSystem.IsLinux() ? Read("/proc/meminfo") : null;
        if (text is null) return null;

        long? Kb(string key) => text.Split('\n')
            .Where(l => l.StartsWith(key + ":"))
            .Select(l => (long?)long.Parse(l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]))
            .FirstOrDefault();

        return Kb("MemTotal") is { } total && Kb("MemAvailable") is { } free
            ? ((total - free) * 1024, total * 1024)
            : null;
    }

    // zauzet i ukupan prostor glavnog diska u bajtovima
    public static (long Used, long Total)? Disk()
    {
        try
        {
            var root = OperatingSystem.IsLinux() ? "/" : Path.GetPathRoot(Environment.SystemDirectory)!;
            var d = new DriveInfo(root);
            return (d.TotalSize - d.AvailableFreeSpace, d.TotalSize);
        }
        catch
        {
            return null;
        }
    }

    // procenat napunjenosti i stanje prve baterije; null ako je nema
    public static (int Percent, string Status)? Battery()
    {
        const string dir = "/sys/class/power_supply";
        if (!OperatingSystem.IsLinux() || !Directory.Exists(dir)) return null;

        var bat = Directory.GetDirectories(dir, "BAT*").FirstOrDefault();
        if (bat is null || !int.TryParse(Read(bat + "/capacity"), out var percent)) return null;

        var status = Read(bat + "/status") switch
        {
            "Charging" => "puni se",
            "Discharging" => "prazni se",
            "Full" => "puna",
            "Not charging" => "ne puni se",
            var s => s ?? "?"
        };
        return (percent, status);
    }

    // aktivni mrežni interfejsi sa IPv4 adresom, npr. "wlp0s20f3  192.168.1.5"
    public static IReadOnlyList<string> Network()
    {
        var lines = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var a in nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                lines.Add($"{nic.Name}  {a.Address}");
        }
        return lines.Count > 0 ? lines : ["nema aktivne mreže"];
    }

    private static string? Read(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }
}
```

### `ViewModels\DashboardViewModel.cs`

```csharp
using Avalonia.Threading;
using CloudAndBear.Desktop.Services;

namespace CloudAndBear.Desktop.ViewModels;

// kontrolna tabla: stanje sistema na jednom mestu, osvežava se na dve sekunde
public sealed class DashboardViewModel : ViewModelBase
{
    private (long Total, long Idle)? _lastCpu;
    private double _cpuPercent, _memoryPercent, _diskPercent;
    private string _cpuText = "", _memoryText = "", _diskText = "", _batteryText = "", _uptimeText = "";
    private IReadOnlyList<string> _network = [];

    public DashboardViewModel()
    {
        Hostname = Environment.MachineName;
        Kernel = SystemInfo.Kernel();

        Refresh();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
    }

    public string Hostname { get; }
    public string Kernel { get; }

    public double CpuPercent { get => _cpuPercent; private set => Set(ref _cpuPercent, value); }
    public string CpuText { get => _cpuText; private set => Set(ref _cpuText, value); }

    public double MemoryPercent { get => _memoryPercent; private set => Set(ref _memoryPercent, value); }
    public string MemoryText { get => _memoryText; private set => Set(ref _memoryText, value); }

    public double DiskPercent { get => _diskPercent; private set => Set(ref _diskPercent, value); }
    public string DiskText { get => _diskText; private set => Set(ref _diskText, value); }

    public string BatteryText { get => _batteryText; private set => Set(ref _batteryText, value); }
    public string UptimeText { get => _uptimeText; private set => Set(ref _uptimeText, value); }
    public IReadOnlyList<string> Network { get => _network; private set => Set(ref _network, value); }

    private void Refresh()
    {
        // procesor: procenat zauzeća je razlika dva uzorka /proc/stat
        var cpu = SystemInfo.CpuTimes();
        if (cpu is { } now && _lastCpu is { } before && now.Total > before.Total)
            CpuPercent = 100.0 * (1 - (double)(now.Idle - before.Idle) / (now.Total - before.Total));
        _lastCpu = cpu;
        CpuText = cpu is null ? "nema podatka" : $"{CpuPercent:0} %";

        if (SystemInfo.Memory() is { } mem)
        {
            MemoryPercent = 100.0 * mem.Used / mem.Total;
            MemoryText = $"{Gb(mem.Used)} / {Gb(mem.Total)}";
        }
        else MemoryText = "nema podatka";

        if (SystemInfo.Disk() is { } disk)
        {
            DiskPercent = 100.0 * disk.Used / disk.Total;
            DiskText = $"{Gb(disk.Used)} / {Gb(disk.Total)}";
        }
        else DiskText = "nema podatka";

        BatteryText = SystemInfo.Battery() is { } bat ? $"{bat.Percent} %  ·  {bat.Status}" : "nema baterije";

        var up = SystemInfo.Uptime();
        UptimeText = $"radi {(int)up.TotalHours} h {up.Minutes} min";

        var net = SystemInfo.Network();
        if (!net.SequenceEqual(_network)) Network = net;   // lista se menja retko, ne crtamo je uzalud
    }

    private static string Gb(long bytes) => $"{bytes / 1073741824.0:0.0} GB";
}
```

### `Views\DashboardView.axaml`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CloudAndBear.Desktop.ViewModels"
             x:Class="CloudAndBear.Desktop.Views.DashboardView"
             x:DataType="vm:DashboardViewModel">

  <UserControl.Styles>
    <Style Selector="Border.card">
      <Setter Property="Background" Value="#16212d" />
      <Setter Property="CornerRadius" Value="8" />
      <Setter Property="Padding" Value="20" />
      <Setter Property="Margin" Value="0,0,16,16" />
      <Setter Property="Width" Value="300" />
    </Style>
    <Style Selector="TextBlock.label">
      <Setter Property="FontSize" Value="14" />
      <Setter Property="Foreground" Value="#8a99a8" />
    </Style>
    <Style Selector="TextBlock.value">
      <Setter Property="FontSize" Value="28" />
      <Setter Property="FontWeight" Value="SemiBold" />
      <Setter Property="Foreground" Value="#e0e0e0" />
    </Style>
    <Style Selector="ProgressBar">
      <Setter Property="Height" Value="8" />
      <Setter Property="Maximum" Value="100" />
      <Setter Property="Foreground" Value="#9ecbff" />
      <Setter Property="Background" Value="#0b1118" />
    </Style>
  </UserControl.Styles>

  <StackPanel Spacing="16">
    <TextBlock Text="Kontrolna tabla" FontSize="32" FontWeight="SemiBold" Foreground="#9ecbff" />

    <WrapPanel>

      <Border Classes="card">
        <StackPanel Spacing="8">
          <TextBlock Classes="label" Text="Procesor" />
          <TextBlock Classes="value" Text="{Binding CpuText}" />
          <ProgressBar Value="{Binding CpuPercent}" />
        </StackPanel>
      </Border>

      <Border Classes="card">
        <StackPanel Spacing="8">
          <TextBlock Classes="label" Text="Memorija" />
          <TextBlock Classes="value" Text="{Binding MemoryText}" />
          <ProgressBar Value="{Binding MemoryPercent}" />
        </StackPanel>
      </Border>

      <Border Classes="card">
        <StackPanel Spacing="8">
          <TextBlock Classes="label" Text="Disk /" />
          <TextBlock Classes="value" Text="{Binding DiskText}" />
          <ProgressBar Value="{Binding DiskPercent}" />
        </StackPanel>
      </Border>

      <Border Classes="card">
        <StackPanel Spacing="8">
          <TextBlock Classes="label" Text="Baterija" />
          <TextBlock Classes="value" Text="{Binding BatteryText}" />
        </StackPanel>
      </Border>

      <Border Classes="card">
        <StackPanel Spacing="8">
          <TextBlock Classes="label" Text="Sistem" />
          <TextBlock Classes="value" Text="{Binding Hostname}" />
          <TextBlock Classes="label" Text="{Binding Kernel}" />
          <TextBlock Classes="label" Text="{Binding UptimeText}" />
        </StackPanel>
      </Border>

      <Border Classes="card">
        <StackPanel Spacing="8">
          <TextBlock Classes="label" Text="Mreža" />
          <ItemsControl ItemsSource="{Binding Network}" TextElement.FontSize="18" />
        </StackPanel>
      </Border>

    </WrapPanel>
  </StackPanel>
</UserControl>
```

### `Views\DashboardView.axaml.cs`

```csharp
using Avalonia.Controls;

namespace CloudAndBear.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();
}
```
Dve izmene u postojećim fajlovima:

**`ViewModels\MainViewModel.cs`**: prvi red u listi `Items` postaje:

```csharp
        new("Kontrolna tabla", new DashboardViewModel()),
```

**`MainView.axaml`**: u `<UserControl.DataTemplates>`, iznad `PlaceholderViewModel` šablona:

```xml
    <DataTemplate DataType="vm:DashboardViewModel">
      <views:DashboardView />
    </DataTemplate>
```

### Šta je provereno (VM, 2026-11-15)

- brojevi se poklapaju sa sistemom: memorija `1.0 GB / 3.8 GB` (`MemTotal` 3,81 GB), disk `0.5 GB / 1.9 GB` (`overlay` 491M od 2.0G), mreža `enp0s3  10.0.2.15` (`docker0` je isključen pa se ne prikazuje), „nema baterije", kernel i ime računara
- procenat procesora reaguje: oko 5 % u mirovanju i `100 %` pod opterećenjem (dve petlje u VM-u)

### Šta nije provereno

- **baterija na pravom laptopu** (u VM-u je nema): čita se `/sys/class/power_supply/BAT*`
- rezolucija 1920x1080 (u VM-u je 1280x800): kartice su široke 300 piksela, pa na većem ekranu staje više njih u red
- `dotnet run` na PC-u (prozor 960x540: staju dve kartice u red)
- IPv6 adrese se ne prikazuju (namerno), a Wi-Fi ime mreže (SSID) tek treba

## Izgled ekrana, korak 3: kursor miša i odeljak Sistem

Dve stvari. **Kursor miša**: Avalonia ga na uređaju ne crta, pa crtamo sopstvenu strelicu (`Path` u `Canvas`-u iznad cele stranice). Prati `PointerMoved`, pojavi se tek kad se miš prvi put pomeri, i crta se samo na uređaju (`top is not Window`); u prozoru na PC-u ostaje običan kursor sistema. **Odeljak Sistem** (poslednji u meniju): podaci o računaru i napajanje: *Restartuj* i *Isključi* traže potvrdu (Da/Ne), *Izađi u konzolu* vraća prijavu na `tty1`.

Novi fajlovi: `ViewModels\RelayCommand.cs`, `ViewModels\SystemViewModel.cs`, `Views\SystemView.axaml`, `Views\SystemView.axaml.cs`.
Izmene: `Services\SystemInfo.cs` (`using` i dve metode), `ViewModels\MainViewModel.cs` (poslednji red u `Items`), `MainView.axaml` i `MainView.axaml.cs` (zameni ceo sadržaj).

### `ViewModels\RelayCommand.cs`

```csharp
using System.Windows.Input;

namespace CloudAndBear.Desktop.ViewModels;

// najjednostavnija komanda za dugme: izvrši zadatu akciju, uvek je dozvoljena
public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
```

### `ViewModels\SystemViewModel.cs`

```csharp
using System.Diagnostics;
using System.Windows.Input;
using CloudAndBear.Desktop.Services;

namespace CloudAndBear.Desktop.ViewModels;

// odeljak Sistem: podaci o računaru i napajanje (restart, isključenje, izlaz u konzolu)
public sealed class SystemViewModel : ViewModelBase
{
    private string? _pending;
    private string _message = "";

    public SystemViewModel()
    {
        Restart = new RelayCommand(() => Pending = "restart");
        Shutdown = new RelayCommand(() => Pending = "shutdown");
        ExitToConsole = new RelayCommand(() => Environment.Exit(0));
        Confirm = new RelayCommand(Run);
        Cancel = new RelayCommand(() => Pending = null);
    }

    public string OsName { get; } = SystemInfo.OsName();
    public string Kernel { get; } = SystemInfo.Kernel();
    public string Hostname { get; } = Environment.MachineName;
    public string Cpu { get; } = SystemInfo.CpuModel();
    public string TimeZone { get; } = TimeZoneInfo.Local.Id;

    public ICommand Restart { get; }
    public ICommand Shutdown { get; }
    public ICommand ExitToConsole { get; }
    public ICommand Confirm { get; }
    public ICommand Cancel { get; }

    // radnja koja čeka potvrdu ("restart" ili "shutdown"), inače null
    public string? Pending
    {
        get => _pending;
        private set
        {
            if (!Set(ref _pending, value)) return;
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(Question));
        }
    }

    public bool IsPending => Pending is not null;
    public string Question => Pending == "restart" ? "Restartovati računar?" : "Isključiti računar?";

    public string Message { get => _message; private set => Set(ref _message, value); }

    // systemctl radi samo na Linuxu; na Windowsu (razvoj) javlja grešku umesto da išta isključi
    private void Run()
    {
        var verb = Pending == "restart" ? "reboot" : "poweroff";
        Pending = null;
        try
        {
            Process.Start("systemctl", verb);
        }
        catch (Exception e)
        {
            Message = e.Message;
        }
    }
}
```

### `Services\SystemInfo.cs`: dopuna

Na vrh fajla, uz ostale `using` redove:

```csharp
using System.Runtime.InteropServices;
```

Zatim ove dve metode, odmah ispod `Kernel()` (a iznad `Uptime()`):

```csharp
    // npr. "Debian GNU/Linux 13 (trixie)"; na Windowsu naziv sistema
    public static string OsName()
    {
        var line = OperatingSystem.IsLinux()
            ? Read("/etc/os-release")?.Split('\n').FirstOrDefault(l => l.StartsWith("PRETTY_NAME="))
            : null;
        return line is null ? RuntimeInformation.OSDescription : line["PRETTY_NAME=".Length..].Trim('"');
    }

    // model procesora i broj jezgara, npr. "13th Gen Intel(R) Core(TM) i9-13900H, 20 jezgara"
    public static string CpuModel()
    {
        var model = OperatingSystem.IsLinux()
            ? Read("/proc/cpuinfo")?.Split('\n').FirstOrDefault(l => l.StartsWith("model name"))?.Split(':', 2)[1].Trim()
            : null;
        return $"{model ?? "procesor"}, {Environment.ProcessorCount} jezgara";
    }
```

### `ViewModels\MainViewModel.cs`: poslednji red u `Items`

Umesto `new("Sistem", new PlaceholderViewModel("Sistem")),` stavi:

```csharp
        new("Sistem", new SystemViewModel()),
```

### `Views\SystemView.axaml`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CloudAndBear.Desktop.ViewModels"
             x:Class="CloudAndBear.Desktop.Views.SystemView"
             x:DataType="vm:SystemViewModel">

  <UserControl.Styles>
    <Style Selector="Border.card">
      <Setter Property="Background" Value="#16212d" />
      <Setter Property="CornerRadius" Value="8" />
      <Setter Property="Padding" Value="20" />
      <Setter Property="Margin" Value="0,0,0,16" />
      <Setter Property="MaxWidth" Value="640" />
      <Setter Property="HorizontalAlignment" Value="Left" />
    </Style>
    <Style Selector="TextBlock.label">
      <Setter Property="FontSize" Value="14" />
      <Setter Property="Foreground" Value="#8a99a8" />
    </Style>
    <Style Selector="TextBlock.value">
      <Setter Property="FontSize" Value="20" />
      <Setter Property="Foreground" Value="#e0e0e0" />
    </Style>
    <Style Selector="Button">
      <Setter Property="FontSize" Value="18" />
      <Setter Property="Padding" Value="18,10" />
    </Style>
  </UserControl.Styles>

  <StackPanel Spacing="16">
    <TextBlock Text="Sistem" FontSize="32" FontWeight="SemiBold" Foreground="#9ecbff" />

    <Border Classes="card">
      <Grid ColumnDefinitions="170,*" RowDefinitions="Auto,Auto,Auto,Auto,Auto" RowSpacing="10">
        <TextBlock Classes="label" Text="Sistem" />
        <TextBlock Grid.Column="1" Classes="value" Text="{Binding OsName}" />
        <TextBlock Grid.Row="1" Classes="label" Text="Kernel" />
        <TextBlock Grid.Row="1" Grid.Column="1" Classes="value" Text="{Binding Kernel}" />
        <TextBlock Grid.Row="2" Classes="label" Text="Ime računara" />
        <TextBlock Grid.Row="2" Grid.Column="1" Classes="value" Text="{Binding Hostname}" />
        <TextBlock Grid.Row="3" Classes="label" Text="Procesor" />
        <TextBlock Grid.Row="3" Grid.Column="1" Classes="value" TextWrapping="Wrap" Text="{Binding Cpu}" />
        <TextBlock Grid.Row="4" Classes="label" Text="Vremenska zona" />
        <TextBlock Grid.Row="4" Grid.Column="1" Classes="value" Text="{Binding TimeZone}" />
      </Grid>
    </Border>

    <Border Classes="card">
      <StackPanel Spacing="14">
        <TextBlock Classes="label" Text="Napajanje" />

        <StackPanel Orientation="Horizontal" Spacing="12" IsVisible="{Binding !IsPending}">
          <Button Content="Restartuj" Command="{Binding Restart}" />
          <Button Content="Isključi" Command="{Binding Shutdown}" />
          <Button Content="Izađi u konzolu" Command="{Binding ExitToConsole}" />
        </StackPanel>

        <StackPanel Spacing="12" IsVisible="{Binding IsPending}">
          <TextBlock Classes="value" Text="{Binding Question}" />
          <StackPanel Orientation="Horizontal" Spacing="12">
            <Button Content="Da" Command="{Binding Confirm}" />
            <Button Content="Ne" Command="{Binding Cancel}" />
          </StackPanel>
        </StackPanel>

        <TextBlock Text="{Binding Message}" Foreground="#ff5f5f" TextWrapping="Wrap" />
      </StackPanel>
    </Border>
  </StackPanel>
</UserControl>
```

### `Views\SystemView.axaml.cs`

```csharp
using Avalonia.Controls;

namespace CloudAndBear.Desktop.Views;

public partial class SystemView : UserControl
{
    public SystemView() => InitializeComponent();
}
```

### `MainView.axaml`: zameni ceo sadržaj

Razlike u odnosu na korak 2: šablon za `SystemViewModel`, spoljni `Grid` i `Canvas` sa strelicom na dnu.

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CloudAndBear.Desktop.ViewModels"
             xmlns:views="using:CloudAndBear.Desktop.Views"
             x:Class="CloudAndBear.Desktop.MainView"
             x:DataType="vm:MainViewModel"
             Focusable="True"
             FocusAdorner="{x:Null}">

  <!-- koja stranica (ViewModel) se crta kojim pogledom (View) -->
  <UserControl.DataTemplates>
    <DataTemplate DataType="vm:DashboardViewModel">
      <views:DashboardView />
    </DataTemplate>
    <DataTemplate DataType="vm:SystemViewModel">
      <views:SystemView />
    </DataTemplate>
    <DataTemplate DataType="vm:PlaceholderViewModel">
      <views:PlaceholderView />
    </DataTemplate>
  </UserControl.DataTemplates>

  <Grid>
    <DockPanel Background="#101820">

      <!-- gornja traka -->
      <Border DockPanel.Dock="Top" Height="56" Padding="24,0" Background="#0b1118">
        <Grid ColumnDefinitions="*,Auto">
          <TextBlock Text="C&amp;B" FontSize="26" FontWeight="SemiBold" Foreground="#9ecbff" VerticalAlignment="Center" />
          <TextBlock Grid.Column="1" Text="{Binding Clock}" FontSize="22" Foreground="#e0e0e0" VerticalAlignment="Center" />
        </Grid>
      </Border>

      <!-- meni levo -->
      <Border DockPanel.Dock="Left" Width="240" Background="#0d141c">
        <ListBox ItemsSource="{Binding Items}" SelectedItem="{Binding Selected}" Margin="12" Background="Transparent">
          <ListBox.ItemTemplate>
            <DataTemplate x:DataType="vm:NavItem">
              <TextBlock Text="{Binding Title}" FontSize="18" Padding="8,6" />
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
      </Border>

      <!-- sadržaj izabrane stranice -->
      <ContentControl Content="{Binding CurrentPage}" Margin="32" />

    </DockPanel>

    <!-- kursor miša: Avalonia ga na uređaju ne crta -->
    <Canvas IsHitTestVisible="False">
      <Path x:Name="MouseCursor" IsVisible="False" Fill="White" Stroke="Black" StrokeThickness="1"
            Data="M0,0 L0,17 L4.5,13 L7.5,20 L10,19 L7,12 L12.5,12 Z" />
    </Canvas>
  </Grid>
</UserControl>
```

### `MainView.axaml.cs`: zameni ceo sadržaj

Razlike: izlaz je `Ctrl+Alt+Q` (kao u poglavlju o servisu) i blok koji pomera strelicu.

```csharp
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
```

### Šta je provereno (VM, 2026-11-15)

- strelica se pojavi kad se virtuelni miš pomeri, prati ga i staje na dugmad
- odeljak Sistem prikazuje `Debian GNU/Linux 13 (trixie)`, kernel, ime računara, procesor i vremensku zonu (`Europe/Belgrade`); slova `č`, `ć`, `đ` se lepo crtaju (font Inter ih ima, ne zavise od konzolnog fonta)
- klik na *Restartuj* prikaže „Restartovati računar?" sa *Da* i *Ne*; *Ne* vraća dugmad
- *Isključi* pa *Da* stvarno ugasi VM (VirtualBox javlja `poweroff`)

### Šta nije provereno

- **pravi laptop**: kursor, touchpad klik na dugmad i izgled na 1920x1080 (strelica je velika 20 piksela, pa može delovati sitno)
- *Restartuj* pa *Da* (isti poziv kao isključenje, samo `systemctl reboot`) i *Izađi u konzolu* (isti izlaz kao `Ctrl+Alt+Q`, koji je proveren)
- `systemctl` radi jer GUI trenutno radi kao `root`; kad pređemo na običan nalog trebaće polkit pravilo
- na Windowsu (`dotnet run`) nema `systemctl`: klik na *Da* ispiše grešku umesto da išta isključi
## Izgled ekrana, korak 4: Mreža, Kontejneri i Kubernetes

Ovaj korak je napisan i proveren u VM-u bez kucanja (na tvoj zahtev "za SVE"); kod je u projektu. Tri odeljka koja su bila prazna sada rade, a svaki zove spoljni alat ili Docker, pa **nijedan ne zavisi od dodatnih NuGet paketa**.

| Odeljak | Odakle podaci | Šta može |
|---|---|---|
| **Mreža** | `nmcli` (NetworkManager) | adapteri sa IP adresom, spisak Wi-Fi mreža, povezivanje (polje za lozinku), prekid veze |
| **Kontejneri** | Docker Engine API preko `/var/run/docker.sock` | spisak, pokreni, zaustavi, restartuj, obriši (sa pitanjem Da/Ne), poslednjih 8 linija ispisa izabranog kontejnera |
| **Kubernetes** | `kubectl` (samo čitanje) | konteksti iz kubeconfig-a, čvorovi, podovi svih namespace-ova (boja po stanju) |

### Šta je novo u projektu

Novi fajlovi:

- `Services\ProcessRunner.cs`: pokreće spoljni program bez ljuske (lozinke i imena sa razmacima prolaze bez navodnika), sa ograničenjem vremena; `ExitCode -1` znači "nije instaliran" ili "isteklo vreme"
- `Services\NetworkManager.cs`, `Services\DockerClient.cs`, `Services\KubeClient.cs`: pozivi i čitanje odgovora (ništa od toga ne zna za ekran)
- `ViewModels\NetworkViewModel.cs`, `ContainersViewModel.cs`, `KubernetesViewModel.cs` i `Views\NetworkView`, `ContainersView`, `KubernetesView` (`.axaml` i `.axaml.cs`)
- `ViewModels\IPage.cs`: stranica koja zna kad je otvorena (`OnShown` / `OnHidden`); skrivena stranica ne pokreće programe niti osvežava podatke

Izmenjeni fajlovi:

- `ViewModels\RelayCommand.cs`: sada ume i asinhrone komande i `CanExecute` (dugme je zatamnjeno dok nešto traje)
- `ViewModels\MainViewModel.cs`: tri nova odeljka umesto praznih strana, i javljanje `OnShown` / `OnHidden` pri promeni odeljka
- `MainView.axaml`: tri nova šablona; `MainView.axaml.cs`: `Esc` izlazi iz polja za unos, a strelice gore/dole ne menjaju odeljak dok se kuca u polju
- `KeyboardInputBackend.cs`: **slova nisu stizala u polje za unos** jer smo koristili sopstveni uređaj tastature koji ne zna za fokus; sada se koristi Avalonijin zajednički uređaj (čita se refleksijom, jer `Instance` nije javan)
- `Services\SystemInfo.cs`: `Ipv4(ime)` za adresu adaptera

### Kako radi i na šta paziti

- **Mreža**: `nmcli device wifi connect "ime" password "lozinka"`. Pre unosa nove lozinke obriše se stari profil iste mreže, jer ga nmcli inače ponovo koristi i javlja `802-11-wireless-security.key-mgmt: property is missing` (to se desilo u VM-u posle pogrešne lozinke). Neuspešan pokušaj ne ostavlja profil. Prazna lozinka znači "koristi sačuvanu vezu". Lozinka se predaje nmcli-ju kao argument pa je kratko vidljiva u `ps`; za uređaj sa jednim korisnikom to je prihvatljivo.
- **Kontejneri**: bez `docker` programa; GUI radi kao `root`, pa čita soket. Spisak se osvežava na tri sekunde dok je odeljak otvoren. Ako Docker ne radi, piše to umesto praznog spiska.
- **Kubernetes**: kontekst se zadaje po pozivu (`--context`), pa se kubeconfig **nikad ne menja**. Kubeconfig se traži: `KUBECONFIG`, pa `/root/.kube/config`, pa `/home/*/.kube/config` (GUI radi kao `root`, a tvoj fajl je u tvom kućnom folderu). Osvežava se na deset sekundi.

### Šta je provereno (VM, 2026-11-15)

- **Mreža**: virtuelni Wi-Fi (`mac80211_hwsim` + `hostapd` u posebnom mrežnom prostoru, dve mreže, jedna WPA2 i jedna otvorena): spisak se pojavi, povezivanje sa ispravnom lozinkom daje adresu i oznaku „povezan", pogrešna lozinka daje „Lozinka nedostaje ili nije tačna.", povezivanje na otvorenu mrežu, *Prekini vezu*, izbor mreže ostaje posle osvežavanja. `veth*` adapteri Dockera se ne prikazuju.
- **Kontejneri**: pravi Docker sa pet kontejnera: boje stanja, port `8080→80/tcp`, ispis izabranog kontejnera, *Zaustavi* (Docker potvrđuje `Exited (137)`), *Obriši* sa pitanjem (kontejner stvarno nestaje), poruka kad se Docker zaustavi i samooporavak kad se vrati.
- **Kubernetes**: pravi `kubectl` 1.32 sa kubeconfig-om od dva konteksta; kontekst koji ne odgovara daje pravu poruku („The connection to the server … was refused"); bez `kubectl`-a piše „kubectl nije instaliran". Čvorovi i podovi su proverani na izmišljenom, ali po obliku pravom ispisu `kubectl get ... -o json` (CrashLoopBackOff, ImagePullBackOff, Completed, Terminating, Pending, Error, `2/2`, čvor `SchedulingDisabled`).
- Kontrolna tabla i Sistem rade kao pre.

### Šta nije provereno

- **pravi Wi-Fi adapter** (laptop): vreme skeniranja, WPA3 i mreže sa korisničkim nalogom (enterprise) nisu probani; u VM-u je skeniranje trajalo oko 8 s
- **pravi Kubernetes klaster**: preuzimanje k3s-a u VM je odbijeno, pa se čitanje JSON-a oslanja na poznat oblik ispisa, ne na stvaran klaster
- dugmad *Pokreni* i *Restartuj* (isti poziv kao *Zaustavi*, samo druga putanja, nisu pojedinačno kliknuta)
- izgled na 1920x1080 (u VM-u je 1280x800)
- da li instalirani sistem na laptopu ima `NetworkManager`, `docker` i `kubectl` (u live slici su svi tu); ako neki fali, odeljak to kaže, ne puca
## Pokretanje pri dizanju (systemd servis na tty1)

Do sada si GUI pokretao rukom (`sudo ... --drm` preko SSH-a). Servis ga pokreće sam pri dizanju, umesto konzolne prijave na `tty1`, pa se laptop podiže pravo u tvoj GUI.

Izlaz iz GUI-ja je sada **`Ctrl+Alt+Q`** (umesto `Esc`, koji će ti trebati u pravim ekranima). U `MainView.axaml.cs`, u `switch`-u, zameni red sa `Key.Escape` ovim:

```csharp
                    case Key.Q when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt): Environment.Exit(0); break;   // Ctrl+Alt+Q: izlaz
```

Fajl servisa je `gui\deploy\cloudandbear-gui.service`:

```ini
[Unit]
Description=CloudAndBear GUI (Avalonia, DRM)
# uzima tty1 od konzolne prijave (getty)
After=systemd-user-sessions.service systemd-logind.service getty@tty1.service
Conflicts=getty@tty1.service
ConditionPathExists=/dev/tty0
# konzolna prijava se vraca kad GUI cisto izadje (Ctrl+Alt+Q), kad se zaustavi, i kad padne vise puta zaredom
OnSuccess=getty@tty1.service
OnFailure=getty@tty1.service
StartLimitIntervalSec=30
StartLimitBurst=3

[Service]
Environment=HOME=/root
ExecStart=/opt/cloudandbear-gui/CloudAndBear.Desktop --drm
# jedan pad se ponovo pokrece posle dve sekunde
Restart=on-failure
RestartSec=2
StandardInput=tty
StandardOutput=journal
StandardError=journal
TTYPath=/dev/tty1
TTYReset=yes
TTYVHangup=yes
TTYVTDisallocate=yes
# posle izlaza ili pada: vrati tastaturu terminala (aplikacija je gasi dok radi)
ExecStopPost=-/usr/bin/kbd_mode -u -C /dev/tty1

[Install]
WantedBy=multi-user.target
```

Šta radi, pravilo po pravilo:

- `Conflicts=getty@tty1.service` i `After=...getty@tty1.service`: servis uzima `tty1`, a konzolna prijava se gasi.
- `OnSuccess=` / `OnFailure=getty@tty1.service`: konzolna prijava se **vraća** kad GUI čisto izađe (`Ctrl+Alt+Q`), kad ga zaustaviš (`systemctl stop`) i kad padne tri puta u 30 sekundi.
- `Restart=on-failure`: **jedan** pad se ponovo pokreće posle 2 s, jer pad ne sme da te ostavi bez ekrana.
- `ExecStopPost=kbd_mode`: vraća tastaturu terminala (aplikacija je gasi dok radi), i posle izlaza i posle pada.
- GUI trenutno radi kao **root** (treba mu pristup `/dev/input`, `/dev/dri` i terminalu). Bezbednije je kasnije dati poseban nalog sa grupama `video`, `input` i `render`.

### Instalacija na laptop

Iz foldera `gui` (promenljiva `$L` je kao i ranije):

```powershell
cd "D:\CloudAndBear Linux\CloudAndBearLinux\gui\CloudAndBear.Desktop"
dotnet publish -c Release -r linux-x64 --self-contained -o ..\publish
cd ..
$L = "marokos@192.168.1.5"
ssh $L "rm -rf ~/cab-gui"
scp -r publish "${L}:cab-gui"
scp deploy\cloudandbear-gui.service "${L}:"
ssh -t $L "sudo rm -rf /opt/cloudandbear-gui && sudo cp -r ~/cab-gui /opt/cloudandbear-gui && sudo chmod +x /opt/cloudandbear-gui/CloudAndBear.Desktop && sudo cp ~/cloudandbear-gui.service /etc/systemd/system/ && sudo systemctl daemon-reload && sudo systemctl start cloudandbear-gui"
```

GUI se odmah pojavljuje na ekranu laptopa. `Ctrl+Alt+Q` ga zatvara i vraća prijavu. Za dizanje pri svakom paljenju:

```powershell
ssh -t $L "sudo systemctl enable cloudandbear-gui"
```

Za razvoj (ponovna objava dok je servis uključen): `sudo systemctl restart cloudandbear-gui` posle kopiranja u `/opt/cloudandbear-gui`. Za ručno pokretanje kao ranije prvo zaustavi servis (`sudo systemctl stop cloudandbear-gui`), jer samo jedan program sme da drži ekran.

### Ako nešto pođe naopako

- laptop se podiže bez prijave i GUI ne radi: preko SSH-a `sudo systemctl disable --now cloudandbear-gui`; konzolna prijava se vraća sama
- nema ni SSH-a: pri dizanju u GRUB-u pritisni `e`, na red `linux` dodaj ` systemd.mask=cloudandbear-gui.service` i `F10`
- tastatura konzole ne radi posle pada: `sudo kbd_mode -u -C /dev/tty0` iz SSH-a

### Šta je provereno (VM, 2026-11-15)

- `systemctl start`: GUI se pojavljuje, `getty@tty1` se gasi
- `Esc` **ne** izlazi, `Ctrl+Alt+Q` izlazi: servis postaje `inactive`, vraća se prijava na `tty1`, tastatura radi
- jedan pad (`SIGKILL`): GUI se pokreće iznova (`NRestarts=1`); više padova zaredom: servis `failed`, vraća se konzola
- `systemctl stop` vraća konzolnu prijavu
- `systemctl enable` prolazi bez greške

### Šta nije provereno

- **pokretanje pri dizanju** (`enable` pa restart): live sistem u VM-u se svaki put diže iz ISO-a, pa instalacija ne ostaje; prvi pravi test je laptop
- redosled sa mrežom i prijavom pri prvom dizanju (GUI ne zavisi od mreže)

## Ako ne radi

| Šta vidiš | Šta znači / šta raditi |
|---|---|
| `No text shaping system configured. Consider calling UseHarfBuzz()` | fali `.UseHarfBuzz()` u `BuildAvaloniaApp` (vidi `Program.cs`) |
| `cannot open shared object file: libX.so` | fali biblioteka iz poglavlja 3; proveri sa `ldd ~/cab-gui/libSkiaSharp.so \| grep "not found"` |
| `Permission denied` na `/dev/dri` ili `/dev/input` | pokrenuto bez `sudo` iz SSH-a |
| crn ekran, a program radi | pogrešna kartica, vidi gore (`--card=`) |
| program odmah izađe bez poruke | pokreni sa `ssh -t $L "sudo ~/cab-gui/CloudAndBear.Desktop --drm"` i pošalji ceo ispis |
| tekst se ne vidi, a pozadina je tu | font nije učitan: proveri da `.WithInterFont()` postoji u `BuildAvaloniaApp` |
| miš ili touchpad ne reaguje | bez poglavlja „Miš i touchpad" Avalonia ne obrađuje relativno pomeranje; sa njim proveri `ls /dev/input/event*` i da je aplikacija pokrenuta sa `sudo` |
| tasteri se i dalje pojavljuju u konzoli | `SilenceTerminal` nije uspeo (nema `/dev/tty0` ili nije pokrenuto sa `sudo`) |
| tastatura konzole ne radi posle pada aplikacije | iz SSH-a: `sudo kbd_mode -u -C /dev/tty0` (nije probano) |

## Sledeći koraci

1. Tastatura za srpsku latinicu (`č ć š đ ž`) i tastatura/miš priključeni u hodu.
2. Običan nalog umesto `root`-a za GUI (polkit za restart i isključenje; grupa `docker` za Docker; kubeconfig tog korisnika).
3. Paket `cloudandbear-desktop` (`.deb`) sa zavisnostima iz poglavlja 3, pa u sopstveni apt repozitorijum i u sliku.
4. Nova slika (pripremljeno u `debian/` i `iso.yml`, čeka izgradnju na GitHub-u): GUI i servis ugrađeni (`/opt/cloudandbear-gui`, servis uključen), 9 biblioteka i `alsa-utils`, preseed instalatera (ime `cloudandbear`, `Europe/Belgrade`, korisnik u `sudo` i `docker`, apt izvori), konzolni font sa `č ć š đ ž`, CloudAndBear naslov i pozadina menija za dizanje, GRUB ime `CloudAndBear`; `nmcli`, `docker` i `kubectl` već su u slici. Tu su i PowerShell (Microsoftov apt repozitorijum) i oh-my-posh sa tvojom temom (`minimal-marko`, u konzoli varijanta bez ikona) i profilom u `/etc/skel`; to je dodatak koji ne prekida izgradnju kad preuzimanje ne uspe, pa u logu treba potražiti `UPOZORENJE [0300 powershell]`. Posle izgradnje proveriti u VM-u, pa na laptopu.