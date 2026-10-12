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
    /// <summary>The extension's id on the Edge Add-ons store.</summary>
    public const string EdgeStoreId = "knkhlmkoeeggiphflliamjhdpjbaioga";

    /// <summary>Where Edge gets store extensions from, for one installed by a program.</summary>
    public const string EdgeStoreUpdateUrl = "https://edge.microsoft.com/extensionwebstorebase/v1/crx";

    /// <summary>
    /// Whether a browser profile's preferences file (Preferences or Secure
    /// Preferences) shows the store's copy installed and turned on: Edge
    /// installs it off and waits for the user to turn it on, which an app
    /// window never asks. Null if the file doesn't mention it.
    /// </summary>
    public static bool? StoreCopyOn(string preferencesJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(preferencesJson);
            if (!doc.RootElement.TryGetProperty("extensions", out var extensions) || extensions.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !extensions.TryGetProperty("settings", out var settings) || settings.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !settings.TryGetProperty(EdgeStoreId, out var entry) || entry.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;
            if (entry.TryGetProperty("state", out var state) && state.ValueKind == System.Text.Json.JsonValueKind.Number && state.GetInt32() == 0) return false;
            if (entry.TryGetProperty("disable_reasons", out var reasons))
            {
                if (reasons.ValueKind == System.Text.Json.JsonValueKind.Number && reasons.GetInt64() != 0) return false;
                if (reasons.ValueKind == System.Text.Json.JsonValueKind.Array && reasons.GetArrayLength() > 0) return false;
            }
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

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
