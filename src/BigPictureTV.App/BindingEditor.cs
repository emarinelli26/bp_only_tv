using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BigPictureTV.Core.Input;

namespace BigPictureTV.App;

/// <summary>
/// Records controller buttons for a shortcut (one or several pressed
/// together) and how long to hold them, from a quick tap up to 5 seconds.
/// </summary>
sealed class BindingEditor : StackPanel
{
    static readonly Strings S = Strings.Current;
    static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(10);

    readonly Func<GamepadButtons?> _controllerButtons;
    readonly TextBox _box = new() { IsReadOnly = true, Width = 220 };
    readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick };
    readonly Slider _hold = new()
    {
        Minimum = 0, Maximum = BindingDetector.MaxHoldSeconds, TickFrequency = 0.5, IsSnapToTickEnabled = true,
        TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight, Width = 220,
    };
    readonly TextBlock _holdText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    GamepadButtons _seen;
    DateTime _deadline;

    public GamepadButtons Buttons { get; private set; }
    public double HoldSeconds => _hold.Value;
    public bool Recording => _timer.IsEnabled;

    public BindingEditor(GamepadButtons buttons, double holdSeconds, Func<GamepadButtons?> controllerButtons)
    {
        Buttons = buttons;
        _controllerButtons = controllerButtons;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_box);
        var record = MakeButton(S.ComboRecord);
        record.Click += (_, _) => Start();
        row.Children.Add(record);
        var none = MakeButton(S.ComboNone);
        none.Click += (_, _) => { Stop(); Buttons = GamepadButtons.None; _message.Text = ""; Show(); };
        row.Children.Add(none);
        Children.Add(row);
        Children.Add(_message);

        _hold.Value = Math.Clamp(Math.Round(holdSeconds * 2) / 2, 0, BindingDetector.MaxHoldSeconds);
        _hold.ValueChanged += (_, _) => ShowHold();
        var holdRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        holdRow.Children.Add(_hold);
        holdRow.Children.Add(_holdText);
        Children.Add(holdRow);

        _timer.Tick += (_, _) => Tick();
        Unloaded += (_, _) => _timer.Stop();
        Show();
        ShowHold();
    }

    static Button MakeButton(string text) => new()
    {
        Content = text, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0),
    };

    void Show() => _box.Text = Buttons == GamepadButtons.None ? S.HotkeyOff : TrayApp.ButtonsName(Buttons);

    void ShowHold() => _holdText.Text = _hold.Value <= 0 ? S.HoldTap : string.Format(S.HoldLabel, _hold.Value);

    void Start()
    {
        if (_controllerButtons() == null)
        {
            _message.Text = S.ComboNoController;
            return;
        }
        _message.Text = "";
        _box.Text = S.ComboRecording;
        _seen = GamepadButtons.None;
        _deadline = DateTime.UtcNow + GiveUpAfter;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Show();
    }

    // Everything pressed together counts; it's recorded when all are let go.
    void Tick()
    {
        var pressed = _controllerButtons();
        if (pressed == null)
        {
            Stop();
            _message.Text = S.ComboNoController;
            return;
        }
        if (pressed.Value != GamepadButtons.None)
        {
            _seen |= pressed.Value;
            _box.Text = TrayApp.ButtonsName(_seen);
        }
        else if (_seen != GamepadButtons.None)
        {
            Buttons = _seen;
            Stop();
            return;
        }
        if (DateTime.UtcNow > _deadline)
        {
            Stop();
            _message.Text = S.ComboTimeout;
        }
    }
}
