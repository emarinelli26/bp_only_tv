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
    public static bool OnIn(string profileDir)
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
        return on == true;
    }
}
