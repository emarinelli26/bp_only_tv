using System.Text;
using BigPictureTV.Core.Display;

namespace BigPictureTV.Core;

/// <summary>
/// The text the "Copy diagnostic info" menu entry puts on the clipboard, for
/// bug reports: versions, displays, the TV choice, settings and recent log lines.
/// Holds nothing personal: display names and ids come from the hardware.
/// </summary>
public static class DiagnosticReport
{
    public static string Build(string appVersion, string os, IReadOnlyList<DisplayInfo> displays,
        DisplayInfo? tv, string tvReason, AppSettings settings, DisplayMode mode, IEnumerable<string> logTail)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"BigPictureTV {appVersion}");
        sb.AppendLine($"Windows: {os}");
        sb.AppendLine($"Mode: {mode}");
        sb.AppendLine();
        sb.AppendLine("Displays:");
        if (displays.Count == 0) sb.AppendLine("  (none found)");
        for (int i = 0; i < displays.Count; i++)
        {
            var d = displays[i];
            string state = d.Active ? (d.Primary ? "active, primary" : "active") : "inactive";
            string size = d.Width > 0 ? $"{d.Width}x{d.Height} at ({d.X},{d.Y})" : "-";
            sb.AppendLine($"  {i + 1}. {(d.Name.Length > 0 ? d.Name : "(no name)")} | {d.Manufacturer} | {d.Connection} | {state} | {size}");
        }
        sb.AppendLine($"TV: {tv?.ToString() ?? "not found"} ({tvReason})");
        sb.AppendLine();
        sb.AppendLine("Settings:");
        sb.AppendLine($"  Layout: {settings.Layout}; grace: {settings.GraceSeconds} s; desktop when hidden: {settings.DesktopWhenBigPictureHidden}");
        sb.AppendLine($"  Shortcut: {(settings.Hotkey.Length > 0 ? settings.Hotkey : "off")}; controller combo: {(settings.ControllerCombo.Length > 0 ? settings.ControllerCombo : "off")}");
        sb.AppendLine($"  Sound to TV: {settings.SwitchAudio}; shortcut opens Big Picture: {settings.ShortcutOpensBigPicture}; TV menu: {settings.ShortcutOpensTvMenu}; menu button: {(settings.ControllerMenuButton.Length > 0 ? settings.ControllerMenuButton : "off")}");
        sb.AppendLine($"  TV menu tiles: {string.Join(", ", settings.TvMenuApps.Select(a => $"{a} ({a.Kind})"))}");
        sb.AppendLine($"  Extra programs: {(settings.ExtraProcesses.Count > 0 ? string.Join(", ", settings.ExtraProcesses) : "none")}");
        sb.AppendLine($"  Big Picture titles: {string.Join(", ", settings.BigPictureTitles)}");
        sb.AppendLine();
        sb.AppendLine("Recent log:");
        foreach (var line in logTail) sb.AppendLine("  " + line);
        return sb.ToString();
    }

    /// <summary>The last lines of a file, or nothing if it can't be read.</summary>
    public static IReadOnlyList<string> Tail(string file, int lines)
    {
        try
        {
            var all = File.ReadAllLines(file);
            return all.Skip(Math.Max(0, all.Length - lines)).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
