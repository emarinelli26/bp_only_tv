using System;
using BigPictureTV.Core;
using BigPictureTV.Core.TvMenu;
using Microsoft.Win32;

namespace BigPictureTV.App;

/// <summary>
/// Has Edge install the navigation extension from the Edge Add-ons store,
/// the way installed programs add extensions: a registry entry with the
/// store's address. Edge then installs it in every profile, keeps it up to
/// date, and asks once in each to turn it on. Nobody has to load it by hand.
/// </summary>
static class EdgeStore
{
    static bool _registered;

    /// <summary>True if the entry is there (written now or before).</summary>
    public static bool Register(ILog log)
    {
        if (_registered) return true;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Edge\Extensions\{SpatialNav.EdgeStoreId}");
            if (key.GetValue("update_url") as string != SpatialNav.EdgeStoreUpdateUrl)
            {
                key.SetValue("update_url", SpatialNav.EdgeStoreUpdateUrl);
                log.Write("Edge will install the TV menu navigation extension from its store.");
            }
            _registered = true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            log.Write($"Couldn't ask Edge to install the navigation extension: {e.Message}");
        }
        return _registered;
    }

    /// <summary>True if the store's copy is installed and turned on in this Edge profile (user data folder).</summary>
    public static bool OnIn(string profileDir) => StateIn(profileDir) == true;

    static readonly System.Collections.Generic.HashSet<string> Asked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Edge never turns the extension on by itself in the TV menu's profiles
    /// (it asks in a normal window, which they never show), and recent
    /// versions ignore --load-extension. So, once per profile and run, a
    /// normal window of that profile opens where one click does it: the
    /// extension's switch in edge://extensions, or its store page (Get) if
    /// Edge hasn't installed it there yet.
    /// </summary>
    public static void AskToTurnOn(string browser, string profileDir, string tile, ILog log)
    {
        bool? state = StateIn(profileDir);
        if (state == true) return;
        lock (Asked)
            if (!Asked.Add(profileDir)) return;
        string page = state == false
            ? $"edge://extensions/?id={SpatialNav.EdgeStoreId}"
            : $"https://microsoftedge.microsoft.com/addons/detail/{SpatialNav.EdgeStoreId}";
        log.Write(state == false
            ? $"{tile}: the navigation extension is installed but off in this profile; opening edge://extensions to turn it on."
            : $"{tile}: the navigation extension isn't in this profile yet; opening its store page to add it.");
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(4)); // after the tile's window, so it opens on top
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo(browser, $"\"--user-data-dir={profileDir}\" --new-window \"{page}\"") { UseShellExecute = false };
                System.Diagnostics.Process.Start(start)?.Dispose();
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                log.Write($"Couldn't open {page}: {e.Message}");
            }
        });
    }

    /// <summary>Whether the store's copy is on in this profile; null if Edge hasn't installed it there.</summary>
    static bool? StateIn(string profileDir)
    {
        bool? on = null;
        foreach (var file in new[] { "Secure Preferences", "Preferences" })
        {
            string path = System.IO.Path.Combine(profileDir, "Default", file);
            try
            {
                if (System.IO.File.Exists(path)) on ??= SpatialNav.StoreCopyOn(System.IO.File.ReadAllText(path));
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { }
        }
        return on;
    }
}
