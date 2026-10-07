using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BigPictureTV.Core.TvMenu;

namespace BigPictureTV.App;

/// <summary>What the TV menu session needs from the menu, without touching WPF types.</summary>
interface ITvMenuView
{
    /// <summary>Shows the menu (or refreshes it if showing). <paramref name="open"/>: keys of the tiles whose app is running.</summary>
    void Show(IReadOnlyList<TvApp> apps, IReadOnlyCollection<string> open, TvApp? current, string? message, string menuButton);

    void Hide();

    /// <summary>A controller action; safe from any thread.</summary>
    void Handle(PadAction action);

    /// <summary>A on a tile.</summary>
    event Action<TvApp> Chosen;

    /// <summary>X on a tile whose app is running.</summary>
    event Action<TvApp> CloseRequested;

    /// <summary>B or Esc.</summary>
    event Action BackRequested;
}

/// <summary>
/// The TV menu: big tiles, full screen on the TV, driven by the controller,
/// the keyboard or the mouse. Made once and then shown and hidden, like a
/// console's home screen; apps opened from it keep running behind it.
/// </summary>
sealed class TvMenuWindow : Window, ITvMenuView
{
    static readonly Strings S = Strings.Current;
    static readonly string[] Palette = { "#2D6CDF", "#7A3FD1", "#1E8E6A", "#C4302B", "#D9822B", "#3A3F47" };

    readonly UniformGrid _grid = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _clock, _message, _hints, _hintMenu;
    readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    readonly List<Border> _tiles = new();
    IReadOnlyList<TvApp> _apps = Array.Empty<TvApp>();
    IReadOnlyCollection<string> _open = Array.Empty<string>();
    TvApp? _current;
    int _selected, _columns = 1;

    public event Action<TvApp>? Chosen;
    public event Action<TvApp>? CloseRequested;
    public event Action? BackRequested;

