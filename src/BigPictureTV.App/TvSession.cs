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

    readonly NowPlaying _nowPlaying;
    readonly TileIcons _icons;
    readonly System.Windows.Forms.Timer _mediaTimer = new() { Interval = 1500 };
    bool _readingMedia;
    ITvMenuView? _view;
    ITvKeyboardView? _keyboardView;
    ITvVolumeView? _volumeView;
    readonly Dictionary<int, string?> _musicProcesses = new(); // process id: key of the music tile it plays for
    readonly Dictionary<string, int> _volumeBefore = new(); // music tile key: its volume before the stick moved it
    readonly OnScreenKeyboard _keyboard = new(); // only touched on the UI thread
    volatile bool _menuShown, _keyboardShown;
    volatile RunningApp? _current; // the app in front, driven by the controller

    // The window that was in front when the menu opened from outside it (a
    // game, Big Picture). A tile goes back to it, like a console's game card.
    IntPtr _origin;
    string _originTitle = "";
    bool _inOrigin; // it is the one in front, not an app from the menu
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
        _nowPlaying = new NowPlaying(log);
        _icons = new TileIcons(log);
        _mediaTimer.Tick += (_, _) => _ = RefreshMediaAsync();
    }

    /// <summary>True while the menu or an app opened from it is in front.</summary>
    public bool Active => _session;

    /// <summary>Shows the menu, switching to the TV first if needed.</summary>
    public void Open() => Guard("Opening the TV menu", () => ShowMenu());

    /// <summary>The menu button or the shortcut: shows the menu, or leaves it (back to the app in front, if any).</summary>
    public void Toggle() => Guard("The menu button", () =>
    {
        if (!_menuShown) ShowMenu();
        // Like a console's home button: back to the game or app in front; with
        // none, it stays on the menu. Leaving the TV is the Desktop tile's (or
        // B's) job, so a double press doesn't switch the displays back and forth.
        else if (_inOrigin && OriginShown) ReturnToOrigin();
        else if (_current is { Gone: false } app) Resume(app);
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
        HideKeyboard();
        if (_view == null)
        {
            _view = WpfDialogs.CreateTvMenu();
            _view.Chosen += app => Guard($"Opening {app}", () => Choose(app));
            _view.CloseRequested += app => Guard($"Closing {app}", () => CloseApp(app.Key));
            _view.BackRequested += () => Guard("Leaving the menu", Back);
            _view.MediaRequested += action => _ = MediaAsync(action);
            _view.AudioRequested += () => Guard("Changing the sound output", NextAudioOutput);
            _view.VolumeRequested += (windows, direction) => Guard("Changing the volume", () => ChangeVolume(windows, direction));
        }
        if (!_menuShown) NoteOrigin();
        var apps = AppSettings.Load(AppPaths.SettingsFile, _log).TvMenuApps; // picks up hand edits
        var origin = OriginTile();
        if (origin != null) apps.Insert(0, origin);
        _menuShown = true;
        _view.ShowIcons(_icons.Known);
        _view.Show(apps, _running.Keys.ToList(), _inOrigin && origin != null ? origin : _current?.App, message, _menuButtonName());
        _icons.Prepare(apps, () => Post(() => { if (_menuShown) _view?.ShowIcons(_icons.Known); }));
        ShowAudioOutput();
        if (!_mediaTimer.Enabled)
        {
            _mediaTimer.Start();
            _ = RefreshMediaAsync();
        }
        MemoryTrim.Soon();
    }

    // Opening the menu over a game (any window that is neither ours, nor an
    // app from the menu, nor the desktop): remember it, to go back to it.
    void NoteOrigin()
    {
        var front = KeySender.ForegroundWindow();
        if (!AppWindows.IsShown(front) || AppWindows.ProcessOf(front) == Environment.ProcessId || AppWindows.IsShell(front)) return;
        if (_running.Values.Any(r => !r.Gone && r.OwnsForegroundWindow()))
        {
            _inOrigin = false;
            return;
        }
        string title = AppWindows.TitleOf(front).Trim();
        if (title.Length == 0) return;
        if (front != _origin) _log.Write($"TV menu opened over {title}.");
        _origin = front;
        _originTitle = title;
        _inOrigin = true;
    }

    bool OriginShown => AppWindows.IsShown(_origin);

    TvApp? OriginTile()
    {
        if (!OriginShown) return null;
        string name = _originTitle.Length > 40 ? _originTitle[..39] + "…" : _originTitle;
        return new TvApp { Kind = TvAppKind.Game, Name = name, Target = _origin.ToString(), Color = "#107C10" };
    }

    void ReturnToOrigin()
    {
        _current = null;
        _inOrigin = true;
        HideMenu();
        HideKeyboard();
        AppWindows.BringBack(_origin);
        _log.Write($"Back to {_originTitle}.");
    }

    void HideMenu()
    {
        _menuShown = false;
        _mediaTimer.Stop();
        _view?.Hide();
    }

    // "Now playing" in the menu, read again every moment while it shows.
    async Task RefreshMediaAsync()
    {
        if (_readingMedia) return;
        _readingMedia = true;
        try
        {
            var info = await _nowPlaying.ReadAsync();
            if (_menuShown) _view?.ShowNowPlaying(info);
        }
        catch (Exception e)
        {
            _log.Write($"Reading what is playing failed: {e.Message}");
        }
        finally
        {
            _readingMedia = false;
        }
    }

    async Task MediaAsync(PadAction action)
    {
        try
        {
            await _nowPlaying.SendAsync(action);
            await Task.Delay(400); // let the player catch up before showing it
            await RefreshMediaAsync();
        }
        catch (Exception e)
        {
            _log.Write($"Media button failed: {e.Message}");
        }
    }

    const float VolumeStep = 0.02f;

    // Only the volume of the music opened from the menu (its slider in
    // Windows' volume mixer), so games, videos and other apps keep theirs;
    // on the sound output card, the PC's.
    void ChangeVolume(bool windows, int direction)
    {
        _volumeView ??= WpfDialogs.CreateVolume();
        if (windows)
        {
            if (AudioOutputs.ChangeVolume(direction * VolumeStep) is { } pc)
                _volumeView.Show(pc.Muted ? S.WindowsVolume + "  🔇" : S.WindowsVolume, pc.Percent);
            return;
        }
        // What each one had, to give it back when it closes.
        foreach (var app in _running.Values.Where(a => !a.Gone && a.App.IsMusic && !_volumeBefore.ContainsKey(a.Key)))
            if (AudioOutputs.ChangeAppVolume(pid => MusicTileOf(pid) == app.Key, 0) is { } before)
                _volumeBefore[app.Key] = before;
        int? music = AudioOutputs.ChangeAppVolume(pid => MusicTileOf(pid) != null, direction * VolumeStep);
        _volumeView.Show(music == null ? S.NoMusicPlaying : S.MusicVolume, music);
    }

    // Windows keeps an app's volume for the next time it runs, for the whole
    // program: YouTube Music left low would leave Edge low. So a music tile
    // gets its volume back before it closes.
    void RestoreVolume(RunningApp app)
    {
        if (!_volumeBefore.Remove(app.Key, out int before) || app.Gone) return;
        AudioOutputs.SetAppVolume(pid => MusicTileOf(pid) == app.Key, before);
        _log.Write($"{app.App}: volume back to {before}%.");
    }

    // The music tile open from the menu that this process plays for, or null:
    // the app of a program tile (Spotify) or one of its helpers, or the
    // browser behind a web tile (YouTube Music), by its profile or as its child.
    // Not the same program opened by other means, nor the browser's other windows.
    string? MusicTileOf(int processId)
    {
        if (!_musicProcesses.TryGetValue(processId, out string? key))
        {
            key = FindMusicTile(processId);
            _musicProcesses[processId] = key;
        }
        return key != null && _running.TryGetValue(key, out var app) && !app.Gone ? key : null;
    }

    string? FindMusicTile(int processId)
    {
        var music = _running.Values.Where(a => !a.Gone && a.App.IsMusic).ToList();
        if (music.Count == 0) return null;
        string name = "";
        try
        {
            using var process = Process.GetProcessById(processId);
            name = process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
        foreach (var app in music.Where(a => !a.IsWeb))
        {
            if (app.ProcessId == processId) return app.Key;
            string own = app.App.ProcessName.Length > 0 ? app.App.ProcessName : Path.GetFileNameWithoutExtension(app.App.Target);
            if (own.Length > 0 && string.Equals(name, own, StringComparison.OrdinalIgnoreCase)) return app.Key;
        }
        var web = music.Where(a => a.IsWeb && a.Profile != null && string.Equals(a.BrowserName, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (web.Count == 0) return null;
        var (commandLine, parent) = BrowserProcesses.Describe(processId);
        return web.FirstOrDefault(a => BrowserCommand.UsesProfile(commandLine, a.Profile!) || (a.ProcessId != 0 && a.ProcessId == parent))?.Key;
    }

    void ShowAudioOutput()
    {
        var outputs = AudioOutputs.List();
        string? id = AudioOutputs.DefaultId();
        _view?.ShowAudioOutput(outputs.Count == 0 ? null : outputs.FirstOrDefault(o => o.Id == id)?.Name ?? "");
    }

    // A on the sound output: the next one, like pressing a TV remote's input button.
    // Back on the desktop the tray app puts the sound where it was before the TV.
    void NextAudioOutput()
    {
        var outputs = AudioOutputs.List();
        if (outputs.Count < 2) return;
        string? id = AudioOutputs.DefaultId();
        int at = outputs.ToList().FindIndex(o => o.Id == id);
        var next = outputs[(at + 1) % outputs.Count];
        _log.Write(AudioOutputs.SetDefault(next.Id) ? $"TV menu: sound moved to {next}." : $"TV menu: Windows refused to move the sound to {next}.");
        ShowAudioOutput();
    }

    // B in the menu: back to the game or app in front (with none, to the
    // game the menu opened over), or out of the menu.
    void Back()
    {
        if (_inOrigin && OriginShown)
        {
            ReturnToOrigin();
            return;
        }
        if (_current is { Gone: false } app)
        {
            Resume(app);
            return;
        }
        if (OriginShown)
        {
            ReturnToOrigin();
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
        if (app.Kind == TvAppKind.Game)
        {
            ReturnToOrigin();
            return;
        }
        _log.Write($"TV menu: opening {app} ({app.Kind}).");
        _musicProcesses.Clear();
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
        _inOrigin = false;
        HideMenu();
    }

    void Resume(RunningApp app)
    {
        _current = app;
        _inOrigin = false;
        HideMenu();
        app.BringToFront();
        _log.Write($"Back to {app.App}.");
    }

    RunningApp? StartWeb(TvApp app, out string? error, string? key = null)
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
        bool shared = BrowserCommand.SharesProfile(app);
        string name = Path.GetFileNameWithoutExtension(browser);
        HashSet<IntPtr> before = shared ? AppWindows.All() : new();
        try
        {
            if (shared) AdoptOldProfile(profile, name);
            Directory.CreateDirectory(profile);
            // One window per tile: a copy left from before would take the new
            // page as a second window and ignore its settings. On the shared
            // profile the other tiles' windows stay: they use the same settings.
            if (!_running.Values.Any(r => !r.Gone && r.Profile == profile)) BrowserProcesses.Close(name, profile, _log);
            File.Delete(Path.Combine(profile, "DevToolsActivePort")); // so we read the new one
            // Only TV pages get the DevTools channel: Cloudflare's check (on
            // Crunchyroll) never passes while the browser has it open. The
            // others get the navigation extension instead.
            // Edge installs it from its store by itself (asking once per
            // profile to turn it on); other browsers load the copy on disk.
            string extension = Path.Combine(AppPaths.DataDir, "Browser", "Extension");
            SpatialNav.Write(extension);
            bool fromStore = string.Equals(name, "msedge", StringComparison.OrdinalIgnoreCase) && EdgeStore.Register(_log);
            var start = new ProcessStartInfo(browser, BrowserCommand.Arguments(app, profile, fromStore ? null : extension))
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
        var running = new RunningApp(app, _log) { Profile = profile, BrowserName = name, Key = key ?? app.Key, SharedProfile = shared };
        _ = Task.Run(() => ConnectAsync(running, before));
        return running;
    }

    // Plain pages used to have a profile per tile; the first time the shared
    // one is needed, an old one (with its logins and, on Crunchyroll's, the
    // navigation extension) becomes it.
    void AdoptOldProfile(string shared, string browserName)
    {
        if (Directory.Exists(shared)) return;
        var apps = AppSettings.Load(AppPaths.SettingsFile, _log).TvMenuApps;
        foreach (var old in apps.Where(BrowserCommand.SharesProfile).Select(a => BrowserCommand.NamedProfileDir(AppPaths.DataDir, a))
                     .OrderByDescending(d => d.EndsWith("crunchyroll", StringComparison.OrdinalIgnoreCase)))
        {
            if (!Directory.Exists(old)) continue;
            try
            {
                BrowserProcesses.Close(browserName, old, _log);
                Directory.Move(old, shared);
                _log.Write($"The web tiles now share the profile that was {Path.GetFileName(old)}'s.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log.Write($"Couldn't reuse {old} for the web tiles: {e.Message}");
            }
            return;
        }
    }

    async Task AttachPageAsync(RunningApp app)
    {
        try
        {
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

    // A window Windows opens behind the one in front leaves the taskbar on top
    // of a full-screen page until it's clicked: bring it forward once it's up.
    async Task FocusWhenShownAsync(RunningApp app)
    {
        for (int i = 0; i < 40 && !app.Gone; i++)
        {
            await Task.Delay(250);
            if (app.Process != null && app.HasWindow)
            {
                Post(() => FocusIfInFront(app));
                return;
            }
        }
    }

    void FocusIfInFront(RunningApp app)
    {
        if (!app.Gone && ReferenceEquals(_current, app)) app.BringToFront();
    }

    // On the shared profile the browser was often already running for another
    // tile: this tile is the window that appeared after it was opened.
    async Task ClaimWindowAsync(RunningApp app, int browserId, HashSet<IntPtr> before)
    {
        for (int i = 0; i < 40 && !app.Gone; i++)
        {
            await Task.Delay(250);
            HashSet<IntPtr> claimed;
            try { claimed = new HashSet<IntPtr>(_running.Values.Where(r => r != app).Select(r => r.Window)); }
            catch (InvalidOperationException) { continue; } // the list changed meanwhile (UI thread); try again
            var window = AppWindows.NewWindowOf(browserId, before, claimed);
            if (window == IntPtr.Zero) continue;
            Post(() =>
            {
                if (app.Gone || _running.Values.Any(r => r != app && r.Window == window)) return;
                app.Claim(window);
                if (!ReferenceEquals(_current, app)) return;
                app.BringToFront();
                // A browser already running ignores --start-fullscreen for the new window.
                if (!AppWindows.CoversItsScreen(window) && KeySender.ForegroundWindow() == window) KeySender.Send(0x7A); // F11
            });
            return;
        }
        _log.Write($"Didn't find the window of {app.App}.");
    }

    // Finds the browser's main process (to bring it back, minimize it and
    // notice when it closes) and opens the DevTools channel to its page.
    async Task ConnectAsync(RunningApp app, HashSet<IntPtr> before)
    {
        try
        {
            for (int i = 0; i < 30 && !app.Gone; i++)
            {
                var found = BrowserProcesses.Find(app.BrowserName, app.Profile!, _log);
                if (found.Count > 0)
                {
                    foreach (var extra in found.Skip(1)) extra.Dispose();
                    int pid = found[0].Id;
                    Post(() => { if (!app.Gone) app.Follow(found[0]); else found[0].Dispose(); });
                    if (app.SharedProfile) _ = Task.Run(() => ClaimWindowAsync(app, pid, before));
                    else _ = Task.Run(() => FocusWhenShownAsync(app));
                    break;
                }
                await Task.Delay(500);
            }

            if (app.App.UserAgent.Trim().Length == 0)
            {
                _log.Write($"{app.App} opened without DevTools; the controller types keys, the extension moves the focus.");
                return;
            }
            await AttachPageAsync(app);
            Post(() => FocusIfInFront(app));
        }
        catch (Exception e)
        {
            _log.Write($"Connecting to {app.App} failed: {e.Message}");
        }
    }

    RunningApp? StartProgram(TvApp app, out string? error)
    {
        error = null;
        string link = app.Target.Trim();
        if (app.Fallback.Trim().Length > 0 && LinkHandlers.IsLink(link) && !LinkHandlers.Has(link))
        {
            // Not installed (Spotify's app): its web page instead, on the same tile.
            _log.Write($"Nothing opens {link}; opening {app.Fallback.Trim()} instead.");
            var web = new TvApp { Name = app.Name, Kind = TvAppKind.Web, Target = app.Fallback.Trim(), Color = app.Color };
            return StartWeb(web, out error, key: app.Key);
        }
        try
        {
            string target = Environment.ExpandEnvironmentVariables(app.Target.Trim().Trim('"'));
            var start = new ProcessStartInfo(target, app.Arguments) { UseShellExecute = true };
            if (File.Exists(target)) start.WorkingDirectory = Path.GetDirectoryName(target);
            var process = Process.Start(start);
            var running = new RunningApp(app, _log);
            if (process != null) running.Follow(process, HandOff);
            if (app.ProcessName.Trim().Length > 0) _ = Task.Run(() => FindProcessAsync(running, app.ProcessName.Trim()));
            return running;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Write($"Opening {app} failed: {e.Message}");
            return null;
        }
    }

    // A program opened through a link, or by a launcher that quits: find it by
    // name once its window is up, to bring it back and notice when it closes.
    async Task FindProcessAsync(RunningApp app, string name)
    {
        for (int i = 0; i < 30 && !app.Gone; i++)
        {
            await Task.Delay(500);
            var found = Process.GetProcessesByName(name);
            var main = found.FirstOrDefault(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch (InvalidOperationException) { return false; } });
            foreach (var p in found) if (!ReferenceEquals(p, main)) p.Dispose();
            if (main == null) continue;
            Post(() =>
            {
                if (app.Gone || app.Process != null) main.Dispose(); // the launch gave a process after all
                else app.Follow(main);
            });
            return;
        }
    }

    void OnExited(RunningApp app)
    {
        if (!_running.TryGetValue(app.Key, out var known) || !ReferenceEquals(known, app)) return;
        _running.Remove(app.Key);
        _volumeBefore.Remove(app.Key); // too late to give it back
        app.Dispose();
        _log.Write($"{app.App} was closed.");
        if (!ReferenceEquals(_current, app)) return;
        _current = null;
        HideKeyboard();
        if (_session) ShowMenu(); // it was in front: back to the menu
    }

    void CloseApp(string key)
    {
        if (!_running.TryGetValue(key, out var app)) return;
        RestoreVolume(app);
        _running.Remove(key);
        if (ReferenceEquals(_current, app)) _current = null;
        _log.Write($"Closing {app.App}.");
        app.Close();
        if (_menuShown) ShowMenu(); // redraw without the "open" mark
    }

    /// <summary>
    /// Back on the desktop (from the menu, the shortcut or the tray): leaves
    /// the menu and closes every app opened from it, music too.
    /// </summary>
    public void CloseAll() => Post(() =>
    {
        End(toDesktop: false);
        foreach (var key in _running.Keys.ToList()) CloseApp(key);
    });

    // Leaves the menu. Apps opened from it keep running (music keeps playing,
    // Big Picture can be on top); going to the desktop closes them.
    void End(bool toDesktop)
    {
        HideMenu();
        HideKeyboard();
        _current = null;
        _origin = IntPtr.Zero;
        _inOrigin = false;
        if (!_session) return;
        _session = false;
        _gamepad.Listener = null;
        _signal.MenuClosed();
        _log.Write("TV menu closed.");
        if (!toDesktop) return;
        foreach (var key in _running.Keys.ToList()) CloseApp(key);
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
        // The right stick turns the music's volume over an app from the menu
        // (the menu got it above; there, on the sound output it turns the
        // PC's). In a game it is the game's.
        bool overMenuApp = _current is { Gone: false };
        foreach (var action in actions)
            if (overMenuApp && action is PadAction.VolumeUp or PadAction.VolumeDown)
                Post(() => ChangeVolume(windows: false, action == PadAction.VolumeUp ? 1 : -1));
        actions.RemoveAll(a => a is PadAction.VolumeUp or PadAction.VolumeDown);
        if (actions.Count == 0) return;
        if (_keyboardShown)
        {
            Post(() => { foreach (var action in actions) OnKeyboardPad(action); });
            return;
        }
        if (_current is not { Gone: false } app) return;
        foreach (var action in actions)
        {
            if (action == PadAction.Keyboard)
            {
                if (!_gamepad.MenuButton.HasFlag(GamepadButtons.RS)) Post(ShowKeyboard); // R3 may open the menu instead
                continue;
            }
            app.Send(action);
            // Y on a page without its own keyboard (YouTube TV has one): the
            // extension puts the focus in the search box, ready to type.
            if (action == PadAction.Search && app.IsWeb && !app.TvInterface) Post(ShowKeyboard);
        }
    }

    // The on-screen keyboard types into the window in front, so it only
    // shows over an app opened from the menu.
    void ShowKeyboard()
    {
        if (_menuShown || _current is not { Gone: false }) return;
        if (!_keyboardShown) _keyboard.Reset();
        _keyboardView ??= WpfDialogs.CreateKeyboard();
        _keyboardShown = true;
        _keyboardView.Show(_keyboard);
    }

    void HideKeyboard()
    {
        _keyboardShown = false;
        _keyboardView?.Hide();
    }

    // On the UI thread, while the keyboard shows.
    void OnKeyboardPad(PadAction action)
    {
        if (!_keyboardShown) return;
        if (_current is not { Gone: false })
        {
            HideKeyboard();
            return;
        }
        switch (action)
        {
            case PadAction.Up or PadAction.Down or PadAction.Left or PadAction.Right:
                _keyboard.Move(action);
                break;
            case PadAction.Accept:
                if (_keyboard.Press() is { } output) Type(output);
                break;
            case PadAction.Option:
                KeySender.Send(KeySender.VK_BACK);
                return;
            case PadAction.Search:
                KeySender.Type(" ");
                return;
            case PadAction.PlayPause:
                Type(new KeyOutput(KeyKind.Enter));
                break;
            case PadAction.PageUp:
                _keyboard.ToggleCapitals();
                break;
            case PadAction.PageDown:
                _keyboard.ToggleSymbols();
                break;
            case PadAction.Previous:
                KeySender.Send(KeySender.VK_LEFT);
                return;
            case PadAction.Next:
                KeySender.Send(KeySender.VK_RIGHT);
                return;
            case PadAction.Back or PadAction.Keyboard:
                HideKeyboard();
                return;
        }
        if (_keyboardShown) _keyboardView?.Show(_keyboard);
    }

    void Type(KeyOutput output)
    {
        switch (output.Kind)
        {
            case KeyKind.Text:
                KeySender.Type(output.Text);
                break;
            case KeyKind.Backspace:
                KeySender.Send(KeySender.VK_BACK);
                break;
            case KeyKind.Enter:
                KeySender.Send(KeySender.VK_RETURN);
                HideKeyboard();
                break;
            case KeyKind.Close:
                HideKeyboard();
                break;
        }
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
        foreach (var app in _running.Values)
        {
            Guard("Giving the volume back", () => RestoreVolume(app));
            app.Dispose(); // leave them running; just let go
        }
        _running.Clear();
    }
}
