using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BigPictureTV.Core.Input;

namespace BigPictureTV.App;

/// <summary>
/// System-wide key combinations, through RegisterHotKey on a hidden message
/// window. They work whatever window has focus, Big Picture included.
/// </summary>
sealed class HotkeyService : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_NOREPEAT = 0x4000;
    const int ToggleId = 1, EmergencyId = 2, ProbeId = 3;

    public event Action? TogglePressed;
    public event Action? EmergencyPressed;

    /// <summary>The toggle combination registered right now, if any.</summary>
    public Hotkey? Toggle { get; private set; }

    public HotkeyService() => CreateHandle(new CreateParams());

    /// <summary>The emergency combination. False if another program already took it.</summary>
    public bool RegisterEmergency() => Register(EmergencyId, Hotkey.Emergency);

    /// <summary>Replaces the toggle combination (null turns it off). False if another program already took it.</summary>
    public bool SetToggle(Hotkey? hotkey)
    {
        if (Toggle != null) UnregisterHotKey(Handle, ToggleId);
        Toggle = null;
        if (hotkey == null) return true;
        if (!Register(ToggleId, hotkey.Value)) return false;
        Toggle = hotkey;
        return true;
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

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            switch ((int)m.WParam)
            {
                case ToggleId: TogglePressed?.Invoke(); return;
                case EmergencyId: EmergencyPressed?.Invoke(); return;
            }
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        UnregisterHotKey(Handle, ToggleId);
        UnregisterHotKey(Handle, EmergencyId);
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
