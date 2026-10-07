using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BigPictureTV.Core.TvMenu;

namespace BigPictureTV.App;

/// <summary>
/// The TV menu: big tiles, full screen on the TV, driven by the controller
/// (through <see cref="Handle"/>), the keyboard or the mouse. Closes with the
/// chosen tile in <see cref="Chosen"/>, or null when the user backs out.
/// </summary>
public sealed class TvMenuWindow : Window
{
    static readonly Strings S = Strings.Current;
    static readonly string[] Palette = { "#2D6CDF", "#7A3FD1", "#1E8E6A", "#C4302B", "#D9822B", "#3A3F47" };

    readonly IReadOnlyList<TvApp> _apps;
    readonly List<Border> _tiles = new();
    readonly int _columns;
    readonly TextBlock _clock, _message;
    readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    int _selected;

    public TvApp? Chosen { get; private set; }

    public TvMenuWindow(IReadOnlyList<TvApp> apps, int selected, string? message)
    {
        _apps = apps;
        _columns = GridNav.Columns(apps.Count);
        _selected = Math.Clamp(selected, 0, Math.Max(0, apps.Count - 1));

        Title = S.TvMenu;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized; // on the primary display, which is the TV by now
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
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

        var grid = new UniformGrid { Columns = _columns, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < apps.Count; i++)
        {
            var tile = BuildTile(apps[i], i);
            _tiles.Add(tile);
            grid.Children.Add(tile);
        }

        _message = new TextBlock
        {
            Text = message ?? "", FontSize = 28, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x40)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16),
        };
        var hints = new TextBlock { Text = S.TvMenuHints, FontSize = 26, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center };
        var footer = new StackPanel { Margin = new Thickness(96, 16, 96, 56), Children = { _message, hints } };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(grid);
        Content = root;

        UpdateClock();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        Closed += (_, _) => _clockTimer.Stop();
        Loaded += (_, _) =>
        {
            Select(_selected);
            KeySender.ForceForeground(new WindowInteropHelper(this).Handle);
            Activate();
        };
        PreviewKeyDown += OnKey;
    }

    Border BuildTile(TvApp app, int index)
    {
        string name = app.Kind == TvAppKind.Desktop && app.Name.Length == 0 ? S.TvMenuDesktop : app.ToString();
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
            Child = new TextBlock
            {
                Text = name, FontSize = 40, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16),
            },
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
    }

    public int Selected => _selected;

    /// <summary>A controller action; safe to call from any thread.</summary>
    public void Handle(PadAction action) => Dispatcher.BeginInvoke(() =>
    {
        if (!IsLoaded) return;
        switch (action)
        {
            case PadAction.Up or PadAction.Down or PadAction.Left or PadAction.Right:
                Select(GridNav.Move(_selected, _tiles.Count, _columns, action));
                break;
            case PadAction.Accept:
                Accept();
                break;
            case PadAction.Back:
                Close();
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
        if (!_apps[_selected].IsUsable)
        {
            _message.Text = string.Format(S.TvMenuOpenFailed, _apps[_selected]);
            return;
        }
        Chosen = _apps[_selected];
        Close();
    }
}
