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

    /// <summary>The isolated world the script runs in, apart from the page's own scripts.</summary>
    public const string World = "tvnav";

    /// <summary>
    /// JavaScript, run in the page, that sends the script one command and
    /// says whether it handled it ("true"/"false"); null for actions it doesn't take.
    /// </summary>
    public static string? Call(PadAction action)
    {
        string? command = action switch
        {
            PadAction.Up => "up",
            PadAction.Down => "down",
            PadAction.Left => "left",
            PadAction.Right => "right",
            PadAction.Accept => "accept",
            PadAction.Search => "search",
            _ => null,
        };
        return command == null ? null :
            "(() => { const d = document.documentElement; if (!d) return false; d.removeAttribute('data-tvnav');" +
            $" document.dispatchEvent(new CustomEvent('tvnav', {{ detail: '{command}' }}));" +
            " const done = d.getAttribute('data-tvnav') === '1'; d.removeAttribute('data-tvnav'); return done; })()";
    }
}
