using System;
using System.Linq;
using Microsoft.Win32;

namespace BigPictureTV.App;

/// <summary>Links like "spotify:" that an installed program opens.</summary>
static class LinkHandlers
{
    /// <summary>True for "scheme:..." that is neither a web address nor a path (C:\...).</summary>
    public static bool IsLink(string target)
    {
        int colon = target.IndexOf(':');
        return colon > 1 && !target.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
               target[..colon].All(c => char.IsLetterOrDigit(c) || c is '+' or '-' or '.');
    }

    /// <summary>True if some program registered itself to open this kind of link.</summary>
    public static bool Has(string link)
    {
        string scheme = link[..link.IndexOf(':')];
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(scheme);
            return key?.GetValue("URL Protocol") != null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true; // can't tell: let Windows try
        }
    }

    /// <summary>The program that opens this kind of link, or null (none, or a Store app).</summary>
    public static string? Program(string link)
    {
        string scheme = link[..link.IndexOf(':')];
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"{scheme}\shell\open\command");
            string command = (key?.GetValue(null) as string ?? "").Trim();
            string exe = command.StartsWith('"') ? command[1..Math.Max(1, command.IndexOf('"', 1))] : command.Split(' ')[0];
            exe = Environment.ExpandEnvironmentVariables(exe);
            return exe.Length > 0 && System.IO.File.Exists(exe) ? exe : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