    public TvMenuWindow()
    {
        Title = S.TvMenu;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x13, 0x18));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        Cursor = Cursors.None;

        _clock = new TextBlock { FontSize = 40, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Right };
        var header = new DockPanel { Margin = new Thickness(96, 64, 96, 32) };
        DockPanel.SetDock(_clock, Dock.Right);
        header.Children.Add(_clock);
        header.Children.Add(new TextBlock { Text = S.TvMenu, FontSize = 56, FontWeight = FontWeights.SemiBold });

        _message = new TextBlock
        {
            FontSize = 28, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x40)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16),
        };
        _hints = new TextBlock { FontSize = 28, Opacity = 0.85, HorizontalAlignment = HorizontalAlignment.Center };
        _hintMenu = new TextBlock { FontSize = 22, Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        var footer = new StackPanel { Margin = new Thickness(96, 16, 96, 56), Children = { _message, _hints, _hintMenu } };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(_grid);
        Content = root;

        _clockTimer.Tick += (_, _) => UpdateClock();
        PreviewKeyDown += OnKey;
        // Keyboard input in a WPF window that lives in the tray's WinForms loop.
        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);
    }

    public void Show(IReadOnlyList<TvApp> apps, IReadOnlyCollection<string> open, TvApp? current, string? message, string menuButton)
    {
        int keep = _selected < _apps.Count ? IndexOf(apps, _apps[_selected].Key) : -1;
        _apps = apps;
        _open = open;
        _current = current;
        _columns = GridNav.Columns(apps.Count);
        _grid.Columns = _columns;
        _grid.Children.Clear();
        _tiles.Clear();
        for (int i = 0; i < apps.Count; i++)
        {
            var tile = BuildTile(apps[i], i);
            _tiles.Add(tile);
            _grid.Children.Add(tile);
        }
        // Start on the app in front, else where the selection was.
        int index = current != null ? IndexOf(apps, current.Key) : keep;
        Select(Math.Clamp(index < 0 ? _selected : index, 0, Math.Max(0, apps.Count - 1)));
        _message.Text = message ?? "";
        _message.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        _hintMenu.Text = menuButton.Length > 0 ? string.Format(S.MenuButtonHint, menuButton) : "";

        UpdateClock();
        _clockTimer.Start();
        if (!IsVisible)
        {
            // Maximize again on the display that is primary now (the TV), which starts at 0,0.
            WindowState = WindowState.Normal;
            Left = 0;
            Top = 0;
            base.Show();
            WindowState = WindowState.Maximized;
        }
        KeySender.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
    }

    void ITvMenuView.Hide()
    {
        _clockTimer.Stop();
        Hide();
    }

    // Alt+F4 and the like hide the menu instead of destroying it.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        BackRequested?.Invoke();
    }

    static int IndexOf(IReadOnlyList<TvApp> apps, string key)
    {
        for (int i = 0; i < apps.Count; i++)
            if (apps[i].Key == key) return i;
        return -1;
    }

    Border BuildTile(TvApp app, int index)
    {
        string name = app.Kind == TvAppKind.Desktop && app.Name.Length == 0 ? S.TvMenuDesktop : app.ToString();
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16) };
        label.Children.Add(new TextBlock
        {
            Text = name, FontSize = 40, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (_open.Contains(app.Key))
        {
            label.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 2, 14, 4), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock { Text = "● " + S.TvMenuRunning, FontSize = 22 },
            });
        }
        var tile = new Border
        {
            Width = 340,
            Height = 200,
            Margin = new Thickness(20),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(TileColor(app, index)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            Opacity = app.IsUsable ? 1 : 0.4,
            Child = label,
        };
        tile.MouseLeftButtonUp += (_, _) => { Select(index); Accept(); };
        return tile;
    }

    static Color TileColor(TvApp app, int index)
    {
        try
        {
            if (app.Color.Length > 0 && ColorConverter.ConvertFromString(app.Color) is Color c) return c;
        }
        catch (FormatException) { }
        return (Color)ColorConverter.ConvertFromString(Palette[index % Palette.Length]);
    }

    void UpdateClock() => _clock.Text = DateTime.Now.ToString("t");

    void Select(int index)
    {
        if (_tiles.Count == 0) return;
        _selected = index;
        for (int i = 0; i < _tiles.Count; i++)
        {
            bool on = i == index;
            _tiles[i].BorderThickness = new Thickness(on ? 6 : 0);
            _tiles[i].RenderTransform = new ScaleTransform(on ? 1.08 : 1, on ? 1.08 : 1);
        }
        bool open = _selected < _apps.Count && _open.Contains(_apps[_selected].Key);
        var parts = new List<string> { open ? S.HintResume : S.HintOpen };
        if (open) parts.Add(S.HintCloseApp);
        parts.Add(_current != null ? string.Format(S.HintBackTo, _current) : S.HintCloseMenu);
        _hints.Text = string.Join("        ", parts);
    }

    public void Handle(PadAction action) => Dispatcher.BeginInvoke(() =>
    {
        if (!IsVisible) return;
        switch (action)
        {
            case PadAction.Up or PadAction.Down or PadAction.Left or PadAction.Right:
                Select(GridNav.Move(_selected, _tiles.Count, _columns, action));
                break;
            case PadAction.Accept:
                Accept();
                break;
            case PadAction.Option:
                if (_selected < _apps.Count && _open.Contains(_apps[_selected].Key)) CloseRequested?.Invoke(_apps[_selected]);
                break;
            case PadAction.Back:
                BackRequested?.Invoke();
                break;
        }
    });

    void OnKey(object sender, KeyEventArgs e)
    {
        PadAction? action = e.Key switch
        {
            Key.Up => PadAction.Up,
            Key.Down => PadAction.Down,
            Key.Left => PadAction.Left,
            Key.Right => PadAction.Right,
            Key.Enter or Key.Space => PadAction.Accept,
            Key.Delete or Key.X => PadAction.Option,
            Key.Escape or Key.Back => PadAction.Back,
            _ => null,
        };
        if (action == null) return;
        e.Handled = true;
        Handle(action.Value);
    }

    void Accept()
    {
        if (_selected >= _apps.Count) return;
        var app = _apps[_selected];
        if (!app.IsUsable)
        {
            _message.Text = string.Format(S.TvMenuOpenFailed, app);
            _message.Visibility = Visibility.Visible;
            return;
        }
        Chosen?.Invoke(app);
    }
}
