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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BigPictureTV.Core.TvMenu;

namespace BigPictureTV.App;

/// <summary>What the TV menu session needs from the menu, without touching WPF types.</summary>
interface ITvMenuView
{
    /// <summary>Shows the menu (or refreshes it if showing). <paramref name="open"/>: keys of the tiles whose app is running.</summary>
    void Show(IReadOnlyList<TvApp> apps, IReadOnlyCollection<string> open, TvApp? current, string? message, string menuButton);

    void Hide();

    /// <summary>Puts the logos found so far on the tiles (image bytes by tile key).</summary>
    void ShowIcons(IReadOnlyDictionary<string, byte[]> icons);

    /// <summary>A controller action; safe from any thread.</summary>
    void Handle(PadAction action);

    /// <summary>A on a tile.</summary>
    event Action<TvApp> Chosen;

    /// <summary>X on a tile whose app is running.</summary>
    event Action<TvApp> CloseRequested;

    /// <summary>B or Esc.</summary>
    event Action BackRequested;

    /// <summary>Another window came to the front (a click on the game, a game's own alert).</summary>
    event Action LostFront;

    /// <summary>Shows what is playing on the PC, or hides the strip (null).</summary>
    void ShowNowPlaying(NowPlayingInfo? info);

    /// <summary>Shows the sound output in use, or hides it (null).</summary>
    void ShowAudioOutput(string? name);

    /// <summary>LB/RB (skip), Start or A on the music strip (play/pause).</summary>
    event Action<PadAction> MediaRequested;

    /// <summary>A on the sound output: move the sound to the next output.</summary>
    event Action AudioRequested;

