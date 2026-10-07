using System;
using System.IO;
using Microsoft.Win32;

namespace BigPictureTV.App;

/// <summary>Finds the browser for web tiles: the one in the settings, else Microsoft Edge.</summary>
static class BrowserFinder
{
    public static string? Find(string configured)
    {
        if (configured.Trim().Length > 0)
            return File.Exists(configured.Trim()) ? configured.Trim() : null;

        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
            if (key?.GetValue(null) is string path && File.Exists(path.Trim('"'))) return path.Trim('"');
        }
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            string path = Path.Combine(Environment.GetFolderPath(folder), @"Microsoft\Edge\Application\msedge.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
