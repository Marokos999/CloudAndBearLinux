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

| Nije proveravano ili ne radi | Zašto |
|---|---|
| **tastatura** | **ne radi**: Avalonia ga u DRM režimu ne podržava (poglavlje „Tastatura") |
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

**Izlaz:** `Ctrl+C` u SSH tabu. `Esc` ne radi, jer tastatura ne stiže do aplikacije.

**Ne kucaj ništa na tastaturi laptopa dok GUI radi:** tasteri idu u konzolu iza GUI-ja (u VM-u su se posle izlaza pojavili kao `aabababab` na promptu).

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

## Tastatura u DRM režimu (poznat problem)

Ovo je jedino što u VM-u **ne radi**, i to nije greška u tvom kodu.

- Avalonijin podrazumevani unos u DRM režimu je `LibInputBackend`. Njegovi metodi obrađuju samo **miša (`HandlePointer`) i dodir (`HandleTouch`)**, a tastaturu uopšte ne.
- Provereno: poslati tasteri su stigli do `/dev/input` i `libinput` ih je pročitao, ali `KeyDown` se u aplikaciji nikad nije pokrenuo (ni na pogledu, ni na `TopLevel`-u, ni sa `handledEventsToo`).
- Posledica 1: u aplikaciji nema tastature. Posledica 2: tasteri idu u konzolu `tty1` iza GUI-ja.

Šta se može: napisati **svoju pozadinu unosa** (`IInputBackend`, ima samo dva metoda: `Initialize(IScreenInfoProvider info, Action<RawInputEventArgs> onInput)` i `SetInputRoot(IInputRoot root)`) i predati je pozivu kao poslednji argument `StartLinuxDrm(..., inputBackend)`. Ta pozadina bi spojila Avalonijin `LibInputBackend` (miš, dodir) sa našim čitanjem tastature iz `/dev/input/event*` (događaji `EV_KEY`, kod tastera pretvoren u Avalonijin `Key`, plus tekst za unos slova). Prvi je zadatak za sledeći korak; nisam ga pisala ni probala. Provereno je samo da `StartLinuxDrm` prima `inputBackend` argument i da `IInputBackend` ima ta dva metoda.

Dok to ne postoji: aplikacija se zatvara iz SSH-a, a servis na `tty1` treba da pokreće aplikaciju sa stdin-om na toj konzoli (`StandardInput=tty`, `TTYPath=/dev/tty1`), da `SilenceConsole` guta tastere umesto da završe u shell-u.

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
| tasteri se pojavljuju u konzoli posle izlaza | očekivano, vidi „Tastatura u DRM režimu" |
| `Esc` ne zatvara aplikaciju | očekivano: tastatura ne stiže do aplikacije; izlaz je `Ctrl+C` u SSH-u |

## Sledeći koraci (kad prvo pokretanje radi)

1. Tastatura: sopstvena pozadina unosa (poglavlje iznad), jer bez nje GUI nema tastaturu.
2. Tvoj stvarni izgled umesto probnog pogleda (sve u `MainView`).
3. Čist izlaz iz aplikacije umesto gašenja signalom.
4. Systemd servis koji pokreće aplikaciju na `tty1` umesto `getty` (da ne treba `sudo`, nego prava sesije).
5. Paket `cloudandbear-desktop` (`.deb`) sa zavisnostima iz poglavlja 3, pa u sopstveni apt repozitorijum i u sliku.
6. Prilagoditi proveru GUI paketa u workflow-u za biblioteke iz poglavlja 3.
