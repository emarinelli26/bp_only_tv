using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SynchronizationContext = System.Threading.SynchronizationContext;
using System.Windows.Forms;
using BigPictureTV.Core;
using BigPictureTV.Core.Audio;
using BigPictureTV.Core.Detection;
using BigPictureTV.Core.Display;
using BigPictureTV.Core.Input;

namespace BigPictureTV.App;

/// <summary>
/// The icon next to the clock and its menu. Checks for Big Picture on a timer
/// and hands everything to ModeController, like `bptv watch` does.
/// </summary>
public sealed class TrayApp : IDisposable
{
    static readonly Strings S = Strings.Current;

    readonly ILog _log;
    AppSettings _settings;
    readonly DisplayConfig _display = new();
    readonly ModeController _controller;
    readonly LayoutStore _store = new(AppPaths.LayoutFile);
    BigPictureWatcher _probe;
    readonly NotifyIcon _icon;
    readonly Timer _timer;
    readonly HotkeyService _hotkeys;
    readonly GamepadService _gamepad;
    readonly TvSession _tvMenu;
    bool _testing;
    DateTime _hotkeyQuietUntil;
    readonly Timer _updateTimer = new() { Interval = 60_000 };
    readonly Timer _handleTimer = new() { Interval = 30_000 };
    int _userObjectsLogged;
    (string Tag, string Url)? _update;
    string? _balloonUrl;
    readonly string _exePath = Environment.ProcessPath ?? Application.ExecutablePath;
    bool _disposed;

    public TrayApp(ILog log)
    {
        _log = log;
        _settings = AppSettings.Load(AppPaths.SettingsFile, log);
        var manager = new DisplayManager(_display, _store, ds => TvSelector.Select(ds, _settings), log,
            () => _settings.Layout);
        _controller = new ModeController(manager, log) { Grace = TimeSpan.FromSeconds(_settings.GraceSeconds) };
        _probe = NewProbe();

        _icon = new NotifyIcon { ContextMenuStrip = new ContextMenuStrip(), Visible = true };
        // WinForms cancels opening a menu that has no items yet, so the first
        // right-click did nothing: build it once now, and allow the opening.
        _icon.ContextMenuStrip.Opening += (_, e) => { SyncWithDisplays(); BuildMenu(); e.Cancel = false; };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
        _icon.BalloonTipClicked += (_, _) => { if (_balloonUrl != null) OpenUrl(_balloonUrl); };
        _controller.ModeChanged += OnModeChanged;
        _controller.LayoutChangedOutside += () => Notify(S.ChangedOutside, ToolTipIcon.Info);
        _controller.LeavingTvSoon += wait => Notify(string.Format(S.LeavingTvSoon, (int)Math.Round(wait.TotalSeconds)), ToolTipIcon.None);

        StartupRegistration.RefreshPath(_exePath);

        var (tv, reason) = TvSelector.Select(_display.ListDisplays(), _settings);
        _log.Write($"BigPictureTV started (TV: {tv?.ToString() ?? "not found"}, {reason}).");
        _controller.Start(_probe.IsOpen());
        if (_controller.Mode == DisplayMode.Desktop) RestoreAudio(); // left over from a crash
        UpdateIcon();

        _timer = new Timer { Interval = Math.Clamp(_settings.PollSeconds, 1, 60) * 1000 };
        _timer.Tick += (_, _) => Check();
        _timer.Start();

        _hotkeys = new HotkeyService(log);
        _hotkeys.TogglePressed += OnToggleShortcut;
        _hotkeys.EmergencyPressed += Emergency;
        if (!_hotkeys.RegisterEmergency())
            _log.Write($"Emergency shortcut {Hotkey.Emergency} is taken by another program.");
        ApplyHotkey();

        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _gamepad = new GamepadService(log, () => ui.Post(_ => OnToggleShortcut(), null), () => ui.Post(_ => OnMenuButton(), null));
        ApplyCombo();
        _tvMenu = new TvSession(log, _gamepad, ui, EnterTvForMenu, LeaveTvFromMenu, OpenBigPicture,
            () => ButtonsName(GamepadCombo.ParseAny(_settings.ControllerMenuButton) ?? GamepadButtons.None));
        BuildMenu();
        WriteMenuTilesOnce();

        _handleTimer.Tick += (_, _) => WatchHandles();
        _handleTimer.Start();
        WatchHandles();

        // First look a minute after starting, then once a day.
        _updateTimer.Tick += (_, _) => { _updateTimer.Interval = 24 * 60 * 60 * 1000; CheckForUpdates(); };
        _updateTimer.Start();

        if (!_settings.FirstRunDone)
        {
            var once = new Timer { Interval = 500 };
            once.Tick += (_, _) => { once.Dispose(); OpenSettings(firstRun: true); };
            once.Start();
        }
        else
        {
            MemoryTrim.Soon();
        }
    }

