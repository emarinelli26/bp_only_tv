using System.Diagnostics;
using System.Text;
using BigPictureTV.Core.Display;

namespace BigPictureTV.Core.Detection;

/// <summary>Answers "should the TV-only layout be on right now?".</summary>
public interface IBigPictureProbe
{
    bool IsOpen();
}

/// <summary>
/// Looks for a visible window titled like Steam Big Picture, or for any of
/// the extra processes (emulators) from the settings. Windows only.
/// </summary>
public sealed class BigPictureWatcher : IBigPictureProbe
{
    readonly string[] _titles;
    readonly string[] _processes;

    public BigPictureWatcher(IEnumerable<string> titles, IEnumerable<string> extraProcesses)
    {
        _titles = titles.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
        _processes = NormalizeProcessNames(extraProcesses);
    }

    public bool IsOpen() => AnyWindowTitled(_titles) || _processes.Any(IsRunning);

    /// <summary>Accepts "retroarch", "RetroArch.exe" or "a,b" and returns bare names.</summary>
    public static string[] NormalizeProcessNames(IEnumerable<string> names) =>
        names.SelectMany(n => n.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    static bool IsRunning(string name)
    {
        var found = Process.GetProcessesByName(name);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    static bool AnyWindowTitled(string[] titles)
    {
        if (titles.Length == 0) return false;
        bool found = false;
        var sb = new StringBuilder(256);
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            sb.Clear();
            if (NativeMethods.GetWindowText(hWnd, sb, sb.Capacity) == 0) return true;
            string title = sb.ToString();
            if (titles.Any(t => string.Equals(title, t, StringComparison.OrdinalIgnoreCase)))
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
