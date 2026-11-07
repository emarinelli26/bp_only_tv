using System;
using System.Net.Http;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using BigPictureTV.Core;
using BigPictureTV.Core.Updates;

namespace BigPictureTV.App;

/// <summary>Asks GitHub for the latest release. Only tells the user; never downloads or installs.</summary>
static class UpdateChecker
{
    public const string ReleasesPage = "https://github.com/emarinelli26/bp_only_tv/releases";
    const string LatestApi = "https://api.github.com/repos/emarinelli26/bp_only_tv/releases/latest";
    const string ReleasesApi = "https://api.github.com/repos/emarinelli26/bp_only_tv/releases?per_page=20";

    /// <summary>The running version, like "1.0.0" or "2.0.0-beta.1" for a test version.</summary>
    public static string Current { get; } = CurrentVersion();

    static string CurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        // The informational version keeps the "-beta.1" the assembly version can't hold.
        var full = ReleaseVersion.ParseFull(assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        if (full is { } f) return f.Label.Length == 0 ? f.Number.ToString(3) : $"{f.Number.ToString(3)}-{f.Label}";
        var v = assembly?.GetName().Version ?? new Version(0, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0)).ToString(3);
    }

    /// <summary>The newer release's tag and page, or null if this is the latest (or GitHub can't be reached).</summary>
    public static async Task<(string Tag, string Url)?> FindNewerAsync(ILog log)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"BigPictureTV/{Current}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            // A test version also hears about newer test versions; GitHub's
            // "latest" release never is one, so those look through the list.
            bool testing = ReleaseVersion.IsTestVersion(Current);
            using var response = await http.GetAsync(testing ? ReleasesApi : LatestApi);
            if (!response.IsSuccessStatusCode) return null; // 404 until the first release
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            (string Tag, string Url)? newest = null;
            var releases = json.RootElement.ValueKind == JsonValueKind.Array
                ? json.RootElement.EnumerateArray().ToArray()
                : new[] { json.RootElement };
            foreach (var release in releases)
            {
                if (release.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
                string? tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                string url = release.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
                if (ReleaseVersion.IsNewer(tag, newest?.Tag ?? Current)) newest = (tag!, url);
            }
            return newest;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.Write($"Update check failed: {e.Message}");
            return null;
        }
    }
}
