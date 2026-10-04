namespace BigPictureTV.Core.Display;

/// <summary>
/// Guesses which connected display is the TV, so the app works without any
/// setup. The guess is only a default; the user can always pick another one.
/// </summary>
public static class TvDetector
{
    // PnP manufacturer ids of brands that sell TVs. LG and Samsung also sell
    // monitors, so a brand match alone is a weak signal.
    static readonly HashSet<string> TvBrands = new(StringComparer.OrdinalIgnoreCase)
    {
        "GSM", // LG
        "SAM", // Samsung
        "SNY", // Sony
        "HEC", // Hisense
        "TCL", // TCL
        "VIZ", // Vizio
        "PHL", // Philips
        "MEI", // Panasonic
        "SHP", // Sharp
        "TSB", // Toshiba
        "HSN", // Hisense (older)
        "XMI", // Xiaomi
    };

    static readonly string[] TvWords = { "TV", "HDTV", "BRAVIA", "VIERA", "AQUOS" };

    /// <summary>A score for each display; higher means more likely a TV.</summary>
    public static int Score(DisplayInfo d, int displayCount)
    {
        int score = 0;
        string name = d.Name ?? "";

        if (TvWords.Any(w => ContainsWord(name, w))) score += 10;
        if (TvBrands.Contains(d.Manufacturer)) score += 3;

        score += d.Connection switch
        {
            OutputTechnology.Hdmi => 3,
            OutputTechnology.DisplayPortExternal or OutputTechnology.DisplayPortUsbTunnel => -1,
            OutputTechnology.Internal or OutputTechnology.Lvds or OutputTechnology.DisplayPortEmbedded
                or OutputTechnology.UdiEmbedded => -100, // a laptop panel is never the TV
            OutputTechnology.Miracast => 2,              // wireless display, usually a TV
            _ => 0,
        };

        if (d.Width >= 3840) score += 1;

        // With several displays the desktop's main one is usually the monitor.
        if (displayCount > 1 && d.Primary) score -= 2;

        return score;
    }

    /// <summary>The most likely TV, or null when nothing looks like one.</summary>
    public static DisplayInfo? Guess(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) return null;
        var best = displays
            .Select((d, i) => (d, i, s: Score(d, displays.Count)))
            .OrderByDescending(x => x.s)
            .ThenBy(x => x.i)
            .First();
        return best.s > 0 ? best.d : null;
    }

    static bool ContainsWord(string text, string word)
    {
        int i = 0;
        while ((i = text.IndexOf(word, i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            bool startOk = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
            int end = i + word.Length;
            bool endOk = end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (startOk && endOk) return true;
            i = end;
        }
        return false;
    }
}
