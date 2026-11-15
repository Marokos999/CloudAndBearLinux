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