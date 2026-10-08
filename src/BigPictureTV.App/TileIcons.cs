using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BigPictureTV.Core;
using BigPictureTV.Core.TvMenu;
using Microsoft.Win32;

namespace BigPictureTV.App;

/// <summary>
/// The logo on each TV menu tile, found on its own: a web page's icon (from
/// the site, kept on disk for next time), a program's icon (Spotify, Steam
/// for Big Picture, the game in front), or the tile's own Icon setting.
/// Nothing ships with the app, so any tile someone adds gets its logo too.
/// </summary>
sealed class TileIcons
{
    static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);
    const int MaxBytes = 2 * 1024 * 1024;

    static readonly HttpClient Http = CreateHttp();

    readonly ILog _log;
    readonly string _dir = Path.Combine(AppPaths.DataDir, "Icons");
    readonly object _gate = new();
    readonly Dictionary<string, byte[]> _known = new(); // tile key: image bytes
    readonly HashSet<string> _tried = new();             // tile keys looked for (found or not), this run

    public TileIcons(ILog log) => _log = log;

    /// <summary>The logos found so far, by tile key.</summary>
    public IReadOnlyDictionary<string, byte[]> Known
    {
        get { lock (_gate) return new Dictionary<string, byte[]>(_known); }
    }

    /// <summary>Starts looking for the tiles' logos not looked for yet; <paramref name="found"/> runs (on any thread) after each one found.</summary>
    public void Prepare(IEnumerable<TvApp> apps, Action found)
    {
        foreach (var app in apps)
        {
            lock (_gate)
                if (!_tried.Add(app.Key)) continue;
            var tile = app;
            _ = Task.Run(async () =>
            {
                try
                {
                    var bytes = await FindAsync(tile);
                    if (bytes == null) return;
                    lock (_gate) _known[tile.Key] = bytes;
                    found();
                }
                catch (Exception e)
                {
                    _log.Write($"No logo for {tile}: {e.Message}");
                }
            });
        }
    }

    async Task<byte[]?> FindAsync(TvApp app)
    {
        string own = app.Icon.Trim();
        if (own.Length > 0)
        {
            if (IsWeb(own)) return await CachedAsync("url|" + own, () => DownloadAsync(new Uri(own)));
            return File.Exists(own) ? FromFile(own) : null;
        }
        switch (app.Kind)
        {
            case TvAppKind.Web when Uri.TryCreate(app.Target, UriKind.Absolute, out var page):
                return await SiteIconAsync(page);
            case TvAppKind.Program:
                string? exe = File.Exists(app.Target) ? app.Target : LinkHandlers.IsLink(app.Target) ? LinkHandlers.Program(app.Target) : null;
                if (exe != null && ExeIcon(exe) is { } icon) return icon;
                return Uri.TryCreate(app.Fallback, UriKind.Absolute, out var fallback) && IsWeb(app.Fallback) ? await SiteIconAsync(fallback) : null;
            case TvAppKind.BigPicture:
                return SteamExe() is { } steam ? ExeIcon(steam) : null;
            case TvAppKind.Game when long.TryParse(app.Target, out long handle):
                return ProgramOf(new IntPtr(handle)) is { } game ? ExeIcon(game) : null;
            default:
                return null;
        }
    }

    static bool IsWeb(string text) => text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    // The icon a site declares for itself, the largest it offers.
    Task<byte[]?> SiteIconAsync(Uri page) => CachedAsync("site|" + page.GetLeftPart(UriPartial.Authority), async () =>
    {
        string html = "";
        try
        {
            using var response = await Http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode) html = await ReadTextAsync(response);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
        foreach (var candidate in IconLinks.Candidates(html, page))
            if (await DownloadAsync(candidate) is { } bytes) return bytes;
        // A site that only answers browsers that pass its bot check
        // (Crunchyroll's Cloudflare): ask Google's icon service for it.
        return await DownloadAsync(new Uri($"https://www.google.com/s2/favicons?sz=128&domain={Uri.EscapeDataString(page.Host)}"));
    });

    // From disk if found before (and not too old), else found now and kept.
    async Task<byte[]?> CachedAsync(string source, Func<Task<byte[]?>> find)
    {
        string file = Path.Combine(_dir, Hash(source) + ".img");
        try
        {
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < KeepFor) return File.ReadAllBytes(file);
        }
        catch (IOException) { }
        var bytes = await find();
        if (bytes == null)
        {
            // Offline or blocked: an old copy beats none.
            try { return File.Exists(file) ? File.ReadAllBytes(file) : null; }
            catch (IOException) { return null; }
        }
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllBytes(file, bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return bytes;
    }

    static async Task<byte[]?> DownloadAsync(Uri uri)
    {
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return bytes.Length <= MaxBytes && IconLinks.IsImage(bytes) ? bytes : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return null; }
    }

    static async Task<string> ReadTextAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[MaxBytes];
        int read = 0, n;
        while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read))) > 0) read += n;
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    static byte[]? FromFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".exe" or ".dll" or ".lnk") return ExeIcon(path);
        try
        {
            var bytes = File.ReadAllBytes(path);
            return bytes.Length <= MaxBytes && IconLinks.IsImage(bytes) ? bytes : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A program's icon, as large as it has it (up to 256 px), as PNG.</summary>
    static byte[]? ExeIcon(string path)
    {
        var handles = new IntPtr[1];
        foreach (int size in new[] { 256, 128, 64, 48 })
        {
            if (PrivateExtractIcons(path, 0, size, size, handles, null, 1, 0) == 0 || handles[0] == IntPtr.Zero) continue;
            try
            {
                using var icon = Icon.FromHandle(handles[0]);
                using var bitmap = icon.ToBitmap();
                using var png = new MemoryStream();
                bitmap.Save(png, ImageFormat.Png);
                return png.ToArray();
            }
            finally
            {
                DestroyIcon(handles[0]);
            }
        }
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path); // a shortcut or a file without icons of its own
            if (icon == null) return null;
            using var bitmap = icon.ToBitmap();
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            return png.ToArray();
        }
        catch (Exception e) when (e is ArgumentException or IOException) { return null; }
    }

    static string? SteamExe()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string? exe = key?.GetValue("SteamExe") as string;
            return exe != null && File.Exists(exe) ? exe : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException) { return null; }
    }

    // The program behind a window (the game the menu was opened over).
    static string? ProgramOf(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint pid);
        if (pid == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.MainModule?.FileName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null; // a game running as administrator, say
        }
    }

    static string Hash(string text) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // Some sites only send their page (and icons) to a browser.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36 Edg/129.0.0.0");
        return http;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint PrivateExtractIcons(string file, int index, int width, int height, IntPtr[] icons, int[]? ids, uint count, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
