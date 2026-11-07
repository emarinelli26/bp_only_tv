using System.Diagnostics;
using System.Text;
using BigPictureTV.Core.Display;

namespace BigPictureTV.Core.Detection;

/// <summary>Answers "should the TV layout be on right now?".</summary>
public interface IBigPictureProbe
{
    bool IsOpen();
}

/// <summary>What the Big Picture window is doing right now.</summary>
public enum BigPictureWindow
{
    /// <summary>No Big Picture window (never opened, or closed).</summary>
    None,

    /// <summary>On screen, though not necessarily in front.</summary>
    Visible,

    /// <summary>Minimized to the taskbar.</summary>
    Minimized,

    /// <summary>Still exists but hidden, e.g. after the Windows key or a click on another window.</summary>
    Hidden,
}

/// <summary>One reading of everything that decides whether Big Picture counts as open.</summary>
public readonly record struct BigPictureSnapshot(
    BigPictureWindow Window,
    bool SteamDesktopVisible,
    bool ExtraProcessRunning);

/// <summary>
/// Looks for the Steam Big Picture window, or for any of the extra processes
/// (emulators) from the settings. Windows only.
/// </summary>
public sealed class BigPictureWatcher : IBigPictureProbe
{
    readonly string[] _titles;
    readonly string[] _processes;
    readonly bool _onlyWhileOnScreen;
    readonly ILog? _log;

    // The Big Picture window we last saw on screen. While it still exists it
    // counts as open even when hidden or minimized, so stepping away from Big
    // Picture (Windows key, Alt+Tab, a click elsewhere) keeps the TV.
    IntPtr _tracked;
    BigPictureSnapshot? _last;

    /// <summary>Processes of the Steam client, which own the Big Picture window.</summary>
    static readonly string[] SteamProcesses = { "steamwebhelper", "steam" };

    /// <summary>The Big Picture window being followed, if any (zero when none).</summary>
    public IntPtr Tracked => _tracked;

    /// <param name="onlyWhileOnScreen">
    /// True: Big Picture counts as closed as soon as it is minimized or hidden
    /// (the user's "go back to the desktop when I leave Big Picture" option).
    /// </param>
    public BigPictureWatcher(IEnumerable<string> titles, IEnumerable<string> extraProcesses,
        bool onlyWhileOnScreen = false, ILog? log = null)
    {
        _titles = titles.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray();
        _processes = NormalizeProcessNames(extraProcesses);
        _onlyWhileOnScreen = onlyWhileOnScreen;
        _log = log;
    }

    public bool IsOpen()
    {
        var snapshot = new BigPictureSnapshot(FindWindow(), SteamDesktopVisible(), _processes.Any(IsRunning));
        if (snapshot != _last)
        {
            _log?.Write($"Big Picture window: {snapshot.Window}; Steam desktop window visible: {snapshot.SteamDesktopVisible}; extra program running: {snapshot.ExtraProcessRunning}.");
            _last = snapshot;
        }
        return Decide(snapshot, _onlyWhileOnScreen);
    }

    /// <summary>The rule, kept apart from Win32 so it can be tested.</summary>
    public static bool Decide(BigPictureSnapshot s, bool onlyWhileOnScreen)
    {
        if (s.ExtraProcessRunning) return true;
        return s.Window switch
        {
            BigPictureWindow.Visible => true,
            // Steam showing its normal desktop window means the user left Big Picture for good.
            BigPictureWindow.Minimized or BigPictureWindow.Hidden => !onlyWhileOnScreen && !s.SteamDesktopVisible,
            _ => false,
        };
    }

    /// <summary>Accepts "retroarch", "RetroArch.exe" or "a,b" and returns bare names.</summary>
    public static string[] NormalizeProcessNames(IEnumerable<string> names) =>
        names.SelectMany(n => n.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    BigPictureWindow FindWindow()
    {
        // No titles in the settings turns the window check off (extra programs only).
        if (_titles.Length == 0) return BigPictureWindow.None;

        if (_tracked != IntPtr.Zero && NativeMethods.IsWindow(_tracked) && TitleMatches(_tracked))
            return StateOf(_tracked);
        _tracked = IntPtr.Zero;

        // Only a window seen on screen starts tracking, so a hidden window
        // Steam might keep around never counts as Big Picture being open.
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd) || !TitleMatches(hWnd)) return true;
            _tracked = hWnd;
            return false;
        }, IntPtr.Zero);
        return _tracked == IntPtr.Zero ? BigPictureWindow.None : StateOf(_tracked);
    }

    static BigPictureWindow StateOf(IntPtr hWnd) =>
        !NativeMethods.IsWindowVisible(hWnd) ? BigPictureWindow.Hidden
        : NativeMethods.IsIconic(hWnd) ? BigPictureWindow.Minimized
        : BigPictureWindow.Visible;

    /// <summary>True if this window is Big Picture's (whether or not it is on screen).</summary>
    public bool IsBigPictureWindow(IntPtr hWnd) => TitleMatches(hWnd);

    bool TitleMatches(IntPtr hWnd)
    {
        string title = TitleOf(hWnd);
        if (title.Length == 0) return false;
        if (IsBigPictureTitle(title, null, _titles)) return true;
        // Only worth asking which program owns it when the title could be Big Picture's.
        return title.Contains("Big Picture", StringComparison.OrdinalIgnoreCase)
            && IsBigPictureTitle(title, ProcessNameOf(hWnd), _titles);
    }

    /// <summary>
    /// The rule for a window title: one of the titles from the settings, or,
    /// for a window of Steam itself, any title with "Big Picture" in it, which
    /// Steam keeps in every language ("Modo Big Picture de Steam",
    /// "Steam Big Picture-Modus"...), so no setting is needed for those.
    /// </summary>
    public static bool IsBigPictureTitle(string title, string? processName, IReadOnlyCollection<string> titles)
    {
        if (title.Length == 0) return false;
        if (titles.Any(t => string.Equals(title, t, StringComparison.OrdinalIgnoreCase))) return true;
        return processName != null
            && SteamProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase)
            && title.Contains("Big Picture", StringComparison.OrdinalIgnoreCase);
    }

    static string? ProcessNameOf(IntPtr hWnd)
    {
        NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return null; // gone meanwhile
        }
    }

    // The regular Steam client window, shown when the user exits Big Picture to the desktop.
    static bool SteamDesktopVisible()
    {
        bool found = false;
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd)) return true;
            if (TitleOf(hWnd) != "Steam") return true;
            found = true;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    static string TitleOf(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        return NativeMethods.GetWindowText(hWnd, sb, sb.Capacity) == 0 ? "" : sb.ToString();
    }

    static bool IsRunning(string name)
    {
        var found = Process.GetProcessesByName(name);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }
}
