using System.Net;
using System.Text.RegularExpressions;

namespace BigPictureTV.Core.TvMenu;

/// <summary>
/// Finds a web page's own icon for its tile: the icons its HTML declares
/// (apple-touch-icon, icon), the largest first, then the usual fixed
/// places. SVG is left out: the menu can't draw it.
/// </summary>
public static class IconLinks
{
    static readonly Regex LinkTag = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Attribute = new(@"([a-zA-Z-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.Compiled);

    /// <summary>Addresses to try, best first, for the page at <paramref name="page"/> with this HTML.</summary>
    public static IReadOnlyList<Uri> Candidates(string html, Uri page)
    {
        var found = new List<(Uri Uri, int Size, int Order)>();
        int order = 0;
        foreach (Match tag in LinkTag.Matches(html))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match a in Attribute.Matches(tag.Value))
                attributes[a.Groups[1].Value] = WebUtility.HtmlDecode(a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value);
            if (!attributes.TryGetValue("rel", out var rel) || !attributes.TryGetValue("href", out var href)) continue;
            var rels = rel.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool touch = rels.Any(r => r.StartsWith("apple-touch-icon"));
            if (!touch && !rels.Contains("icon")) continue;
            if (attributes.TryGetValue("type", out var type) && type.Contains("svg", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(page, href.Trim(), out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) continue;
            if (uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
            attributes.TryGetValue("sizes", out var sizes);
            found.Add((uri, SizeOf(sizes, touch), order++));
        }
        var result = found.OrderByDescending(f => f.Size).ThenBy(f => f.Order).Select(f => f.Uri).ToList();
        foreach (var fixedPlace in new[] { "/apple-touch-icon.png", "/favicon.ico" })
        {
            var uri = new Uri(page, fixedPlace);
            if (!result.Contains(uri)) result.Add(uri);
        }
        return result;
    }

    // "192x192", "16x16 32x32", "any"; none given: what such icons usually are.
    static int SizeOf(string? sizes, bool touch)
    {
        int best = 0;
        foreach (var size in (sizes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = size.ToLowerInvariant().Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) best = Math.Max(best, Math.Min(w, h));
        }
        return best > 0 ? best : touch ? 180 : 32;
    }

    /// <summary>True if the bytes are an image the menu can draw (PNG, ICO, JPEG, GIF, BMP).</summary>
    public static bool IsImage(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 8 && (
            bytes[..4].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) ||
            bytes[..4].SequenceEqual(new byte[] { 0x00, 0x00, 0x01, 0x00 }) ||
            bytes[..3].SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF }) ||
            bytes[..3].SequenceEqual("GIF"u8) ||
            bytes[..2].SequenceEqual("BM"u8));
}
