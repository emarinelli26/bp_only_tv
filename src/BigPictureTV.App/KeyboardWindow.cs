using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using BigPictureTV.Core.TvMenu;

namespace BigPictureTV.App;

/// <summary>What the TV menu session needs from the on-screen keyboard, without touching WPF types.</summary>
interface ITvKeyboardView
{
    /// <summary>Shows the keyboard (or redraws it if showing).</summary>
    void Show(OnScreenKeyboard keyboard);

    void Hide();
}

/// <summary>
/// The on-screen keyboard: a strip at the bottom of the TV, on top of the
/// page, that never takes the focus, so the keys it types land in the text
/// box of the window in front. The controller drives it (see TvSession).
/// </summary>
sealed class KeyboardWindow : Window, ITvKeyboardView
{
    static readonly Strings S = Strings.Current;
    const double Unit = 92, Gap = 8;

    static readonly Brush KeyBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x38));
    static readonly Brush SpecialBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x4B));
    static readonly Brush ChosenBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xFA));
    static readonly Brush OnBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x6C, 0xDF));

    readonly StackPanel _rows = new();

    public KeyboardWindow()
    {
        Title = "Keyboard";
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

        var panel = new StackPanel { Children = { _rows } };
        panel.Children.Add(new TextBlock
        {
            Text = S.KeyboardHints, FontSize = 22, Foreground = Brushes.White, Opacity = 0.75,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0),
        });
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x10, 0x13, 0x18)),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(28, 24, 28, 20),
            Child = panel,
        };
        SizeChanged += (_, _) => Place();
    }

    // Never active: the page keeps the focus and gets what is typed.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    public void Show(OnScreenKeyboard keyboard)
    {
        Draw(keyboard);
        if (!IsVisible) base.Show();
        Place();
    }

    void ITvKeyboardView.Hide() => Hide();

    // Bottom middle of the primary display, which is the TV while the menu is in use.
    void Place()
    {
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2);
        Top = Math.Max(0, SystemParameters.PrimaryScreenHeight - ActualHeight - 48);
    }

    void Draw(OnScreenKeyboard keyboard)
    {
        _rows.Children.Clear();
        for (int r = 0; r < keyboard.Rows.Count; r++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var keys = keyboard.Rows[r];
            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                bool chosen = r == keyboard.Row && i == keyboard.Index;
                bool on = key.Kind == KeyKind.Shift && keyboard.Capitals;
                row.Children.Add(new Border
                {
                    Width = key.Width * Unit + (key.Width - 1) * Gap,
                    Height = Unit * 0.8,
                    Margin = new Thickness(Gap / 2),
                    CornerRadius = new CornerRadius(10),
                    Background = chosen ? ChosenBrush : on ? OnBrush : key.Kind == KeyKind.Text ? KeyBrush : SpecialBrush,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(chosen ? 1.08 : 1, chosen ? 1.08 : 1),
                    Child = new TextBlock
                    {
                        Text = keyboard.LabelOf(key),
                        FontSize = key.Kind == KeyKind.Text ? 34 : 28,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = chosen ? Brushes.Black : Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                });
            }
            _rows.Children.Add(row);
        }
    }

    const int GWL_EXSTYLE = -20;
    const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;

    [DllImport("user32.dll")]
    static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
