using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BigPictureTV.App;

/// <summary>
/// Shown on the TV while testing a setting. Goes back by itself after a
/// countdown, so a TV that shows nothing never leaves the user stuck.
/// </summary>
public sealed class TestDialog : Window
{
    static readonly Strings S = Strings.Current;
    int _seconds = 15;

    public TestDialog()
    {
        Title = "BigPictureTV";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; // the primary display, i.e. the TV
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        FontSize = 18;

        var countdown = new TextBlock { Margin = new Thickness(0, 8, 0, 16) };
        var back = new Button { Content = S.TestBack, Padding = new Thickness(16, 6, 16, 6), IsDefault = true, IsCancel = true };
        back.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children = { new TextBlock { Text = S.TestDialog, FontWeight = FontWeights.SemiBold }, countdown, back },
        };

        void Update() => countdown.Text = string.Format(S.TestSeconds, _seconds);
        Update();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (--_seconds <= 0) Close();
            else Update();
        };
        Closed += (_, _) => timer.Stop();
        timer.Start();
    }
}
