namespace BigPictureTV.Core.Updates;

/// <summary>Compares a GitHub release tag such as "v1.2.0" with the running version.</summary>
public static class ReleaseVersion
{
    /// <summary>"v1.2", "1.2.0" or "V1.2.0-beta" → 1.2.0. Null if it isn't a version.</summary>
    public static Version? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string s = tag.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) s = s[..cut];
        if (!Version.TryParse(s.Contains('.') ? s : s + ".0", out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    public static bool IsNewer(string? tag, Version current)
    {
        var latest = Parse(tag);
        var mine = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        return latest != null && latest > mine;
    }
}
