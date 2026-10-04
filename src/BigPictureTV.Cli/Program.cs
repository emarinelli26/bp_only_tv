using BigPictureTV.Core;
using BigPictureTV.Core.Detection;
using BigPictureTV.Core.Display;

// Minimal console front end for BigPictureTV.Core. Does what BigPictureTV.ps1
// does, plus automatic TV detection; the tray app will replace it.

const string Usage = """
    bptv - keep Steam Big Picture on the TV only

    Usage:
      bptv list                     Show connected displays and which one is the TV
      bptv select <number|name>     Remember which display is the TV
      bptv tv-only [--tv <name>]    Switch to the TV only now
      bptv restore                  Put the desktop layout back
      bptv watch [options]          Switch automatically while Big Picture is open (Ctrl+C to stop)

    Watch options:
      --tv <name>          Use this display instead of the saved or detected one
      --extra <a,b>        Also keep the TV on while these processes run (e.g. retroarch,dolphin)
      --grace <seconds>    Wait this long after Big Picture closes (default 5)
      --poll <seconds>     How often to check (default 2)

    Files: %LOCALAPPDATA%\BigPictureTV (settings.json, saved-layout.json, BigPictureTV.log)
    """;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("bptv only runs on Windows.");
    return 1;
}

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 1 : 0;
}

Directory.CreateDirectory(AppPaths.DataDir);
var log = new FileLog(AppPaths.LogFile);
var settings = AppSettings.Load(AppPaths.SettingsFile, log);
var display = new DisplayConfig();
var store = new LayoutStore(AppPaths.LayoutFile);

string command = args[0].ToLowerInvariant();
Dictionary<string, string> options;
List<string> positional;
try
{
    (options, positional) = ParseOptions(args.Skip(1).ToArray());
    var unknown = options.Keys.Except(new[] { "tv", "extra", "grace", "poll" }, StringComparer.OrdinalIgnoreCase).ToList();
    if (unknown.Count > 0) throw new ArgumentException($"Unknown option --{unknown[0]}.");
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
options.TryGetValue("tv", out string? tvOverride);

var manager = new DisplayManager(display, store, ds => TvSelector.Select(ds, settings, tvOverride), log);

switch (command)
{
    case "list":
        return List();
    case "select":
        return Select();
    case "tv-only":
        return manager.SwitchToTv() ? 0 : 1;
    case "restore":
        manager.RestoreDesktop();
        return 0;
    case "watch":
        return Watch();
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        Console.Error.WriteLine(Usage);
        return 1;
}

int List()
{
    var displays = display.ListDisplays();
    var (tv, reason) = TvSelector.Select(displays, settings, tvOverride);
    Console.WriteLine($"{"#",-3}{"Name",-22}{"Active",-8}{"Primary",-9}{"Connection",-22}{"Resolution",-12}TV score");
    for (int i = 0; i < displays.Count; i++)
    {
        var d = displays[i];
        string res = d.Width > 0 ? $"{d.Width}x{d.Height}" : "-";
        string mark = ReferenceEquals(d, tv) ? "  <- TV" : "";
        Console.WriteLine($"{i + 1,-3}{Trim(d.Name, 21),-22}{d.Active,-8}{d.Primary,-9}{d.Connection,-22}{res,-12}{TvDetector.Score(d, displays.Count)}{mark}");
    }
    Console.WriteLine();
    Console.WriteLine(tv != null ? $"TV: {tv} ({reason})." : $"TV: none ({reason}). Use 'bptv select <number>'.");
    foreach (var d in displays) Console.WriteLine($"  {d}: {d.DevicePath}");
    return 0;
}

int Select()
{
    if (positional.Count != 1)
    {
        Console.Error.WriteLine("Usage: bptv select <number|name>   (numbers come from 'bptv list')");
        return 1;
    }
    var displays = display.ListDisplays();
    DisplayInfo? chosen = int.TryParse(positional[0], out int n) && n >= 1 && n <= displays.Count
        ? displays[n - 1]
        : TvSelector.Select(displays, settings, positional[0]).Tv;
    if (chosen == null)
    {
        Console.Error.WriteLine($"No display matches '{positional[0]}'. Run 'bptv list'.");
        return 1;
    }
    settings.TvDevicePath = chosen.DevicePath;
    settings.TvName = chosen.Name;
    settings.Save(AppPaths.SettingsFile);
    log.Write($"TV set to {chosen}.");
    return 0;
}

int Watch()
{
    var extra = settings.ExtraProcesses.ToList();
    if (options.TryGetValue("extra", out string? e)) extra.Add(e);
    int grace = IntOption("grace", settings.GraceSeconds, 0, 600);
    int poll = IntOption("poll", settings.PollSeconds, 1, 60);
    if (grace < 0 || poll < 0) return 1;

    // One watcher per user; the same name the PowerShell script uses, so the
    // two never fight over the displays.
    using var mutex = new Mutex(false, @"Local\BigPictureTV");
    if (!mutex.WaitOne(0))
    {
        log.Write("BigPictureTV is already running.");
        return 1;
    }

    var probe = new BigPictureWatcher(settings.BigPictureTitles, extra);
    var controller = new ModeController(manager, log) { Grace = TimeSpan.FromSeconds(grace) };

    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, ev) => { ev.Cancel = true; stop.Cancel(); };

    var (tv, reason) = TvSelector.Select(display.ListDisplays(), settings, tvOverride);
    log.Write($"Watching for Big Picture (TV: {tv?.ToString() ?? "not found"}, {reason}).");

    controller.Start(probe.IsOpen());
    try
    {
        while (!stop.IsCancellationRequested)
        {
            controller.Tick(probe.IsOpen(), DateTime.UtcNow);
            stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(poll));
        }
    }
    finally
    {
        controller.Shutdown();
        mutex.ReleaseMutex();
    }
    return 0;
}

int IntOption(string name, int fallback, int min, int max)
{
    if (!options.TryGetValue(name, out string? text)) return fallback;
    if (int.TryParse(text, out int value) && value >= min && value <= max) return value;
    Console.Error.WriteLine($"--{name} must be a number from {min} to {max}.");
    return -1;
}

static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

static (Dictionary<string, string>, List<string>) ParseOptions(string[] rest)
{
    var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var pos = new List<string>();
    for (int i = 0; i < rest.Length; i++)
    {
        if (rest[i].StartsWith("--"))
        {
            if (i + 1 >= rest.Length) throw new ArgumentException($"{rest[i]} needs a value.");
            opts[rest[i][2..]] = rest[++i];
        }
        else
        {
            pos.Add(rest[i]);
        }
    }
    return (opts, pos);
}
