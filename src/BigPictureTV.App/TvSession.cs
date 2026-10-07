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
/// One visit to the TV menu: from opening it, through the apps picked in it,
/// to leaving it. While it lasts the controller drives the menu, and in web
/// apps it types keys (cross = arrows, A = Enter, B = back...). Holding View
/// (Back) closes the app and brings the menu back. Runs on the UI thread.
/// </summary>
sealed class TvSession : IDisposable
{
    static readonly Strings S = Strings.Current;
    static readonly TimeSpan HandOffWindow = TimeSpan.FromSeconds(3);

    readonly ILog _log;
    readonly GamepadService _gamepad;
    readonly SynchronizationContext _ui;
    readonly Func<(bool Ok, bool Switched)> _enterTv;
    readonly Action _toDesktop;
    readonly Action _openBigPicture;
    readonly MenuSignal _signal;
    readonly PadMapper _mapper = new(); // only touched on the controller thread

    bool _active, _switchedHere;
    volatile Action<PadAction>? _menuHandler;
    Action? _closeMenu;
    int _selected;

    // The app opened from the menu; _app stays set while it runs even if
    // its process couldn't be followed.
    volatile TvApp? _app;
    Process? _process;
    DateTime _startedAt;
    string _browserName = "";
    string? _profile;  // the open web tile's browser profile
    volatile int _launches; // tells a late browser lookup it is out of date
    volatile bool _keysLogged;

    /// <param name="enterTv">Switches to the TV unless already there; Switched is true if it did.</param>
    public TvSession(ILog log, GamepadService gamepad, SynchronizationContext ui,
        Func<(bool Ok, bool Switched)> enterTv, Action toDesktop, Action openBigPicture)
    {
        _log = log;
        _gamepad = gamepad;
        _ui = ui;
        _enterTv = enterTv;
        _toDesktop = toDesktop;
        _openBigPicture = openBigPicture;
        _signal = new MenuSignal(() => _ui.Post(_ => Open(), null));
    }

    public bool Active => _active;

    /// <summary>Opens the menu, switching to the TV first. Does nothing if a visit is going on.</summary>
    public void Open()
    {
        if (_active) return;
        var (ok, switched) = _enterTv();
        if (!ok) return; // the tray already said why
        _active = true;
        _switchedHere = switched;
        _signal.MenuOpened();
        _gamepad.Listener = OnPad;
        _log.Write("TV menu opened.");
        ShowMenu();
    }

    /// <summary>The shortcut: opens the menu, closes it if it's showing, or leaves the app back to it.</summary>
    public void Toggle()
    {
        if (!_active) Open();
        else if (_closeMenu != null) _closeMenu();
        else CloseApp(showMenu: true);
    }

    void ShowMenu(string? message = null)
    {
        var apps = AppSettings.Load(AppPaths.SettingsFile, _log).TvMenuApps; // picks up hand edits
        var (chosen, selected) = WpfDialogs.ShowTvMenu(apps, _selected, message, (handle, close) =>
        {
            _menuHandler = handle;
            _closeMenu = close;
        });
        _menuHandler = null;
        _closeMenu = null;
        _selected = selected;
        MemoryTrim.Soon();

        if (chosen == null) End(_switchedHere);
        else Launch(chosen);
    }

    void Launch(TvApp app)
    {
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

        try
        {
            ProcessStartInfo start;
            if (app.Kind == TvAppKind.Web)
            {
                string? browser = BrowserFinder.Find(AppSettings.Load(AppPaths.SettingsFile, _log).BrowserPath);
                if (browser == null)
                {
                    _log.Write("No browser found for web tiles.");
                    ShowMenu(S.BrowserNotFound);
                    return;
                }
                string profile = BrowserCommand.ProfileDir(AppPaths.DataDir, app);
                Directory.CreateDirectory(profile);
                _browserName = Path.GetFileNameWithoutExtension(browser);
                // One window per tile: a copy left from before would take the
                // new page as a second window (and ignore its settings).
                BrowserProcesses.Close(_browserName, profile, _log);
                start = new ProcessStartInfo(browser, BrowserCommand.Arguments(app, profile)) { UseShellExecute = false };
                _profile = profile;
            }
            else
            {
                string target = Environment.ExpandEnvironmentVariables(app.Target.Trim().Trim('"'));
                start = new ProcessStartInfo(target, app.Arguments) { UseShellExecute = true };
                if (File.Exists(target)) start.WorkingDirectory = Path.GetDirectoryName(target);
            }

            _app = app;
            _keysLogged = false;
            _startedAt = DateTime.UtcNow;
            _process = Process.Start(start);
            if (_process != null) Follow(_process);
            if (app.Kind == TvAppKind.Web) FindBrowser(++_launches, _browserName, _profile!);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Write($"Opening {app} failed: {e.Message}");
            _app = null;
            _process = null;
            ShowMenu(string.Format(S.TvMenuOpenFailed, app));
        }
    }

    void Follow(Process process)
    {
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => _ui.Post(_ => OnExited(process), null);
    }

