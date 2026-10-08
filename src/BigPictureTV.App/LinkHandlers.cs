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
}
