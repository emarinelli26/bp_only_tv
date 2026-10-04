using System;
using System.Net.Http;
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

    public static Version Current { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>The newer release's tag and page, or null if this is the latest (or GitHub can't be reached).</summary>
    public static async Task<(string Tag, string Url)?> FindNewerAsync(ILog log)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"BigPictureTV/{Current.ToString(3)}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.GetAsync(LatestApi);
            if (!response.IsSuccessStatusCode) return null; // 404 until the first release
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            string? tag = json.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            string url = json.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
            return ReleaseVersion.IsNewer(tag, Current) ? (tag!, url) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.Write($"Update check failed: {e.Message}");
            return null;
        }
    }
}
