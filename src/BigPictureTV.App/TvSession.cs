using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigPictureTV.Core;
using BigPictureTV.Core.Input;
using BigPictureTV.Core.TvMenu;

namespace BigPictureTV.App;

/// <summary>
/// The TV menu and the apps opened from it, like a console's home screen:
/// coming back to the menu leaves the app running behind it, A goes back to
/// it and X closes it. While the menu or an app is in front the controller
/// drives them (cross = arrows, A = Enter, B = back...). Runs on the UI thread.
/// </summary>
sealed class TvSession : IDisposable
{
    static readonly Strings S = Strings.Current;
    static readonly TimeSpan HandOff = TimeSpan.FromSeconds(3);

    readonly ILog _log;
    readonly GamepadService _gamepad;
    readonly SynchronizationContext _ui;
    readonly Func<(bool Ok, bool Switched)> _enterTv;
    readonly Action _toDesktop;
    readonly Action _openBigPicture;
    readonly Func<string> _menuButtonName;
    readonly MenuSignal _signal;
    readonly PadMapper _mapper = new(); // only touched on the controller thread
    readonly Dictionary<string, RunningApp> _running = new();

    ITvMenuView? _view;
    volatile bool _menuShown;
    volatile RunningApp? _current; // the app in front, driven by the controller
    bool _session, _switchedHere;

    /// <param name="enterTv">Switches to the TV unless already there; Switched is true if it did.</param>
    /// <param name="menuButtonName">How to call the menu button in the hints, or "" if there is none.</param>
    public TvSession(ILog log, GamepadService gamepad, SynchronizationContext ui,
        Func<(bool Ok, bool Switched)> enterTv, Action toDesktop, Action openBigPicture, Func<string> menuButtonName)
    {
        _log = log;
        _gamepad = gamepad;
        _ui = ui;
        _enterTv = enterTv;
        _toDesktop = toDesktop;
        _openBigPicture = openBigPicture;
        _menuButtonName = menuButtonName;
        _signal = new MenuSignal(() => Post(Open));
    }

    /// <summary>True while the menu or an app opened from it is in front.</summary>
    public bool Active => _session;

    /// <summary>Shows the menu, switching to the TV first if needed.</summary>
    public void Open() => Guard("Opening the TV menu", () => ShowMenu());

    /// <summary>The menu button or the shortcut: shows the menu, or leaves it (back to the app in front, if any).</summary>
    public void Toggle() => Guard("The menu button", () =>
    {
        if (_menuShown) Back();
        else ShowMenu();
    });

    void ShowMenu(string? message = null)
    {
        if (!_session)
        {
            var (ok, switched) = _enterTv();
            if (!ok) return; // the tray already said why
            _session = true;
            _switchedHere = switched;
            _signal.MenuOpened();
            _gamepad.Listener = OnPad;
            _log.Write("TV menu opened.");
        }
        Prune();
        if (_view == null)
        {
            _view = WpfDialogs.CreateTvMenu();
            _view.Chosen += app => Guard($"Opening {app}", () => Choose(app));
            _view.CloseRequested += app => Guard($"Closing {app}", () => CloseApp(app.Key));
            _view.BackRequested += () => Guard("Leaving the menu", Back);
        }
        var apps = AppSettings.Load(AppPaths.SettingsFile, _log).TvMenuApps; // picks up hand edits
        _menuShown = true;
        _view.Show(apps, _running.Keys.ToList(), _current?.App, message, _menuButtonName());
        MemoryTrim.Soon();
    }

    void HideMenu()
    {
        _menuShown = false;
        _view?.Hide();
    }

    // B in the menu: back to the app in front, or out of the menu.
    void Back()
    {
        if (_current is { Gone: false } app)
        {
            Resume(app);
            return;
        }
        End(toDesktop: _switchedHere);
    }

    void Choose(TvApp app)
    {
        if (_running.TryGetValue(app.Key, out var running) && !running.Gone)
        {
            Resume(running);
            return;
        }
        _log.Write($"TV menu: opening {app} ({app.Kind}).");
        switch (app.Kind)
        {
            case TvAppKind.Desktop:
                End(toDesktop: true);
                return;
            case TvAppKind.BigPicture:
                End(toDesktop: false);
                _openBigPicture();
                return;
        }
        var started = app.Kind == TvAppKind.Web ? StartWeb(app, out string? error) : StartProgram(app, out error);
        if (started == null)
        {
            ShowMenu(error ?? string.Format(S.TvMenuOpenFailed, app));
            return;
        }
        _running[app.Key] = started;
        started.Exited += gone => Post(() => OnExited(gone));
        _current = started;
        HideMenu();
    }

    void Resume(RunningApp app)
    {
        _current = app;
        HideMenu();
        app.BringToFront();
        _log.Write($"Back to {app.App}.");
    }

