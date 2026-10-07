namespace BigPictureTV.Core.TvMenu;

/// <summary>
/// A small browser extension (TvMenu/Extension) that lets the cross move a
/// focus box between the links and buttons of web pages without a TV
/// interface, like Crunchyroll. It's loaded only into the TV menu's browser
/// profiles. An extension, unlike a DevTools connection, passes Cloudflare's
/// bot check.
/// </summary>
public static class SpatialNav
{
    public static readonly string[] Files = { "manifest.json", "background.js", "spatial-nav.js", "player.js" };

    /// <summary>The extension's files, as shipped with the app.</summary>
    public static string Read(string file)
    {
        using var stream = typeof(SpatialNav).Assembly.GetManifestResourceStream("extension/" + file)
            ?? throw new InvalidOperationException($"{file} is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Writes the extension to a folder for the browser to load, if it changed.</summary>
    public static void Write(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in Files)
        {
            string path = Path.Combine(folder, file), text = Read(file);
            if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
        }
    }
}
