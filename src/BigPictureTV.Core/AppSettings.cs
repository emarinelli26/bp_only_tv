using System.Text.Json;

namespace BigPictureTV.Core;

/// <summary>User settings, stored as settings.json. Every field has a working default.</summary>
public sealed class AppSettings
{
    /// <summary>Device path of the chosen TV. Empty means "detect it automatically".</summary>
    public string TvDevicePath { get; set; } = "";

    /// <summary>Friendly name of the chosen TV, used if the device path stops matching.</summary>
    public string TvName { get; set; } = "";

    /// <summary>Window titles that mean Big Picture is open.</summary>
    public List<string> BigPictureTitles { get; set; } = new() { "Steam Big Picture Mode", "Steam Big Picture" };

    /// <summary>Process names (without .exe) that also keep the TV-only layout while they run.</summary>
    public List<string> ExtraProcesses { get; set; } = new();

    /// <summary>How long Big Picture must stay closed before the desktop comes back.</summary>
    public int GraceSeconds { get; set; } = 5;

    public int PollSeconds { get; set; } = 2;

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