    /// <summary>Right stick up (+1) or down (-1): the PC's volume when on the sound output, else the music's.</summary>
    event Action<bool, int> VolumeRequested;
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
    readonly Dictionary<string, Image> _tileImages = new(); // tile key: its logo
    IReadOnlyDictionary<string, byte[]> _icons = new Dictionary<string, byte[]>();
    readonly Dictionary<string, ImageSource?> _decoded = new(); // tile key: its logo, drawn once
    readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(96, 8, 96, 8) };
    readonly Border _musicCard, _audioCard;
    readonly Image _cover = new() { Width = 96, Height = 96, Stretch = Stretch.UniformToFill };
    readonly TextBlock _songTitle, _songArtist, _songState, _audioName;
    NowPlayingInfo? _nowPlaying;
    string? _audioOutput;
    bool _inBar;  // the selection is on the strip under the tiles
    int _barItem; // which of BarItems()
    IReadOnlyList<TvApp> _apps = Array.Empty<TvApp>();
    IReadOnlyCollection<string> _open = Array.Empty<string>();
    TvApp? _current;
    int _selected, _columns = 1;

    public event Action<TvApp>? Chosen;
    public event Action<TvApp>? CloseRequested;
    public event Action? BackRequested;
    public event Action? LostFront;
    public event Action<PadAction>? MediaRequested;
    public event Action? AudioRequested;
    public event Action<bool, int>? VolumeRequested;

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

        // Under the tiles, like a console's control center: what is playing and where the sound goes.
        _songTitle = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _songArtist = new TextBlock { FontSize = 22, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis };
        _songState = new TextBlock { FontSize = 20, Opacity = 0.6, Margin = new Thickness(0, 4, 0, 0) };
        var song = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0), Width = 460, Children = { _songTitle, _songArtist, _songState } };
        var coverFrame = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Child = _cover };
        _musicCard = Card(new StackPanel { Orientation = Orientation.Horizontal, Children = { coverFrame, song } });
        _audioName = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 380 };
        _audioCard = Card(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = "🔊  " + S.AudioOutput, FontSize = 22, Opacity = 0.75 }, _audioName },
        });
        _audioCard.MinHeight = 128;
        _musicCard.MouseLeftButtonUp += (_, _) => MediaRequested?.Invoke(PadAction.PlayPause);
        _audioCard.MouseLeftButtonUp += (_, _) => AudioRequested?.Invoke();
        _bar.Children.Add(_musicCard);
        _bar.Children.Add(_audioCard);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(_bar, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(_bar);
        root.Children.Add(_grid);
        Content = root;

        _clockTimer.Tick += (_, _) => UpdateClock();
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => { if (IsVisible) LostFront?.Invoke(); };
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
        _tileImages.Clear();
        for (int i = 0; i < apps.Count; i++)
        {
            var tile = BuildTile(apps[i], i);
            _tiles.Add(tile);
            _grid.Children.Add(tile);
        }
        // Start on the app in front, else where the selection was.
        if (!IsVisible) _inBar = false; // opening the menu starts on the tiles
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

    static Border Card(UIElement content) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x27, 0x30)),
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(16),
        Margin = new Thickness(16, 0, 16, 0),
        BorderBrush = Brushes.White,
        RenderTransformOrigin = new Point(0.5, 0.5),
        Child = content,
    };

    public void ShowNowPlaying(NowPlayingInfo? info)
    {
        if (info == _nowPlaying) return;
        bool newCover = !ReferenceEquals(info?.Cover, _nowPlaying?.Cover);
        _nowPlaying = info;
        if (info != null)
        {
            _songTitle.Text = info.Title.Length > 0 ? info.Title : info.App;
            _songArtist.Text = info.Artist.Length > 0 && info.App.Length > 0 ? $"{info.Artist}  ·  {info.App}" : info.Artist.Length > 0 ? info.Artist : info.App;
            _songState.Text = info.Playing ? "▶  " + S.NowPlayingPlaying : "❚❚  " + S.NowPlayingPaused;
            if (newCover) _cover.Source = Cover(info.Cover);
        }
        Select(_selected);
    }

    public void ShowAudioOutput(string? name)
    {
        _audioOutput = name;
        _audioName.Text = name ?? "";
        Select(_selected);
    }

    static ImageSource? Cover(byte[]? bytes)
    {
        if (bytes == null) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 192;
            image.StreamSource = new System.IO.MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is NotSupportedException or System.IO.IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // The strip's cards that are showing, left to right.
    List<Border> BarItems()
    {
        var items = new List<Border>();
        if (_nowPlaying != null) items.Add(_musicCard);
        if (_audioOutput != null) items.Add(_audioCard);
        return items;
    }

    void Move(PadAction direction)
    {
        var bar = BarItems();
        if (_inBar)
        {
            if (direction == PadAction.Up) _inBar = false;
            else if (direction == PadAction.Left) _barItem = Math.Max(0, _barItem - 1);
            else if (direction == PadAction.Right) _barItem = Math.Min(bar.Count - 1, _barItem + 1);
            Select(_selected);
            return;
        }
        int next = GridNav.Move(_selected, _tiles.Count, _columns, direction);
        if (direction == PadAction.Down && next == _selected && bar.Count > 0) _inBar = true; // down from the last row
        Select(next);
    }

    void AcceptBar()
    {
        var bar = BarItems();
        if (_barItem >= bar.Count) return;
        if (bar[_barItem] == _musicCard) MediaRequested?.Invoke(PadAction.PlayPause);
        else AudioRequested?.Invoke();
    }

    static int IndexOf(IReadOnlyList<TvApp> apps, string key)
    {
        for (int i = 0; i < apps.Count; i++)
            if (apps[i].Key == key) return i;
        return -1;
    }

    public void ShowIcons(IReadOnlyDictionary<string, byte[]> icons)
    {
        foreach (var key in icons.Keys.Where(k => !_icons.TryGetValue(k, out var old) || !ReferenceEquals(old, icons[k])).ToList())
            _decoded.Remove(key);
        _icons = icons;
        foreach (var (key, image) in _tileImages) SetLogo(image, key);
    }

    void SetLogo(Image image, string key)
    {
        if (!_decoded.TryGetValue(key, out var source))
            _decoded[key] = source = _icons.TryGetValue(key, out var bytes) ? Logo(bytes) : null;
        image.Source = source;
        image.Visibility = source == null ? Visibility.Collapsed : Visibility.Visible;
    }

    // The largest picture in the file: an .ico holds several sizes, the first often 16 px.
    static ImageSource? Logo(byte[] bytes)
    {
        try
        {
            var decoder = BitmapDecoder.Create(new System.IO.MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
            frame?.Freeze();
            return frame;
        }
        catch (Exception e) when (e is NotSupportedException or System.IO.IOException or ArgumentException or InvalidOperationException or System.IO.FileFormatException)
        {
            return null;
        }
    }

    Border BuildTile(TvApp app, int index)
    {
        string name = app.Kind == TvAppKind.Desktop && app.Name.Length == 0 ? S.TvMenuDesktop : app.ToString();
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 12, 16, 12) };
        if (app.Kind == TvAppKind.Desktop && app.Icon.Trim().Length == 0)
        {
            label.Children.Add(new TextBlock
            {
                Text = "\uE7F4", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 64,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6),
            });
        }
        else
        {
            var logo = new Image { Width = 76, Height = 76, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            _tileImages[app.Key] = logo;
            SetLogo(logo, app.Key);
            label.Children.Add(logo);
        }
        label.Children.Add(new TextBlock
        {
            Text = name, FontSize = 34, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 300,
        });
        if (_open.Contains(app.Key))
        {
            label.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 1, 12, 3), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock { Text = "● " + S.TvMenuRunning, FontSize = 20 },
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
        tile.MouseLeftButtonUp += (_, _) => { _inBar = false; Select(index); Accept(); };
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
        var bar = BarItems();
        _musicCard.Visibility = _nowPlaying != null ? Visibility.Visible : Visibility.Collapsed;
        _audioCard.Visibility = _audioOutput != null ? Visibility.Visible : Visibility.Collapsed;
        if (bar.Count == 0) _inBar = false;
        _barItem = Math.Clamp(_barItem, 0, Math.Max(0, bar.Count - 1));
        foreach (var card in new[] { _musicCard, _audioCard })
        {
            bool on = _inBar && _barItem < bar.Count && bar[_barItem] == card;
            card.BorderThickness = new Thickness(on ? 5 : 0);
            card.RenderTransform = new ScaleTransform(on ? 1.04 : 1, on ? 1.04 : 1);
        }
        if (_inBar)
        {
            foreach (var tile in _tiles)
            {
                tile.BorderThickness = new Thickness(0);
                tile.RenderTransform = new ScaleTransform(1, 1);
            }
            _selected = index;
            _hints.Text = bar[_barItem] == _musicCard
                ? string.Join("        ", S.HintPlayPause, S.HintSkip, S.HintMusicVolume, S.HintBackToTiles)
                : string.Join("        ", S.HintNextOutput, S.HintWindowsVolume, S.HintBackToTiles);
            return;
        }

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
                Move(action);
                break;
            case PadAction.Accept:
                if (_inBar) AcceptBar();
                else Accept();
                break;
            case PadAction.Option:
                if (!_inBar && _selected < _apps.Count && _open.Contains(_apps[_selected].Key)) CloseRequested?.Invoke(_apps[_selected]);
                break;
            case PadAction.Previous or PadAction.Next or PadAction.PlayPause:
                if (_nowPlaying != null) MediaRequested?.Invoke(action);
                break;
            case PadAction.VolumeUp or PadAction.VolumeDown:
                var bar = BarItems();
                bool windows = _inBar && _barItem < bar.Count && bar[_barItem] == _audioCard;
                VolumeRequested?.Invoke(windows, action == PadAction.VolumeUp ? 1 : -1);
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
