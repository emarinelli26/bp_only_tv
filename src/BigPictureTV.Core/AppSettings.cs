using System.Text.Json;
using System.Text.Json.Serialization;
using BigPictureTV.Core.TvMenu;

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

    /// <summary>Seconds to hold <see cref="ControllerCombo"/> (0 to 5; 0 is a quick tap).</summary>
    public double ControllerComboHold { get; set; } = 1.5;

    /// <summary>
    /// Buzz the controller when the combo works. Off by default, as not
    /// every third-party controller handles vibration from outside a game.
    /// </summary>
    public bool ControllerRumble { get; set; }

    /// <summary>Also move the sound to the TV while on it, and put it back after.</summary>
    public bool SwitchAudio { get; set; }

    /// <summary>Sound output to use on the TV. Empty: the one named after the TV.</summary>
    public string AudioDeviceId { get; set; } = "";

    /// <summary>The keyboard shortcut and controller combo also open Big Picture when switching to the TV.</summary>
    public bool ShortcutOpensBigPicture { get; set; }

    /// <summary>
    /// The keyboard shortcut and controller combo open the TV menu (switching
    /// to the TV first) instead of just switching. Wins over <see cref="ShortcutOpensBigPicture"/>.
    /// </summary>
    public bool ShortcutOpensTvMenu { get; set; } = true;

    /// <summary>
    /// Controller button (or buttons, like "Back+Down") that open and close
    /// the TV menu from anywhere. "Back" (View on Xbox, Share on PlayStation)
    /// by default; empty for none.
    /// </summary>
    public string ControllerMenuButton { get; set; } = "Back";

    /// <summary>Seconds to hold <see cref="ControllerMenuButton"/> (0 to 5; 0, the default, is a quick tap).</summary>
    public double ControllerMenuHold { get; set; }

    /// <summary>Tiles of the TV menu, in order.</summary>
    public List<TvApp> TvMenuApps { get; set; } = TvApp.Defaults();

    /// <summary>Which <see cref="TvApp.DefaultsVersion"/> the tiles were last brought up to (0 in files from before).</summary>
    public int TvMenuVersion { get; set; }

    /// <summary>Browser for web tiles (Edge or Chrome). Empty: Microsoft Edge, which comes with Windows.</summary>
    public string BrowserPath { get; set; } = "";

    /// <summary>Look for a new version on GitHub now and then, and say so (never installs anything).</summary>
    public bool CheckForUpdates { get; set; } = true;

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
        if (!File.Exists(file)) return Fresh();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), JsonOptions) ?? new AppSettings();
            settings.TvMenuApps ??= TvApp.Defaults();
            TvApp.UpdateUserAgents(settings.TvMenuApps);
            if (settings.TvMenuVersion < TvApp.DefaultsVersion)
            {
                // New tiles appear once; removing them afterwards sticks.
                TvApp.AddNewDefaults(settings.TvMenuApps, settings.TvMenuVersion);
                settings.TvMenuVersion = TvApp.DefaultsVersion;
                try { settings.Save(file); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log?.Write($"Couldn't save the new TV menu tiles: {e.Message}"); }
            }
            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            log?.Write($"Settings file unreadable ({e.Message}), using defaults.");
            return Fresh();
        }
    }

    static AppSettings Fresh() => new() { TvMenuVersion = TvApp.DefaultsVersion };

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, JsonOptions));
    }
}