    BigPictureWatcher NewProbe() => new(_settings.BigPictureTitles, _settings.ExtraProcesses,
        _settings.DesktopWhenBigPictureHidden, _log);

    void OpenSettings(bool firstRun = false)
    {
        if (WpfDialogs.SettingsOpen)
        {
            WpfDialogs.ActivateSettings();
            return;
        }
        // The menu tiles may have been edited by hand in settings.json; keep those edits.
        _settings.TvMenuApps = AppSettings.Load(AppPaths.SettingsFile, _log).TvMenuApps;
        // Off while the window is open, so pressing it in the shortcut box
        // records it instead of switching displays.
        _hotkeys.SetToggle(null);
        _gamepad.Combo = GamepadButtons.None;
        _gamepad.MenuButton = GamepadButtons.None;
        try
        {
            var result = WpfDialogs.ShowSettings(_settings, _display.ListDisplays(), firstRun,
                StartupRegistration.IsEnabled, TestSettings,
                () => _controller.Mode == DisplayMode.Desktop && !_store.HasSaved,
                _hotkeys.IsAvailable, () => _gamepad.Pressed, AudioOutputs.List());
            if (result is { } saved)
            {
                ApplySettings(saved.Settings);
                StartupRegistration.Set(saved.StartWithWindows, _exePath);
            }
            else if (firstRun)
            {
                _settings.FirstRunDone = true; // don't greet again on every start
                _settings.Save(AppPaths.SettingsFile);
            }
        }
        catch (Exception e)
        {
            _log.Write($"Settings failed: {e}");
        }
        finally
        {
            ApplyHotkey();
            ApplyCombo();
            MemoryTrim.Soon();
        }
    }

    void ApplySettings(AppSettings updated)
    {
        _settings = updated;
        _settings.Save(AppPaths.SettingsFile);
        _controller.Grace = TimeSpan.FromSeconds(_settings.GraceSeconds);
        _probe = NewProbe(); // the shortcut is applied when the settings window closes
        var (tv, reason) = TvSelector.Select(_display.ListDisplays(), _settings);
        _log.Write($"Settings saved (TV: {tv?.ToString() ?? "not found"}, {reason}; layout: {_settings.Layout}).");
    }

    void ApplyHotkey()
    {
        var hotkey = Hotkey.Parse(_settings.Hotkey);
        if (hotkey == null && _settings.Hotkey.Trim().Length > 0)
            _log.Write($"Shortcut \"{_settings.Hotkey}\" in the settings isn't valid; no shortcut.");
        if (hotkey == _hotkeys.Toggle) return;
        if (_hotkeys.SetToggle(hotkey))
        {
            _log.Write(hotkey == null ? "Keyboard shortcut off." : $"Keyboard shortcut: {hotkey}.");
            return;
        }
        _log.Write($"Keyboard shortcut {hotkey} is taken by another program.");
        Notify(string.Format(S.HotkeyTakenNotify, hotkey), ToolTipIcon.Warning);
    }