    // The browser we start may hand over to another process and quit; find
    // the one that really shows the page, so its closing brings the menu
    // back and View (Back) can close it.
    void FindBrowser(int launch, string browserName, string profile) => Task.Run(() =>
    {
        for (int i = 0; i < 30 && launch == _launches; i++)
        {
            var found = BrowserProcesses.Find(browserName, profile, _log);
            if (found.Count > 0)
            {
                _ui.Post(_ =>
                {
                    if (launch != _launches || _app == null || found[0].HasExited)
                    {
                        found.ForEach(p => p.Dispose());
                        return;
                    }
                    if (_process?.Id != found[0].Id)
                    {
                        _process?.Dispose();
                        _process = found[0];
                        Follow(_process);
                    }
                    else
                    {
                        found[0].Dispose();
                    }
                    foreach (var extra in found.Skip(1)) extra.Dispose();
                    _log.Write($"Following the browser (process {_process.Id}).");
                }, null);
                return;
            }
            Thread.Sleep(500);
        }
        if (launch == _launches) _log.Write("The browser window wasn't found; hold View (Back) to come back to the menu.");
    });

    void OnExited(Process process)
    {
        if (!ReferenceEquals(process, _process)) return; // closed by us, or an older one
        _process = null;
        if (DateTime.UtcNow - _startedAt < HandOffWindow)
        {
            // Browsers and launchers often hand over to another process and
            // quit: the app is still on screen, so keep the session going
            // and let View (Back) bring the menu back.
            _log.Write($"{_app} handed over to another process; hold View (Back) to return to the menu.");
            process.Dispose();
            return;
        }
        process.Dispose();
        _app = null;
        _log.Write("The app was closed; back to the TV menu.");
        if (_active) ShowMenu();
    }

    void CloseApp(bool showMenu)
    {
        var process = _process;
        _process = null;
        _app = null;
        _launches++;
        if (process != null)
        {
            try
            {
                // Politely first, so the browser saves its session and logins.
                if (!process.HasExited && (!process.CloseMainWindow() || !process.WaitForExit(3000)))
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _log.Write($"Closing the app failed: {e.Message}");
            }
            process.Dispose();
        }
        if (_profile != null)
        {
            BrowserProcesses.Close(_browserName, _profile, _log); // anything left on that profile
            _profile = null;
        }
        if (showMenu && _active) ShowMenu();
    }

    void End(bool toDesktop)
    {
        if (!_active) return;
        _active = false;
        _gamepad.Listener = null;
        CloseApp(showMenu: false);
        _signal.MenuClosed();
        _log.Write("TV menu closed.");
        if (toDesktop) _toDesktop();
    }

    // On the controller thread.
    void OnPad(GamepadButtons buttons)
    {
        var actions = _mapper.Update(buttons, DateTime.UtcNow);
        if (actions.Count == 0) return;

        if (_menuHandler is { } menu)
        {
            foreach (var action in actions) menu(action);
            return;
        }

        var app = _app;
        if (app == null) return;
        foreach (var action in actions)
        {
            if (action == PadAction.Close)
            {
                _ui.Post(_ => { if (_active && _closeMenu == null) CloseApp(showMenu: true); }, null);
                return;
            }
            // Only web pages get keys, and only while their window is in
            // front: other programs read the controller themselves.
            if (app.Kind != TvAppKind.Web) continue;
            string front = KeySender.ForegroundProcessName();
            if (!string.Equals(front, _browserName, StringComparison.OrdinalIgnoreCase))
            {
                if (!_keysLogged) _log.Write($"Controller keys not sent: \"{front}\" is in front, not {_browserName}.");
                _keysLogged = true;
                continue;
            }
            if (KeysFor(action, app) is not { } keys) continue;
            KeySender.Send(keys);
            if (!_keysLogged) _log.Write($"Controller keys go to {_browserName} (first: {keys}).");
            _keysLogged = true;
        }
    }

    static readonly Hotkey AltLeft = new(KeyModifiers.Alt, 0x25);

    static Hotkey? KeysFor(PadAction action, TvApp app) => action switch
    {
        PadAction.Up => new Hotkey(KeyModifiers.None, 0x26),
        PadAction.Down => new Hotkey(KeyModifiers.None, 0x28),
        PadAction.Left => new Hotkey(KeyModifiers.None, 0x25),
        PadAction.Right => new Hotkey(KeyModifiers.None, 0x27),
        PadAction.Accept => new Hotkey(KeyModifiers.None, 0x0D),
        PadAction.Back => Hotkey.ParseAny(app.BackKey) ?? AltLeft,
        PadAction.Search => Hotkey.ParseAny(app.SearchKey),
        PadAction.PlayPause => new Hotkey(KeyModifiers.None, KeySender.VK_MEDIA_PLAY_PAUSE),
        PadAction.Previous => new Hotkey(KeyModifiers.Shift, 0x09),
        PadAction.Next => new Hotkey(KeyModifiers.None, 0x09),
        PadAction.PageUp => new Hotkey(KeyModifiers.None, 0x21),
        PadAction.PageDown => new Hotkey(KeyModifiers.None, 0x22),
        _ => null,
    };

    public void Dispose()
    {
        _gamepad.Listener = null;
        _closeMenu?.Invoke();
        _active = false;
        _signal.Dispose();
    }
}
