using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BigPictureTV.App;

/// <summary>
/// Top-level windows, to tell apart the windows of one browser that shows
/// several tiles (they share a profile, so one process owns them all).
/// </summary>
static class AppWindows
{
    /// <summary>Every top-level window right now.</summary>
    public static HashSet<IntPtr> All()
    {
        var all = new HashSet<IntPtr>();
        EnumWindows((window, _) => { all.Add(window); return true; }, IntPtr.Zero);
        return all;
    }

    /// <summary>A visible window of the process that wasn't there <paramref name="before"/> and nobody claimed, or zero.</summary>
    public static IntPtr NewWindowOf(int processId, HashSet<IntPtr> before, ICollection<IntPtr> claimed)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (before.Contains(window) || claimed.Contains(window) || !IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out uint pid);
            if (pid != processId || GetWindowTextLength(window) == 0) return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static bool Exists(IntPtr window) => window != IntPtr.Zero && IsWindow(window);

    /// <summary>Exists and is on screen (minimized counts).</summary>
    public static bool IsShown(IntPtr window) => Exists(window) && IsWindowVisible(window);

    public static int ProcessOf(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint pid);
        return (int)pid;
    }

    public static string TitleOf(IntPtr window)
    {
        var text = new StringBuilder(256);
        return GetWindowText(window, text, text.Capacity) == 0 ? "" : text.ToString();
    }

    /// <summary>The desktop or the taskbar: nothing to go back to.</summary>
    public static bool IsShell(IntPtr window)
    {
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    /// <summary>Un-minimizes it and brings it to the front.</summary>
    public static void BringBack(IntPtr window)
    {
        if (!Exists(window)) return;
        if (IsIconic(window)) ShowWindow(window, SW_RESTORE);
        KeySender.ForceForeground(window);
    }

    /// <summary>True if the window covers its whole display (full screen).</summary>
    public static bool CoversItsScreen(IntPtr window)
    {
        if (!GetWindowRect(window, out var rect)) return true; // can't tell: leave it alone
        var monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return true;
        return rect.Left <= info.rcMonitor.Left && rect.Top <= info.rcMonitor.Top &&
               rect.Right >= info.rcMonitor.Right && rect.Bottom >= info.rcMonitor.Bottom;
    }

    /// <summary>Asks the window to close, like its X button.</summary>
    public static void Close(IntPtr window)
    {
        if (Exists(window)) PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    const uint WM_CLOSE = 0x0010, MONITOR_DEFAULTTONEAREST = 2;
    const int SW_RESTORE = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr window, StringBuilder name, int max);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window, int command);

    delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
