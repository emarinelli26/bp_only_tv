using System.Text.Json;
using System.Text.Json.Serialization;

namespace BigPictureTV.Core;

/// <summary>What "on the TV" means.</summary>
public enum TvLayout
{
    /// <summary>Only the TV is on; it is the primary display. The default.</summary>
    TvOnly,

    /// <summary>Every display stays on, but the TV becomes the primary one.</summary>
    TvPrimary,

    /// <summary>The TV and the other displays show the same picture.</summary>
    Duplicate,
}

/// <summary>User settings, stored as settings.json. Every field has a working default.</summary>
public sealed class AppSettings
{
    /// <summary>Device path of the chosen TV. Empty means "detect it automatically".</summary>
    public string TvDevicePath { get; set; } = "";

    /// <summary>Friendly name of the chosen TV, used if the device path stops matching.</summary>
    public string TvName { get; set; } = "";

    /// <summary>What switching to the TV does.</summary>
    public TvLayout Layout { get; set; } = TvLayout.TvOnly;

    /// <summary>False until the first-run setup has been shown once.</summary>
    public bool FirstRunDone { get; set; }

    /// <summary>Window titles that mean Big Picture is open.</summary>
    public List<string> BigPictureTitles { get; set; } = new() { "Steam Big Picture Mode", "Steam Big Picture" };

    /// <summary>
    /// Go back to the desktop as soon as Big Picture is minimized or hidden
    /// (Windows key, Alt+Tab, a click elsewhere). Off: only when it closes.
    /// </summary>
    public bool DesktopWhenBigPictureHidden { get; set; }

    /// <summary>
    /// Key combination that switches between the TV and the desktop, like
    /// "Ctrl+Alt+F12". Empty turns it off.
    /// </summary>
    public string Hotkey { get; set; } = Input.Hotkey.DefaultToggle.ToString();

    /// <summary>
    /// Controller buttons that, held together, switch between the TV and the
    /// desktop, like "LS+RS". Empty (the default) turns it off: controllers
    /// have their own button shortcuts, so the user picks one that is safe.
    /// </summary>
    public string ControllerCombo { get; set; } = "";

    /// <summary>
    /// Buzz the controller when the combo works. Off by default, as not
    /// every third-party controller handles vibration from outside a game.
    /// </summary>
    public bool ControllerRumble { get; set; }

    /// <summary>Process names (without .exe) that also keep the TV-only layout while they run.</summary>
    public List<string> ExtraProcesses { get; set; } = new();

    /// <summary>How long Big Picture must stay closed before the desktop comes back.</summary>
    public int GraceSeconds { get; set; } = 5;

    public int PollSeconds { get; set; } = 2;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    /// <summary>Loads settings, or returns defaults if the file is missing or broken.</summary>
    public static AppSettings Load(string file, ILog? log = null)
    {
        if (!File.Exists(file)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), JsonOptions) ?? new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            log?.Write($"Settings file unreadable ({e.Message}), using defaults.");
            return new AppSettings();
        }
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, JsonOptions));
    }
}
