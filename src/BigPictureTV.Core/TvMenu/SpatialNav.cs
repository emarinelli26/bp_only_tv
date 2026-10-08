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
    public static readonly string[] Files =
    {
        "manifest.json", "background.js", "spatial-nav.js", "player.js", "icon16.png", "icon48.png", "icon128.png",
    };

    /// <summary>A text file of the extension, as shipped with the app.</summary>
    public static string Read(string file) => System.Text.Encoding.UTF8.GetString(ReadBytes(file));

    /// <summary>Any file of the extension, as shipped with the app.</summary>
    public static byte[] ReadBytes(string file)
    {
        using var stream = typeof(SpatialNav).Assembly.GetManifestResourceStream("extension/" + file)
            ?? throw new InvalidOperationException($"{file} is missing from the build.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Writes the extension to a folder for the browser to load, if it changed.</summary>
    public static void Write(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in Files)
        {
            string path = Path.Combine(folder, file);
            byte[] bytes = ReadBytes(file);
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(path, bytes);
        }
    }
}
