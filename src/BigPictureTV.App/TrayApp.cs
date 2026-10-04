using System;
using System.Diagnostics;
using System.Linq;
using SynchronizationContext = System.Threading.SynchronizationContext;
using System.Windows.Forms;
using BigPictureTV.Core;
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
    bool _testing;
    DateTime _hotkeyQuietUntil;
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
        _controller.ModeChanged += OnModeChanged;
        _controller.LayoutChangedOutside += () => Notify(S.ChangedOutside, ToolTipIcon.Info);
        _controller.LeavingTvSoon += wait => Notify(string.Format(S.LeavingTvSoon, (int)Math.Round(wait.TotalSeconds)), ToolTipIcon.None);

        StartupRegistration.RefreshPath(_exePath);

        var (tv, reason) = TvSelector.Select(_display.ListDisplays(), _settings);
        _log.Write($"BigPictureTV started (TV: {tv?.ToString() ?? "not found"}, {reason}).");
        _controller.Start(_probe.IsOpen());
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
        _gamepad = new GamepadService(log, () => ui.Post(_ => OnToggleShortcut(), null));
        ApplyCombo();
        BuildMenu();

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
        // Off while the window is open, so pressing it in the shortcut box
        // records it instead of switching displays.
        _hotkeys.SetToggle(null);
        _gamepad.Combo = GamepadButtons.None;
        try
        {
            var result = WpfDialogs.ShowSettings(_settings, _display.ListDisplays(), firstRun,
                StartupRegistration.IsEnabled, TestSettings,
                () => _controller.Mode == DisplayMode.Desktop && !_store.HasSaved,
                _hotkeys.IsAvailable, () => _gamepad.Pressed);
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
        var combo = GamepadCombo.Parse(_settings.ControllerCombo) ?? GamepadButtons.None;
        if (combo == _gamepad.Combo) return;
        _gamepad.Combo = combo;
        _log.Write(combo == GamepadButtons.None ? "Controller combo off." : $"Controller combo: {GamepadCombo.Format(combo)}.");
    }

    // Switching displays takes a moment; a second press in the meantime
    // (or right after) would undo the first.
    void OnToggleShortcut()
    {
        if (_testing || DateTime.UtcNow < _hotkeyQuietUntil) return;
        Toggle();
        _hotkeyQuietUntil = DateTime.UtcNow.AddSeconds(1.5);
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
            _controller.Tick(_probe.IsOpen(), DateTime.UtcNow);
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
        UpdateIcon();
        Notify(mode == DisplayMode.Desktop ? S.NowOnDesktop : S.NowOnTv, ToolTipIcon.None);
    }

    void Notify(string text, ToolTipIcon kind) => _icon.ShowBalloonTip(3000, "BigPictureTV", text, kind);

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

        var logs = new ToolStripMenuItem(S.OpenLogFolder);
        logs.Click += (_, _) => Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });
        menu.Items.Add(logs);

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
        _hotkeys.Dispose();
        _gamepad.Dispose();
        try { _controller.Shutdown(); }
        catch (Exception e) { _log.Write($"Restoring the desktop on exit failed: {e.Message}"); }
        _icon.Visible = false;
        _icon.Dispose();
        _boldFont?.Dispose();
        _log.Write("BigPictureTV stopped.");
    }
}
