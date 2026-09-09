using System.Runtime.InteropServices;
using Muses.Core.Platform;

namespace Muses.Platform.Windows;

/// <summary>
/// System-wide Ctrl+P / Ctrl+Left / Ctrl+Right via RegisterHotKey. Default off.
/// Does not replace in-window chords on MainWindow.
/// </summary>
public sealed class WindowsGlobalHotkeys : IGlobalHotkeys
{
    private const int HotPlay = 1;
    private const int HotPrev = 2;
    private const int HotNext = 3;
    private const uint ModControl = 0x0002;
    private const uint VkP = 0x50;
    private const uint VkLeft = 0x25;
    private const uint VkRight = 0x27;
    private const int WmHotkey = 0x0312;
    private static readonly IntPtr HwndMessage = new(-3);

    private Thread? _thread;
    private IntPtr _hwnd;
    private volatile bool _enabled;
    private volatile bool _stop;
    private WndProcDlg? _wndProc;
    private readonly AutoResetEvent _ready = new(false);

    public bool IsSupported => OperatingSystem.IsWindows();
    public bool IsEnabled => _enabled;

    public event Action? PlayPausePressed;
    public event Action? NextPressed;
    public event Action? PreviousPressed;

    public static IGlobalHotkeys Create() =>
        OperatingSystem.IsWindows() ? new WindowsGlobalHotkeys() : NullGlobalHotkeys.Instance;

    public void SetEnabled(bool enabled)
    {
        if (!IsSupported) return;
        if (enabled == _enabled) return;
        if (enabled) Start();
        else Stop();
    }

    private void Start()
    {
        _stop = false;
        _thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "Muses.GlobalHotkeys"
        };
        if (OperatingSystem.IsWindows())
            _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.WaitOne(TimeSpan.FromSeconds(2));
        _enabled = _hwnd != IntPtr.Zero;
    }

    private void Stop()
    {
        _enabled = false;
        _stop = true;
        if (_hwnd != IntPtr.Zero)
            PostMessage(_hwnd, 0x0012 /*WM_QUIT*/, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _hwnd = IntPtr.Zero;
    }

    private void Pump()
    {
        try
        {
            var className = "MusesGlobalHotkeys_" + Guid.NewGuid().ToString("N");
            _wndProc = WndProc;
            var wndClass = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = _wndProc,
                hInstance = GetModuleHandle(null),
                lpszClassName = className
            };
            var atom = RegisterClassEx(ref wndClass);
            if (atom == 0)
            {
                _ready.Set();
                return;
            }

            _hwnd = CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                _ready.Set();
                return;
            }

            RegisterHotKey(_hwnd, HotPlay, ModControl, VkP);
            RegisterHotKey(_hwnd, HotPrev, ModControl, VkLeft);
            RegisterHotKey(_hwnd, HotNext, ModControl, VkRight);
            _ready.Set();

            while (!_stop && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            UnregisterHotKey(_hwnd, HotPlay);
            UnregisterHotKey(_hwnd, HotPrev);
            UnregisterHotKey(_hwnd, HotNext);
            DestroyWindow(_hwnd);
            UnregisterClass(className, wndClass.hInstance);
        }
        catch
        {
            _ready.Set();
        }
        finally
        {
            _hwnd = IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey)
        {
            switch (wParam.ToInt32())
            {
                case HotPlay:
                    PlayPausePressed?.Invoke();
                    break;
                case HotNext:
                    NextPressed?.Invoke();
                    break;
                case HotPrev:
                    PreviousPressed?.Invoke();
                    break;
            }
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }

    private delegate IntPtr WndProcDlg(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public WndProcDlg lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x;
        public int y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
