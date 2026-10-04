using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Threading;
using BigPictureTV.Core;
using BigPictureTV.Core.Detection;
using BigPictureTV.Core.Display;

namespace BigPictureTV.App;

/// <summary>
/// The icon next to the clock and its menu. Checks for Big Picture on a timer
/// and hands everything to ModeController, like `bptv watch` does.
/// </summary>
public sealed class TrayApp : IDisposable
{
    static readonly Strings S = Strings.Current;

    readonly ILog _log;
    readonly AppSettings _settings;
    readonly DisplayConfig _display = new();
    readonly ModeController _controller;
    readonly BigPictureWatcher _probe;
    readonly NotifyIcon _icon;
    readonly DispatcherTimer _timer;
    readonly string _exePath = Environment.ProcessPath ?? Application.ExecutablePath;
    bool _disposed;

    public TrayApp(ILog log)
    {
        _log = log;
        _settings = AppSettings.Load(AppPaths.SettingsFile, log);
        var store = new LayoutStore(AppPaths.LayoutFile);
        var manager = new DisplayManager(_display, store, ds => TvSelector.Select(ds, _settings), log);
        _controller = new ModeController(manager, log) { Grace = TimeSpan.FromSeconds(_settings.GraceSeconds) };
        _probe = new BigPictureWatcher(_settings.BigPictureTitles, _settings.ExtraProcesses);

        _icon = new NotifyIcon { ContextMenuStrip = new ContextMenuStrip(), Visible = true };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
        _controller.ModeChanged += OnModeChanged;

        StartupRegistration.RefreshPath(_exePath);

        var (tv, reason) = TvSelector.Select(_display.ListDisplays(), _settings);
        _log.Write($"BigPictureTV started (TV: {tv?.ToString() ?? "not found"}, {reason}).");
        _controller.Start(_probe.IsOpen());
        UpdateIcon();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.PollSeconds, 1, 60)) };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
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
        var toggle = new ToolStripMenuItem(onTv ? S.BackToDesktop : S.SwitchToTv) { Font = BoldMenuFont(menu) };
        toggle.Click += (_, _) => Toggle();
        menu.Items.Add(toggle);

        var pause = new ToolStripMenuItem(S.PauseAutomatic) { Checked = _controller.Paused };
        pause.Click += (_, _) => { _controller.SetPaused(!_controller.Paused); UpdateIcon(); };
        menu.Items.Add(pause);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(BuildTvMenu());

        var startup = new ToolStripMenuItem(S.StartWithWindows) { Checked = StartupRegistration.IsEnabled };
        startup.Click += (_, _) => StartupRegistration.Set(!StartupRegistration.IsEnabled, _exePath);
        menu.Items.Add(startup);

        var logs = new ToolStripMenuItem(S.OpenLogFolder);
        logs.Click += (_, _) => Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });
        menu.Items.Add(logs);

        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem(S.Exit);
        exit.Click += (_, _) => System.Windows.Application.Current.Shutdown();
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
        try { _controller.Shutdown(); }
        catch (Exception e) { _log.Write($"Restoring the desktop on exit failed: {e.Message}"); }
        _icon.Visible = false;
        _icon.Dispose();
        _boldFont?.Dispose();
        _log.Write("BigPictureTV stopped.");
    }
}