    void ApplyCombo()
    {
        _gamepad.Rumble = _settings.ControllerRumble;
        _gamepad.MenuHold = Math.Clamp(_settings.ControllerMenuHold, 0, BindingDetector.MaxHoldSeconds);
        _gamepad.ComboHold = Math.Clamp(_settings.ControllerComboHold, 0, BindingDetector.MaxHoldSeconds);
        var menuButton = GamepadCombo.ParseAny(_settings.ControllerMenuButton) ?? GamepadButtons.None;
        if (menuButton != _gamepad.MenuButton)
        {
            _gamepad.MenuButton = menuButton;
            _log.Write(menuButton == GamepadButtons.None ? "Controller menu button off." : $"Controller menu button: {GamepadCombo.Format(menuButton)} ({_gamepad.MenuHold} s).");
        }
        var combo = GamepadCombo.ParseAny(_settings.ControllerCombo) ?? GamepadButtons.None;
        if (combo == _gamepad.Combo) return;
        _gamepad.Combo = combo;
        _log.Write(combo == GamepadButtons.None ? "Controller combo off." : $"Controller combo: {GamepadCombo.Format(combo)} ({_gamepad.ComboHold} s).");
    }

    // Windows allows a program 10,000 windows, menus, icons and the like; at
    // the limit anything that needs a new one fails and the app closed. Keep
    // an eye on the count so a leak shows in the log long before that, and
    // turn off the newest suspect (the PlayStation/Switch controller reader)
    // if it keeps growing.
    void WatchHandles()
    {
        int user = GuiResources.UserObjects, gdi = GuiResources.GdiObjects;
        if (_userObjectsLogged == 0 || user > _userObjectsLogged + 300)
        {
            _log.Write($"Windows objects in use: {user} USER, {gdi} GDI.");
            _userObjectsLogged = user;
        }
        if (user > 4000 && _gamepad.ReadsOtherControllers)
        {
            _log.Write("Too many Windows objects in use; stopping the PlayStation/Switch controller reader to find out if it's the cause.");
            _gamepad.DropOtherControllers();
        }
    }

    void OnMenuButton()
    {
        if (_testing || WpfDialogs.SettingsOpen) return;
        _tvMenu.Toggle();
    }

    /// <summary>How to call controller buttons on screen, with PlayStation names next to Xbox ones.</summary>
    internal static string ButtonsName(GamepadButtons buttons) => buttons switch
    {
        GamepadButtons.None => "",
        GamepadButtons.Back => S.MenuButtonBack,
        GamepadButtons.Start => S.MenuButtonStart,
        GamepadButtons.LS => S.MenuButtonLS,
        GamepadButtons.RS => S.MenuButtonRS,
        _ => GamepadCombo.Format(buttons),
    };

    // Switching displays takes a moment; a second press in the meantime
    // (or right after) would undo the first.
    void OnToggleShortcut()
    {
        if (_testing || DateTime.UtcNow < _hotkeyQuietUntil) return;
        if (_settings.ShortcutOpensTvMenu) _tvMenu.Toggle();
        else if (_settings.ShortcutOpensBigPicture && _controller.Mode == DisplayMode.Desktop) OpenBigPicture();
        else Toggle();
        _hotkeyQuietUntil = DateTime.UtcNow.AddSeconds(1.5);
    }

