namespace BigPictureTV.Core;

/// <summary>Where the app keeps its files: %LOCALAPPDATA%\BigPictureTV, shared with the PowerShell script.</summary>
public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BigPictureTV");

    public static string LayoutFile => Path.Combine(DataDir, "saved-layout.json");
    public static string LogFile => Path.Combine(DataDir, "BigPictureTV.log");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
}
