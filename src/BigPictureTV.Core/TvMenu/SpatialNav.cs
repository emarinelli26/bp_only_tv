namespace BigPictureTV.Core.TvMenu;

/// <summary>
/// The script (spatial-nav.js) that lets the cross move a focus box between
/// the links and buttons of web pages without a TV interface, like Crunchyroll.
/// </summary>
public static class SpatialNav
{
    static readonly Lazy<string> Source = new(() =>
    {
        using var stream = typeof(SpatialNav).Assembly.GetManifestResourceStream("spatial-nav.js")
            ?? throw new InvalidOperationException("spatial-nav.js is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string Script => Source.Value;

    /// <summary>JavaScript that runs one command and says whether the page handled it ("true"/"false").</summary>
    public static string Call(PadAction action) => action switch
    {
        PadAction.Up => Move("up"),
        PadAction.Down => Move("down"),
        PadAction.Left => Move("left"),
        PadAction.Right => Move("right"),
        PadAction.Accept => "!!(window.__tvNav && __tvNav.accept())",
        PadAction.Search => "!!(window.__tvNav && __tvNav.search())",
        _ => "false",
    };

    static string Move(string direction) => $"!!(window.__tvNav && __tvNav.move('{direction}'))";
}