    // So the TV menu tiles show up in settings.json, ready to be edited by hand.
    void WriteMenuTilesOnce()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile) && !File.ReadAllText(AppPaths.SettingsFile).Contains("\"TvMenuApps\""))
                _settings.Save(AppPaths.SettingsFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Write($"Writing the TV menu tiles to the settings failed: {e.Message}");
        }
    }

    (bool Ok, bool Switched) EnterTvForMenu()
    {
        if (_controller.Mode != DisplayMode.Desktop) return (true, false);
        Toggle();
        bool onTv = _controller.Mode != DisplayMode.Desktop;
        return (onTv, onTv);
    }

    void LeaveTvFromMenu()
    {
        if (_controller.Mode != DisplayMode.Desktop) Toggle();
    }

    /// <summary>Switches to the TV first, then opens Big Picture, so Steam starts on the TV.</summary>
    void OpenBigPicture()
    {
        try
        {
            if (!_controller.OpenBigPicture(DateTime.UtcNow)) Notify(S.SwitchFailed, ToolTipIcon.Warning);
        }
        catch (Exception e)
        {
            _log.Write($"Switching for Big Picture failed: {e.Message}");
        }
        try
        {
            Process.Start(new ProcessStartInfo("steam://open/bigpicture") { UseShellExecute = true })?.Dispose();
            _log.Write("Opening Big Picture.");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Write($"Opening Big Picture failed: {e.Message}");
            Notify(S.SteamNotFound, ToolTipIcon.Warning);
        }
    }

    // The sound follows the picture: to the TV's output while on it, back to
    // whatever it was afterwards. The previous output is kept in a file so a
    // crash on the TV still gets it back next start.
    void MoveAudioToTv()
    {
        if (!_settings.SwitchAudio) return;
        try
        {
            var outputs = AudioOutputs.List();
            var tvName = TvSelector.Select(_display.ListDisplays(), _settings).Tv?.Name;
            var target = AudioPicker.Pick(outputs, _settings.AudioDeviceId, tvName);
            if (target == null)
            {
                _log.Write("No sound output matches the TV; pick one in Settings.");
                return;
            }
            string? current = AudioOutputs.DefaultId();
            if (current == null || current == target.Id) return;
            if (!File.Exists(AppPaths.AudioFile)) File.WriteAllText(AppPaths.AudioFile, current);
            _log.Write(AudioOutputs.SetDefault(target.Id) ? $"Sound moved to {target}." : $"Windows refused to move the sound to {target}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _log.Write($"Moving the sound failed: {e.Message}");
        }
    }

    void RestoreAudio()
    {
        try
        {
            if (!File.Exists(AppPaths.AudioFile)) return;
            string id = File.ReadAllText(AppPaths.AudioFile).Trim();
            File.Delete(AppPaths.AudioFile);
            var previous = AudioOutputs.List().FirstOrDefault(d => d.Id == id);
            if (previous == null) return; // unplugged meanwhile; leave Windows' choice
            _log.Write(AudioOutputs.SetDefault(id) ? $"Sound back on {previous}." : $"Windows refused to put the sound back on {previous}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Write($"Putting the sound back failed: {e.Message}");
        }
    }

    async void CheckForUpdates()
    {
        if (!_settings.CheckForUpdates || _disposed) return;
        var found = await UpdateChecker.FindNewerAsync(_log);
        MemoryTrim.Soon();
        if (found == null || found == _update || _disposed) return;
        _update = found;
        _log.Write($"Version {found.Value.Tag} is available.");
        Notify(string.Format(S.UpdateAvailable, found.Value.Tag), ToolTipIcon.Info, found.Value.Url);
    }

    void CopyDiagnostics()
    {
        try
        {
            var displays = _display.ListDisplays();
            var (tv, reason) = TvSelector.Select(displays, _settings);
            string report = DiagnosticReport.Build(UpdateChecker.Current.ToString(3),
                $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} (app holds {GuiResources.UserObjects} USER, {GuiResources.GdiObjects} GDI objects)",
                displays, tv, reason, _settings,
                _controller.Mode, DiagnosticReport.Tail(AppPaths.LogFile, 40));
            Clipboard.SetText(report);
            Notify(S.DiagnosticsCopied, ToolTipIcon.Info);
        }
        catch (Exception e)
        {
            _log.Write($"Copying diagnostic info failed: {e.Message}");
        }
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    void Emergency()
    {
        _log.Write($"{Hotkey.Emergency} pressed.");
        try
        {
            bool wasDesktop = _controller.Mode == DisplayMode.Desktop;
            _controller.RestoreNow(_probe.IsOpen());
            if (wasDesktop) Notify(S.NowOnDesktop, ToolTipIcon.None);
        }
        catch (Exception e)
        {
            _log.Write($"Emergency restore failed: {e.Message}");
        }
    }

    /// <summary>Settings' Test button: switch with the draft settings, show the countdown on the TV, come back.</summary>
    bool TestSettings(AppSettings draft)
    {
        _timer.Stop();
        _testing = true;
        try
        {
            var tester = new DisplayManager(_display, _store, ds => TvSelector.Select(ds, draft), _log, () => draft.Layout);
            if (!tester.SwitchToTv())
            {
                if (_store.HasSaved) tester.RestoreDesktop();
                return false;
            }
            WpfDialogs.ShowTestCountdown();
            tester.RestoreDesktop();
            return true;
        }
        finally
        {
            _testing = false;
            _timer.Start();
        }
    }

    void Check()
    {
        try
        {
            // An app opened from the TV menu counts as Big Picture, so the
            // TV stays on while it runs even if Big Picture is hidden.
            bool open = _probe.IsOpen() || (_tvMenu.Active && _controller.Mode != DisplayMode.Desktop);
            _controller.Tick(open, DateTime.UtcNow);
        }
        catch (Exception e)
        {
            _log.Write($"Check failed: {e.Message}");
        }
    }

    void SyncWithDisplays()
    {
        try { _controller.SyncWithDisplays(_probe.IsOpen()); }
        catch (Exception e) { _log.Write($"Display check failed: {e.Message}"); }
    }

    void Toggle()
    {
        try
        {
            bool wasDesktop = _controller.Mode == DisplayMode.Desktop;
            _controller.Toggle(_probe.IsOpen());
            if (wasDesktop && _controller.Mode == DisplayMode.Desktop)
                Notify(S.SwitchFailed, ToolTipIcon.Warning);
        }
        catch (Exception e)
        {
            _log.Write($"Toggle failed: {e.Message}");
            Notify(S.SwitchFailed, ToolTipIcon.Warning);
        }
    }

    void OnModeChanged(DisplayMode mode)
    {
        if (mode == DisplayMode.Desktop) RestoreAudio();
        else MoveAudioToTv();
        UpdateIcon();
        Notify(mode == DisplayMode.Desktop ? S.NowOnDesktop : S.NowOnTv, ToolTipIcon.None);
    }

    void Notify(string text, ToolTipIcon kind, string? url = null)
    {
        _balloonUrl = url;
        _icon.ShowBalloonTip(3000, "BigPictureTV", text, kind);
    }

    void UpdateIcon()
    {
        bool onTv = _controller.Mode != DisplayMode.Desktop;
        _icon.Icon = onTv ? TrayIcons.Tv : _controller.Paused ? TrayIcons.Paused : TrayIcons.Desktop;
        string text = $"BigPictureTV: {StateText()}";
        _icon.Text = text.Length <= 63 ? text : text[..63]; // Windows limit
    }

    string StateText()
    {
        string state = _controller.Mode switch
        {
            DisplayMode.TvAuto => S.StateTvAuto,
            DisplayMode.TvManual => S.StateTvManual,
            _ => S.StateDesktop,
        };
        return _controller.Paused ? $"{state} ({S.Paused})" : state;
    }

    // Rebuilt every time the menu opens, so it always shows the current state
    // and the displays connected right now.
    void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();

        menu.Items.Add(new ToolStripMenuItem(StateText()) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        bool onTv = _controller.Mode != DisplayMode.Desktop;
        var toggle = new ToolStripMenuItem(onTv ? S.BackToDesktop : S.SwitchToTv)
        {
            Font = BoldMenuFont(menu),
            ShortcutKeyDisplayString = _hotkeys.Toggle?.ToString(),
        };
        toggle.Click += (_, _) => Toggle();
        menu.Items.Add(toggle);

        var openBp = new ToolStripMenuItem(S.OpenBigPicture);
        openBp.Click += (_, _) => OpenBigPicture();
        menu.Items.Add(openBp);

        var tvMenu = new ToolStripMenuItem(S.TvMenuOpen);
        tvMenu.Click += (_, _) => _tvMenu.Open();
        menu.Items.Add(tvMenu);

        var pause = new ToolStripMenuItem(S.PauseAutomatic) { Checked = _controller.Paused };
        pause.Click += (_, _) => { _controller.SetPaused(!_controller.Paused); UpdateIcon(); };
        menu.Items.Add(pause);

        menu.Items.Add(new ToolStripSeparator());
        var settings = new ToolStripMenuItem(S.Settings);
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);
        menu.Items.Add(BuildTvMenu());

        var startup = new ToolStripMenuItem(S.StartWithWindows) { Checked = StartupRegistration.IsEnabled };
        startup.Click += (_, _) => StartupRegistration.Set(!StartupRegistration.IsEnabled, _exePath);
        menu.Items.Add(startup);

        var diagnostics = new ToolStripMenuItem(S.CopyDiagnostics);
        diagnostics.Click += (_, _) => CopyDiagnostics();
        menu.Items.Add(diagnostics);

        var logs = new ToolStripMenuItem(S.OpenLogFolder);
        logs.Click += (_, _) => Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });
        menu.Items.Add(logs);

        if (_update is { } update)
        {
            var download = new ToolStripMenuItem(string.Format(S.DownloadUpdate, update.Tag));
            download.Click += (_, _) => OpenUrl(update.Url);
            menu.Items.Add(download);
        }

        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem(S.Exit);
        exit.Click += (_, _) => Application.Exit();
        menu.Items.Add(exit);
    }

    ToolStripMenuItem BuildTvMenu()
    {
        var parent = new ToolStripMenuItem(S.ChooseTv);
        var displays = _display.ListDisplays();
        var guess = TvDetector.Guess(displays);
        bool automatic = _settings.TvDevicePath.Length == 0 && _settings.TvName.Length == 0;

        var auto = new ToolStripMenuItem($"{S.DetectAutomatically} ({guess?.ToString() ?? S.DetectedNone})")
        {
            Checked = automatic,
        };
        auto.Click += (_, _) => ChooseTv(null);
        parent.DropDownItems.Add(auto);
        parent.DropDownItems.Add(new ToolStripSeparator());

        if (displays.Count == 0)
            parent.DropDownItems.Add(new ToolStripMenuItem(S.NoDisplays) { Enabled = false });

        var chosen = automatic ? null : TvSelector.Select(displays, _settings).Tv;
        foreach (var d in displays)
        {
            string label = string.IsNullOrEmpty(d.Name) ? d.DevicePath : d.Name;
            if (d.Width > 0) label += $"  ({d.Width}x{d.Height})";
            var item = new ToolStripMenuItem(label) { Checked = ReferenceEquals(d, chosen) };
            item.Click += (_, _) => ChooseTv(d);
            parent.DropDownItems.Add(item);
        }
        return parent;
    }

    void ChooseTv(DisplayInfo? tv)
    {
        _settings.TvDevicePath = tv?.DevicePath ?? "";
        _settings.TvName = tv?.Name ?? "";
        _settings.Save(AppPaths.SettingsFile);
        _log.Write(tv == null ? "TV set to automatic detection." : $"TV set to {tv}.");
    }

    System.Drawing.Font? _boldFont;

    System.Drawing.Font BoldMenuFont(ContextMenuStrip menu) =>
        _boldFont ??= new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _updateTimer.Dispose();
        _handleTimer.Dispose();
        _hotkeys.Dispose();
        _tvMenu.Dispose();
        _gamepad.Dispose();
        try { _controller.Shutdown(); }
        catch (Exception e) { _log.Write($"Restoring the desktop on exit failed: {e.Message}"); }
        _icon.Visible = false;
        _icon.Dispose();
        _boldFont?.Dispose();
        _log.Write("BigPictureTV stopped.");
    }
}
