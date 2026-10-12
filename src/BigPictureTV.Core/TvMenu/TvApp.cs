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

    /// <summary>
    /// The game (or any window) that was in front when the menu opened. The
    /// menu adds this tile on its own, like a console's game card; never saved.
    /// </summary>
    Game,
}

/// <summary>One tile of the TV menu, stored in settings.json.</summary>
public sealed class TvApp
{
    // What YouTube's TV interface (youtube.com/tv) expects: the browser of a
    // console (Cobalt, on a PS5), written the way Starboard writes it so the
    // server recognizes a PlayStation. That turns on the console's buttons
    // (Triangle searches, Square deletes). A desktop browser gets the normal site.
    public const string SmartTvUserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) Cobalt/22.lts.3-gold (unlike Gecko) v8/8.8.278.17-jit gles Starboard/13, Sony_PS5_2020/1.0 (Sony, PS5, Wired)";

    // Sent by earlier test builds. The Tizen one gets the normal site; the
    // short Cobalt one gets the TV site as a generic device, without search.
    static readonly string[] RetiredUserAgents =
    {
        "Mozilla/5.0 (SMART-TV; Linux; Tizen 6.0) AppleWebKit/537.36 (KHTML, like Gecko) 76.0.3809.146/6.0 TV Safari/537.36",
        "Mozilla/5.0 (Linux; Android 12) Cobalt/22.2.3-gold (PS5)",
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

    /// <summary>
    /// Program only: web page opened instead when the program isn't installed,
    /// for programs opened through a link like "spotify:".
    /// </summary>
    public string Fallback { get; set; } = "";

    /// <summary>
    /// Program only: name of its process (no .exe), to find it when it was
    /// opened through a link or a launcher that hands over and quits.
    /// </summary>
    public string ProcessName { get; set; } = "";

    /// <summary>
    /// Picture for the tile: an image or a program file (its icon), or a web
    /// address. Empty: the page's own icon (Web) or the program's (Program).
    /// </summary>
    public string Icon { get; set; } = "";

    /// <summary>Tile color as #RRGGBB. Empty: one from the menu's palette.</summary>
    public string Color { get; set; } = "";

    /// <summary>Tells tiles apart, to know which ones are open.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => $"{Kind}|{Name.Trim()}|{Target.Trim()}";

    public override string ToString() => Name.Length > 0 ? Name : Kind.ToString();

    /// <summary>A music tile (Spotify, YouTube Music...): the right stick turns its volume.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMusic => Kind is TvAppKind.Web or TvAppKind.Program &&
                           (MediaSessionPicker.IsMusicApp(Name) || MediaSessionPicker.IsMusicApp(Target) || MediaSessionPicker.IsMusicApp(ProcessName));

    public static List<TvApp> Defaults() => new()
    {
        new() { Name = "YouTube", Target = "https://www.youtube.com/tv", UserAgent = SmartTvUserAgent, BackKey = "Esc", Color = "#C4302B" },
        new() { Name = "Crunchyroll", Target = "https://www.crunchyroll.com", Color = "#F47521" },
        Spotify(),
        YouTubeMusic(),
        new() { Name = "Big Picture", Kind = TvAppKind.BigPicture, Color = "#1B2838" },
        new() { Kind = TvAppKind.Desktop, Color = "#3A3F47" },
    };

    // Music keeps playing behind the menu, Big Picture and games. Spotify's
    // own app if it is installed (pick songs from the phone with Spotify
    // Connect), else its web player.
    static TvApp Spotify() => new()
    {
        Name = "Spotify", Kind = TvAppKind.Program, Target = "spotify:", Fallback = "https://open.spotify.com",
        ProcessName = "Spotify", Color = "#1DB954",
    };

    static TvApp YouTubeMusic() => new() { Name = "YouTube Music", Target = "https://music.youtube.com", Color = "#FF0033" };

    /// <summary>Version of <see cref="Defaults"/>; tiles added since a version reach saved settings once (<see cref="AddNewDefaults"/>).</summary>
    public const int DefaultsVersion = 1;

    /// <summary>
    /// Adds the tiles that came with versions after <paramref name="fromVersion"/>
    /// to tiles saved before them, before the Big Picture tile, unless one
    /// with the same address is already there.
    /// </summary>
    public static void AddNewDefaults(List<TvApp> apps, int fromVersion)
    {
        var added = new List<TvApp>();
        if (fromVersion < 1) added.AddRange(new[] { Spotify(), YouTubeMusic() });
        added.RemoveAll(n => apps.Any(a => a.Kind == n.Kind && string.Equals(a.Target.Trim(), n.Target, StringComparison.OrdinalIgnoreCase)));
        if (added.Count == 0) return;
        int at = apps.FindIndex(a => a.Kind is TvAppKind.BigPicture or TvAppKind.Desktop);
        apps.InsertRange(at < 0 ? apps.Count : at, added);
    }

    /// <summary>True if this tile can be opened: web pages need an http(s) address, programs a path.</summary>
    public bool IsUsable => Kind switch
    {
        TvAppKind.Web => Uri.TryCreate(Target, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
        TvAppKind.Program => Target.Trim().Length > 0,
        _ => true,
    };
}
