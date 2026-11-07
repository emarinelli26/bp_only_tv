using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BigPictureTV.App;

/// <summary>
/// Hears from Windows the moment a window appears, is renamed, hides,
/// minimizes or closes, so Big Picture opening or closing is noticed right
/// away instead of at the next regular check. <see cref="Interesting"/> picks
/// the windows that matter; for those, <see cref="Changed"/> is raised once
/// a short moment later (several events in a row count once). Must be created
/// on the UI thread: Windows delivers the events through its message loop.
/// </summary>
sealed class WindowEvents : IDisposable
{
    const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    const uint EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003;
    const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;
    const int OBJID_WINDOW = 0, CHILDID_SELF = 0;

    delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    static extern bool UnhookWinEvent(IntPtr hook);

    readonly WinEventProc _callback; // kept alive here: Windows calls it for as long as the hooks exist
    readonly IntPtr[] _hooks;
    readonly Timer _soon = new() { Interval = 150 };
    readonly Func<IntPtr, bool> _interesting;

    /// <summary>Raised on the UI thread shortly after a window that matters changed.</summary>
    public event Action? Changed;

    /// <param name="interesting">Whether a change to this window can matter (Big Picture's, for example). Must be quick.</param>
    public WindowEvents(Func<IntPtr, bool> interesting)
    {
        _interesting = interesting;
        _callback = OnEvent;
        _soon.Tick += (_, _) => { _soon.Stop(); Changed?.Invoke(); };
        // Separate small ranges: one wide range would also bring the flood of
        // focus and move events every window sends.
        _hooks = new[]
        {
            Hook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND),
            Hook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE),
            Hook(EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE),
        };
    }

    /// <summary>False if Windows refused every hook; the regular checks still work then.</summary>
    public bool Working => Array.Exists(_hooks, h => h != IntPtr.Zero);

    IntPtr Hook(uint min, uint max) =>
        SetWinEventHook(min, max, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

    void OnEvent(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint thread, uint time)
    {
        // Only whole windows, not the buttons, carets and other parts inside them.
        if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF || hWnd == IntPtr.Zero) return;
        if (_soon.Enabled) return;
        try
        {
            if (_interesting(hWnd)) _soon.Start();
        }
        catch (Exception)
        {
            // A window going away mid-check: the regular check catches up.
        }
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
        _soon.Dispose();
    }
}
