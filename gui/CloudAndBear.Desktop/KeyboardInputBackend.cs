using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LinuxFramebuffer.Input;
using Avalonia.Threading;
using Microsoft.Win32.SafeHandles;

namespace CloudAndBear.Desktop;

// Unos na uređaju: miš i dodir ostaju Avalonijin LibInputBackend, a tastaturu čitamo sami iz /dev/input
// (LibInputBackend tastaturu ne obrađuje). Raspored tastature je za sada samo US.
public sealed class KeyboardInputBackend : IInputBackend
{
    private readonly PointerInput _pointer = new();
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