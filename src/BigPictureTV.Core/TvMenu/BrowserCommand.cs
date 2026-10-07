using System.Text;

namespace BigPictureTV.Core.TvMenu;

/// <summary>
/// The command line that opens a web tile in Edge or Chrome: an app window
/// (no tabs or address bar) in full screen, with its own profile per tile so
/// logins are kept and never mix with the user's everyday browser.
/// </summary>
public static class BrowserCommand
{
    public static string ProfileDir(string dataDir, TvApp app)
    {
        var name = new StringBuilder();
        foreach (char c in app.Name.Trim())
            name.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        string folder = name.ToString().Trim('-');
        if (folder.Length == 0) folder = "web";
        return Path.Combine(dataDir, "Browser", folder);
    }

    public static string Arguments(TvApp app, string profileDir)
    {
        var args = new List<string>
        {
            Quote("--user-data-dir=" + profileDir),
            "--no-first-run",
            "--no-default-browser-check",
            "--hide-crash-restore-bubble",
            "--start-fullscreen",
        };
        if (app.UserAgent.Trim().Length > 0) args.Add(Quote("--user-agent=" + app.UserAgent.Trim()));
        if (app.Arguments.Trim().Length > 0) args.Add(app.Arguments.Trim());
        args.Add(Quote("--app=" + app.Target.Trim()));
        return string.Join(" ", args);
    }

    // Windows command line quoting for values without quotes of their own
    // (paths, URLs and user agents); a stray quote is dropped.
    static string Quote(string value)
    {
        value = value.Replace("\"", "");
        if (value.EndsWith('\\')) value += "\\";
        return "\"" + value + "\"";
    }
}
