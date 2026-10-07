namespace BigPictureTV.Core.TvMenu;

public enum TvAppKind
{
    /// <summary>A web page, opened full screen in its own browser window.</summary>
    Web,

    /// <summary>Any program, like Kodi or an emulator front end.</summary>
    Program,

    /// <summary>Steam Big Picture.</summary>
    BigPicture,

    /// <summary>Leaves the TV menu and goes back to the desktop.</summary>
    Desktop,
}

/// <summary>One tile of the TV menu, stored in settings.json.</summary>
public sealed class TvApp
{
    // What YouTube's TV interface (youtube.com/tv) expects: the browser of a
    // console (Cobalt, on a PS5). A desktop browser gets the normal site.
    public const string SmartTvUserAgent = "Mozilla/5.0 (Linux; Android 12) Cobalt/22.2.3-gold (PS5)";

    // Sent by the first test build; YouTube answers it with the normal site.
    static readonly string[] RetiredUserAgents =
    {
        "Mozilla/5.0 (SMART-TV; Linux; Tizen 6.0) AppleWebKit/537.36 (KHTML, like Gecko) 76.0.3809.146/6.0 TV Safari/537.36",
    };

    /// <summary>Replaces a user agent that stopped working, in tiles saved by an older version.</summary>
    public static void UpdateUserAgents(IEnumerable<TvApp> apps)
    {
        foreach (var app in apps)
            if (Array.IndexOf(RetiredUserAgents, app.UserAgent.Trim()) >= 0) app.UserAgent = SmartTvUserAgent;
    }

    /// <summary>Shown on the tile. Empty on the Desktop tile: it uses the translated name.</summary>
    public string Name { get; set; } = "";

    public TvAppKind Kind { get; set; } = TvAppKind.Web;

    /// <summary>The page's address (Web) or the program's path (Program).</summary>
    public string Target { get; set; } = "";

    /// <summary>Extra command line arguments for the program or the browser.</summary>
    public string Arguments { get; set; } = "";

    /// <summary>Web only: browser identity to send, empty for the browser's own.</summary>
    public string UserAgent { get; set; } = "";

    /// <summary>Web only: key the controller's B button sends, like "Esc" or "Alt+Left".</summary>
    public string BackKey { get; set; } = "Alt+Left";

    /// <summary>Web only: key the controller's Y button sends (search). Empty: none.</summary>
    public string SearchKey { get; set; } = "";

    /// <summary>Tile color as #RRGGBB. Empty: one from the menu's palette.</summary>
    public string Color { get; set; } = "";

    /// <summary>Tells tiles apart, to know which ones are open.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => $"{Kind}|{Name.Trim()}|{Target.Trim()}";

    public override string ToString() => Name.Length > 0 ? Name : Kind.ToString();

    public static List<TvApp> Defaults() => new()
    {
        new() { Name = "YouTube", Target = "https://www.youtube.com/tv", UserAgent = SmartTvUserAgent, BackKey = "Esc", Color = "#C4302B" },
        new() { Name = "Crunchyroll", Target = "https://www.crunchyroll.com", Color = "#F47521" },
        new() { Name = "Big Picture", Kind = TvAppKind.BigPicture, Color = "#1B2838" },
        new() { Kind = TvAppKind.Desktop, Color = "#3A3F47" },
    };

    /// <summary>True if this tile can be opened: web pages need an http(s) address, programs a path.</summary>
    public bool IsUsable => Kind switch
    {
        TvAppKind.Web => Uri.TryCreate(Target, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
        TvAppKind.Program => Target.Trim().Length > 0,
        _ => true,
    };
}
