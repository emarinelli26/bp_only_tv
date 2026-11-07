namespace BigPictureTV.Core.Updates;

/// <summary>
/// Compares GitHub release tags such as "v1.2.0" or "v2.0.0-beta.1" with the
/// running version, the semantic versioning way: a test version comes before
/// the final one of the same number (2.0.0-beta.1 &lt; 2.0.0).
/// </summary>
public static class ReleaseVersion
{
    /// <summary>"v1.2", "1.2.0" or "V1.2.0-beta" → 1.2.0. Null if it isn't a version.</summary>
    public static Version? Parse(string? tag) => ParseFull(tag)?.Number;

    /// <summary>
    /// "v2.0.0-beta.1+abc123" → (2.0.0, "beta.1"); the test-version label is
    /// empty for a final version. Null if it isn't a version.
    /// </summary>
    public static (Version Number, string Label)? ParseFull(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string s = tag.Trim().TrimStart('v', 'V');
        int build = s.IndexOfAny(new[] { '+', ' ' });
        if (build >= 0) s = s[..build];
        string label = "";
        int dash = s.IndexOf('-');
        if (dash >= 0) (s, label) = (s[..dash], s[(dash + 1)..]);
        if (!Version.TryParse(s.Contains('.') ? s : s + ".0", out var v)) return null;
        return (new Version(v.Major, v.Minor, Math.Max(v.Build, 0)), label);
    }

    /// <summary>True if the release <paramref name="tag"/> is newer than <paramref name="current"/> (also a tag or version text).</summary>
    public static bool IsNewer(string? tag, string current)
    {
        var latest = ParseFull(tag);
        var mine = ParseFull(current);
        return latest != null && mine != null && Compare(latest.Value, mine.Value) > 0;
    }

    public static bool IsNewer(string? tag, Version current) =>
        IsNewer(tag, new Version(current.Major, current.Minor, Math.Max(current.Build, 0)).ToString(3));

    /// <summary>True for a test version, like "2.0.0-beta.1".</summary>
    public static bool IsTestVersion(string? version) => ParseFull(version)?.Label.Length > 0;

    static int Compare((Version Number, string Label) a, (Version Number, string Label) b)
    {
        int byNumber = a.Number.CompareTo(b.Number);
        if (byNumber != 0) return byNumber;
        // The final version (no label) is newer than any test version of it.
        if (a.Label.Length == 0) return b.Label.Length == 0 ? 0 : 1;
        if (b.Label.Length == 0) return -1;

        // Both test versions: compare the dot-separated parts, numbers as numbers ("beta.10" > "beta.9").
        string[] x = a.Label.Split('.'), y = b.Label.Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            bool xNum = int.TryParse(x[i], out int xn), yNum = int.TryParse(y[i], out int yn);
            int part = xNum && yNum ? xn.CompareTo(yn)
                : xNum ? -1
                : yNum ? 1
                : string.CompareOrdinal(x[i], y[i]);
            if (part != 0) return part;
        }
        return x.Length.CompareTo(y.Length);
    }
}
