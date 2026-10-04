using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using BigPictureTV.Core;
using BigPictureTV.Core.Input;

namespace BigPictureTV.App;

/// <summary>
/// System-wide key combinations. RegisterHotKey reserves them (so other
/// programs don't also react, and so we can tell when one is taken); a
/// low-level keyboard hook on its own thread notices them in any key order,
/// even while the app is busy switching displays.
/// </summary>
sealed class HotkeyService : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312, WM_QUIT = 0x0012, WM_APP_CHORD = 0x8000 + 1;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const int WH_KEYBOARD_LL = 13;
    const uint MOD_NOREPEAT = 0x4000;
    const int ToggleId = 1, EmergencyId = 2, ProbeId = 3;

    readonly ILog _log;
    readonly ChordDetector _chords = new();
    Thread? _hookThread;
    uint _hookThreadId;
    volatile bool _hookActive;
    bool _emergency;
    HookProc? _hookProc; // kept alive while the hook is installed

    public event Action? TogglePressed;
    public event Action? EmergencyPressed;

    /// <summary>The toggle combination registered right now, if any.</summary>
    public Hotkey? Toggle { get; private set; }

    public HotkeyService(ILog log)
    {
        _log = log;
        CreateHandle(new CreateParams());
        StartHook();
    }

    /// <summary>The emergency combination. False if another program already took it.</summary>
    public bool RegisterEmergency()
    {
        _emergency = Register(EmergencyId, Hotkey.Emergency);
        UpdateChords();
        return _emergency;
    }

    /// <summary>Replaces the toggle combination (null turns it off). False if another program already took it.</summary>
    public bool SetToggle(Hotkey? hotkey)
    {
        if (Toggle != null) UnregisterHotKey(Handle, ToggleId);
        Toggle = null;
        if (hotkey != null && Register(ToggleId, hotkey.Value)) Toggle = hotkey;
        UpdateChords();
        return hotkey == null || Toggle != null;
    }

    /// <summary>Whether a combination could be used: ours already, or free right now.</summary>
    public bool IsAvailable(Hotkey hotkey)
    {
        if (hotkey == Toggle) return true;
        if (hotkey == Hotkey.Emergency) return false;
        if (!Register(ProbeId, hotkey)) return false;
        UnregisterHotKey(Handle, ProbeId);
        return true;
    }

    bool Register(int id, Hotkey hotkey) =>
        RegisterHotKey(Handle, id, (uint)hotkey.Modifiers | MOD_NOREPEAT, hotkey.Key);

    void UpdateChords()
    {
        var list = new System.Collections.Generic.List<Hotkey>();
        if (_emergency) list.Add(Hotkey.Emergency);
        if (Toggle != null) list.Add(Toggle.Value);
        _chords.Hotkeys = list.ToArray();
    }

    protected override void WndProc(ref Message m)
    {
        // With the hook running it reports every combination; WM_HOTKEY would
        // repeat it. Without it, WM_HOTKEY is the fallback.
        int id = m.Msg switch
        {
            WM_APP_CHORD => (int)m.WParam,
            WM_HOTKEY when !_hookActive => (int)m.WParam,
            _ => 0,
        };
        switch (id)
        {
            case ToggleId: TogglePressed?.Invoke(); return;
            case EmergencyId: EmergencyPressed?.Invoke(); return;
        }
        base.WndProc(ref m);
    }

    void StartHook()
    {
        IntPtr window = Handle;
        using var ready = new ManualResetEventSlim();
        _hookThread = new Thread(() =>
        {
            _hookThreadId = GetCurrentThreadId();
            _hookProc = (code, wParam, lParam) => OnKey(code, wParam, lParam, window);
            IntPtr hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                _log.Write($"Keyboard hook unavailable ({new Win32Exception().Message}); shortcuts need the modifiers pressed first.");
            _hookActive = hook != IntPtr.Zero;
            ready.Set();
            if (hook == IntPtr.Zero) return;
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            _hookActive = false;
            UnhookWindowsHookEx(hook);
        }) { IsBackground = true, Name = "Keyboard hook" };
        _hookThread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    // Runs on the hook thread for every key press in the system: keep it short.
    IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam, IntPtr window)
    {
        if (code >= 0)
        {
            uint vk = (uint)Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode
            switch ((int)wParam)
            {
                case WM_KEYDOWN or WM_SYSKEYDOWN:
                    var hit = _chords.KeyDown(vk, k => (GetAsyncKeyState((int)k) & 0x8000) != 0);
                    if (hit != null)
                        PostMessage(window, WM_APP_CHORD, (IntPtr)(hit == Hotkey.Emergency ? EmergencyId : ToggleId), IntPtr.Zero);
                    break;
                case WM_KEYUP or WM_SYSKEYUP:
                    _chords.KeyUp(vk);
                    break;
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookThread != null && _hookThreadId != 0)
        {
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _hookThread.Join(TimeSpan.FromSeconds(2));
        }
        _hookThread = null;
        if (Handle == IntPtr.Zero) return;
        UnregisterHotKey(Handle, ToggleId);
        UnregisterHotKey(Handle, EmergencyId);
        DestroyHandle();
    }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int x, y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool PostThreadMessage(uint threadId, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();
}