    RunningApp? StartWeb(TvApp app, out string? error)
    {
        error = null;
        string? browser = BrowserFinder.Find(AppSettings.Load(AppPaths.SettingsFile, _log).BrowserPath);
        if (browser == null)
        {
            _log.Write("No browser found for web tiles.");
            error = S.BrowserNotFound;
            return null;
        }
        string profile = BrowserCommand.ProfileDir(AppPaths.DataDir, app);
        string name = Path.GetFileNameWithoutExtension(browser);
        try
        {
            Directory.CreateDirectory(profile);
            // One window per tile: a copy left from before would take the new
            // page as a second window and ignore its settings.
            BrowserProcesses.Close(name, profile, _log);
            File.Delete(Path.Combine(profile, "DevToolsActivePort")); // so we read the new one
            var start = new ProcessStartInfo(browser, BrowserCommand.Arguments(app, profile) + " --remote-debugging-port=0")
            {
                UseShellExecute = false,
            };
            Process.Start(start)?.Dispose(); // it often hands over and quits; the real one is found below
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Write($"Opening {app} failed: {e.Message}");
            return null;
        }
        var running = new RunningApp(app, _log) { Profile = profile, BrowserName = name };
        _ = Task.Run(() => ConnectAsync(running));
        return running;
    }

    // Finds the browser's main process (to bring it back, minimize it and
    // notice when it closes) and opens the DevTools channel to its page.
    async Task ConnectAsync(RunningApp app)
    {
        try
        {
            for (int i = 0; i < 30 && !app.Gone; i++)
            {
                var found = BrowserProcesses.Find(app.BrowserName, app.Profile!, _log);
                if (found.Count > 0)
                {
                    foreach (var extra in found.Skip(1)) extra.Dispose();
                    Post(() => { if (!app.Gone) app.Follow(found[0]); else found[0].Dispose(); });
                    break;
                }
                await Task.Delay(500);
            }

            var page = await CdpPage.ConnectAsync(app.Profile!, _log, TimeSpan.FromSeconds(15));
            if (page == null)
            {
                _log.Write($"No DevTools connection to {app.App}; the controller types keys instead.");
                return;
            }
            if (app.Gone)
            {
                page.Dispose();
                return;
            }
            if (app.App.UserAgent.Trim().Length > 0)
            {
                await page.PretendToBeTvAsync(app.App.UserAgent.Trim());
                await page.NavigateAsync(app.App.Target.Trim()); // load it again, now as a TV
            }
            app.Attach(page);
            _log.Write($"Connected to the page of {app.App}.");
        }
        catch (Exception e)
        {
            _log.Write($"Connecting to {app.App} failed: {e.Message}");
        }
    }

    RunningApp? StartProgram(TvApp app, out string? error)
    {
        error = null;
        try
        {
            string target = Environment.ExpandEnvironmentVariables(app.Target.Trim().Trim('"'));
            var start = new ProcessStartInfo(target, app.Arguments) { UseShellExecute = true };
            if (File.Exists(target)) start.WorkingDirectory = Path.GetDirectoryName(target);
            var process = Process.Start(start);
            var running = new RunningApp(app, _log);
            if (process != null) running.Follow(process, HandOff);
            return running;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Write($"Opening {app} failed: {e.Message}");
            return null;
        }
    }

    void OnExited(RunningApp app)
    {
        if (!_running.TryGetValue(app.Key, out var known) || !ReferenceEquals(known, app)) return;
        _running.Remove(app.Key);
        app.Dispose();
        _log.Write($"{app.App} was closed.");
        if (!ReferenceEquals(_current, app)) return;
        _current = null;
        if (_session) ShowMenu(); // it was in front: back to the menu
    }

    void CloseApp(string key)
    {
        if (!_running.Remove(key, out var app)) return;
        if (ReferenceEquals(_current, app)) _current = null;
        _log.Write($"Closing {app.App}.");
        app.Close();
        if (_menuShown) ShowMenu(); // redraw without the "open" mark
    }

    // Leaves the menu. Apps opened from it keep running (music keeps playing);
    // going to the desktop minimizes them so they don't cover it.
    void End(bool toDesktop)
    {
        HideMenu();
        _current = null;
        if (!_session) return;
        _session = false;
        _gamepad.Listener = null;
        _signal.MenuClosed();
        _log.Write("TV menu closed.");
        if (!toDesktop) return;
        foreach (var app in _running.Values) app.Minimize();
        _toDesktop();
    }

    void Prune()
    {
        foreach (var key in _running.Where(r => r.Value.Gone).Select(r => r.Key).ToList())
        {
            _running[key].Dispose();
            _running.Remove(key);
        }
    }

    // On the controller thread.
    void OnPad(GamepadButtons buttons)
    {
        var actions = _mapper.Update(buttons, DateTime.UtcNow);
        if (actions.Count == 0) return;
        if (_menuShown)
        {
            if (_view is { } view) foreach (var action in actions) view.Handle(action);
            return;
        }
        if (_current is { Gone: false } app)
            foreach (var action in actions) app.Send(action);
    }

    void Post(Action action) => _ui.Post(_ => Guard("TV menu", action), null);

    // Anything going wrong in the menu is written to the log, never takes the app down.
    void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception e) { _log.Write($"{what} failed: {e}"); }
    }

    public void Dispose()
    {
        _gamepad.Listener = null;
        _signal.Dispose();
        foreach (var app in _running.Values) app.Dispose(); // leave them running; just let go
        _running.Clear();
    }
}
