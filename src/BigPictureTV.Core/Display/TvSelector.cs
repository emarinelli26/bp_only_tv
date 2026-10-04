namespace BigPictureTV.Core.Display;

/// <summary>Decides which connected display is the TV.</summary>
public static class TvSelector
{
    /// <summary>
    /// In order: a name given on the command line, the display saved in the
    /// settings (by device path, then by name), then the automatic guess.
    /// </summary>
    public static (DisplayInfo? Tv, string Reason) Select(
        IReadOnlyList<DisplayInfo> displays, AppSettings settings, string? nameOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(nameOverride))
        {
            var byOverride = FindByName(displays, nameOverride);
            return (byOverride, byOverride != null ? $"matches '{nameOverride}'" : $"no display matches '{nameOverride}'");
        }

        if (!string.IsNullOrEmpty(settings.TvDevicePath))
        {
            var byPath = displays.FirstOrDefault(d =>
                string.Equals(d.DevicePath, settings.TvDevicePath, StringComparison.OrdinalIgnoreCase));
            if (byPath != null) return (byPath, "chosen in settings");
        }

        if (!string.IsNullOrWhiteSpace(settings.TvName))
        {
            var byName = FindByName(displays, settings.TvName);
            if (byName != null) return (byName, "chosen in settings (matched by name)");
        }

        bool configured = !string.IsNullOrEmpty(settings.TvDevicePath) || !string.IsNullOrWhiteSpace(settings.TvName);
        if (configured)
            return (null, $"the TV chosen in settings ('{settings.TvName}') is not connected");

        var guess = TvDetector.Guess(displays);
        return (guess, guess != null ? "detected automatically" : "no display looks like a TV");
    }

    static DisplayInfo? FindByName(IReadOnlyList<DisplayInfo> displays, string name)
    {
        name = name.Trim();
        return displays.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? displays.FirstOrDefault(d => d.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }
}
