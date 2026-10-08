using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BigPictureTV.App;

/// <summary>What the TV menu session needs from the volume overlay, without touching WPF types.</summary>
interface ITvVolumeView
{
    /// <summary>Shows a volume for a moment. Null percent: <paramref name="what"/> is a message instead.</summary>
    void Show(string what, int? percent);
}

/// <summary>
/// A small volume bar at the top of the TV, like a console's, shown for a
/// moment while the right stick turns the volume. Never takes the focus.
/// </summary>
sealed class VolumeWindow : Window, ITvVolumeView
{
    const double BarWidth = 360;

    readonly TextBlock _what = new() { FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    readonly TextBlock _percent = new() { FontSize = 26, Foreground = Brushes.White, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly Border _fill = new() { Background = Brushes.White, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
    readonly FrameworkElement _bar;
    readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(1.6) };

    public VolumeWindow()
    {
        Title = "Volume";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI");

        _bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                new Border
                {
                    Width = BarWidth, Height = 10, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), Child = _fill,
                },
                _percent,
            },
        };
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x10, 0x13, 0x18)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(28, 18, 28, 20),
            Child = new StackPanel { Children = { _what, _bar } },
        };
        _hide.Tick += (_, _) => { _hide.Stop(); Hide(); };
        SizeChanged += (_, _) => Place();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NoActivate.Apply(new WindowInteropHelper(this).Handle);
    }

    public void Show(string what, int? percent)
    {
        _what.Text = what;
        _bar.Visibility = percent == null ? Visibility.Collapsed : Visibility.Visible;
        if (percent is { } p)
        {
            _fill.Width = BarWidth * Math.Clamp(p, 0, 100) / 100.0;
            _percent.Text = $"{p}%";
        }
        if (!IsVisible) base.Show();
        Place();
        _hide.Stop();
        _hide.Start();
    }

    // Top middle of the primary display (the TV while the menu is in use).
    void Place()
    {
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2);
        Top = 48;
    }
}
