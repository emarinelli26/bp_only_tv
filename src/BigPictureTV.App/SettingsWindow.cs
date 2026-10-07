using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BigPictureTV.Core;
using BigPictureTV.Core.Audio;
using BigPictureTV.Core.Detection;
using BigPictureTV.Core.Display;
using BigPictureTV.Core.Input;

namespace BigPictureTV.App;

/// <summary>
/// Settings, also shown on first run as a welcome screen: which display is
/// the TV, what switching does, and a Test button to try it right away.
/// Works on a copy of the settings; the caller saves <see cref="Result"/>.
/// </summary>
public sealed class SettingsWindow : Window
{
    static readonly Strings S = Strings.Current;

    readonly AppSettings _draft;
    readonly IReadOnlyList<DisplayInfo> _displays;
    readonly Func<AppSettings, bool> _test;
    readonly Func<bool> _canTest;
    readonly Func<Hotkey, bool> _hotkeyAvailable;
    readonly Func<GamepadButtons?> _controllerButtons;

    readonly RadioButton _auto;
    readonly List<(RadioButton Button, DisplayInfo Display)> _tvChoices = new();
    readonly RadioButton _tvOnly, _tvPrimary, _duplicate;
    readonly TextBox _grace, _extra;
    readonly CheckBox _startup, _desktopWhenHidden, _rumble, _switchAudio, _shortcutOpensBp, _shortcutOpensMenu, _checkUpdates;
    readonly ComboBox _audioDevice;
    readonly Expander _advanced;
    readonly IReadOnlyList<AudioDevice> _audioOutputs;
    readonly TextBlock _testMessage;
    readonly TextBox _hotkeyBox;
    readonly TextBlock _hotkeyMessage;
    Hotkey? _hotkey;
    readonly TextBox _comboBox;
    readonly TextBlock _comboMessage;
    readonly System.Windows.Threading.DispatcherTimer _comboTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    GamepadButtons _combo;
    GamepadButtons _comboSeen;
    DateTime _comboSeenSince, _comboDeadline;

    public AppSettings Result => _draft;
    public bool StartWithWindows => _startup.IsChecked == true;

    /// <param name="test">Applies the given settings on the TV, waits for the user, and goes back. False if it couldn't switch.</param>
    /// <param name="canTest">False while the app itself is on the TV.</param>
    /// <param name="hotkeyAvailable">False if another program already uses that combination.</param>
    /// <param name="controllerButtons">Buttons held right now on any controller; null if none is connected.</param>
    public SettingsWindow(AppSettings settings, IReadOnlyList<DisplayInfo> displays, bool firstRun,
        bool startWithWindows, Func<AppSettings, bool> test, Func<bool> canTest, Func<Hotkey, bool> hotkeyAvailable,
        Func<GamepadButtons?> controllerButtons, IReadOnlyList<AudioDevice> audioOutputs)
    {
        _controllerButtons = controllerButtons;
        _audioOutputs = audioOutputs;
        _hotkeyAvailable = hotkeyAvailable;
        _draft = settings.Clone();
        _displays = displays;
        _test = test;
        _canTest = canTest;

        Title = S.SettingsTitle;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontSize = 14;

        var root = new StackPanel { Margin = new Thickness(20) };
        var advanced = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var guess = TvDetector.Guess(displays);

        if (firstRun)
        {
            root.Children.Add(Paragraph(S.Welcome));
            root.Children.Add(Paragraph(guess != null ? string.Format(S.WelcomeDetected, guess) : S.WelcomeNotDetected, bold: true));
        }

        // Which display is the TV.
        root.Children.Add(Heading(S.TvGroup));
        _auto = new RadioButton
        {
            Content = string.Format(S.DetectAutomaticallyWith, guess?.ToString() ?? S.DetectedNone),
            GroupName = "tv",
            Margin = new Thickness(0, 2, 0, 2),
        };
        root.Children.Add(_auto);

        bool automatic = _draft.TvDevicePath.Length == 0 && _draft.TvName.Length == 0;
        var chosen = automatic ? null : TvSelector.Select(displays, _draft).Tv;
        _auto.IsChecked = automatic || chosen == null;
        for (int i = 0; i < displays.Count; i++)
        {
            var d = displays[i];
            var rb = new RadioButton
            {
                Content = DisplayLabel(i + 1, d),
                GroupName = "tv",
                IsChecked = ReferenceEquals(d, chosen),
                Margin = new Thickness(0, 2, 0, 2),
            };
            _tvChoices.Add((rb, d));
            root.Children.Add(rb);
        }
        var identify = new Button { Content = S.Identify, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        identify.Click += (_, _) => IdentifyOverlay.Show(_displays);
        root.Children.Add(identify);

        // What switching does.
        root.Children.Add(Heading(S.LayoutGroup));
        _tvOnly = Choice(S.LayoutTvOnly, "layout", _draft.Layout == TvLayout.TvOnly);
        _tvPrimary = Choice(S.LayoutTvPrimary, "layout", _draft.Layout == TvLayout.TvPrimary);
        _duplicate = Choice(S.LayoutDuplicate, "layout", _draft.Layout == TvLayout.Duplicate);
        root.Children.Add(_tvOnly);
        root.Children.Add(_tvPrimary);
        root.Children.Add(_duplicate);
        _desktopWhenHidden = new CheckBox
        {
            Content = new TextBlock { Text = S.DesktopWhenHidden, TextWrapping = TextWrapping.Wrap },
            IsChecked = _draft.DesktopWhenBigPictureHidden,
            Margin = new Thickness(0, 8, 0, 0),
        };
        advanced.Children.Add(_desktopWhenHidden);

        // Keyboard shortcut.
        root.Children.Add(Heading(S.HotkeyGroup));
        _hotkey = Hotkey.Parse(_draft.Hotkey);
        var hotkeyRow = new StackPanel { Orientation = Orientation.Horizontal };
        _hotkeyBox = new TextBox { IsReadOnly = true, Width = 220, ToolTip = string.Format(S.HotkeyHint, Hotkey.Emergency) };
        _hotkeyBox.PreviewKeyDown += OnHotkeyKeyDown;
        hotkeyRow.Children.Add(_hotkeyBox);
        var noHotkey = Button(S.HotkeyNone);
        noHotkey.Click += (_, _) => { _hotkey = null; _hotkeyMessage!.Text = ""; ShowHotkey(); };
        hotkeyRow.Children.Add(noHotkey);
        root.Children.Add(hotkeyRow);
        _hotkeyMessage = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick };
        root.Children.Add(_hotkeyMessage);
        root.Children.Add(new TextBlock
        {
            Text = string.Format(S.HotkeyHint, Hotkey.Emergency),
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        });
        ShowHotkey();

        // Controller combo.
        advanced.Children.Add(Heading(S.ComboGroup));
        _combo = GamepadCombo.Parse(_draft.ControllerCombo) ?? GamepadButtons.None;
        var comboRow = new StackPanel { Orientation = Orientation.Horizontal };
        _comboBox = new TextBox { IsReadOnly = true, Width = 220 };
        comboRow.Children.Add(_comboBox);
        var record = Button(S.ComboRecord);
        record.Click += (_, _) => StartComboCapture();
        comboRow.Children.Add(record);
        var noCombo = Button(S.ComboNone);
        noCombo.Click += (_, _) => { StopComboCapture(); _combo = GamepadButtons.None; _comboMessage!.Text = ""; ShowCombo(); };
        comboRow.Children.Add(noCombo);
        advanced.Children.Add(comboRow);
        _comboMessage = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick };
        advanced.Children.Add(_comboMessage);
        advanced.Children.Add(new TextBlock
        {
            Text = string.Format(S.ComboHint, GamepadCombo.Hold.TotalSeconds),
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        });
        _rumble = new CheckBox { Content = S.ComboRumble, IsChecked = _draft.ControllerRumble, Margin = new Thickness(0, 6, 0, 0) };
        advanced.Children.Add(_rumble);
        _comboTimer.Tick += (_, _) => ComboCaptureTick();
        Closed += (_, _) => _comboTimer.Stop();
        ShowCombo();

        // The rest of the advanced options.
        advanced.Children.Add(Heading(S.AudioGroup));
        _switchAudio = new CheckBox
        {
            Content = new TextBlock { Text = S.SwitchAudio, TextWrapping = TextWrapping.Wrap },
            IsChecked = _draft.SwitchAudio,
        };
        advanced.Children.Add(_switchAudio);
        _audioDevice = new ComboBox { Margin = new Thickness(20, 4, 0, 0), IsEnabled = _draft.SwitchAudio };
        var tvName = TvSelector.Select(displays, _draft).Tv?.Name;
        var autoAudio = AudioPicker.Pick(_audioOutputs, "", tvName);
        _audioDevice.Items.Add(string.Format(S.AudioAutomatic, autoAudio?.Name ?? S.DetectedNone));
        foreach (var output in _audioOutputs) _audioDevice.Items.Add(output);
        var pickedAudio = _audioOutputs.FirstOrDefault(o => o.Id == _draft.AudioDeviceId);
        _audioDevice.SelectedItem = (object?)pickedAudio ?? _audioDevice.Items[0];
        _switchAudio.Checked += (_, _) => _audioDevice.IsEnabled = true;
        _switchAudio.Unchecked += (_, _) => _audioDevice.IsEnabled = false;
        advanced.Children.Add(_audioDevice);

        advanced.Children.Add(Heading(S.TvMenuGroup));
        _shortcutOpensMenu = new CheckBox
        {
            Content = new TextBlock { Text = S.ShortcutOpensTvMenu, TextWrapping = TextWrapping.Wrap },
            IsChecked = _draft.ShortcutOpensTvMenu,
        };
        advanced.Children.Add(_shortcutOpensMenu);

        advanced.Children.Add(Heading(S.OpenBigPictureGroup));
        _shortcutOpensBp = new CheckBox
        {
            Content = new TextBlock { Text = S.ShortcutOpensBigPicture, TextWrapping = TextWrapping.Wrap },
            IsChecked = _draft.ShortcutOpensBigPicture,
        };
        advanced.Children.Add(_shortcutOpensBp);

        advanced.Children.Add(Heading(S.OtherGroup));
        var graceRow = new StackPanel { Orientation = Orientation.Horizontal };
        graceRow.Children.Add(new TextBlock { Text = S.GraceLabel, VerticalAlignment = VerticalAlignment.Center });
        _grace = new TextBox { Text = _draft.GraceSeconds.ToString(), Width = 50, Margin = new Thickness(8, 0, 0, 0) };
        graceRow.Children.Add(_grace);
        advanced.Children.Add(graceRow);

        advanced.Children.Add(new TextBlock { Text = S.ExtraLabel, Margin = new Thickness(0, 10, 0, 2), TextWrapping = TextWrapping.Wrap });
        _extra = new TextBox { Text = string.Join(", ", _draft.ExtraProcesses), ToolTip = S.ExtraHint };
        advanced.Children.Add(_extra);
        advanced.Children.Add(new TextBlock { Text = S.ExtraHint, FontSize = 12, Opacity = 0.7 });

        _startup = new CheckBox { Content = S.StartWithWindows, IsChecked = startWithWindows, Margin = new Thickness(0, 12, 0, 0) };
        root.Children.Add(_startup);

        _checkUpdates = new CheckBox
        {
            Content = new TextBlock { Text = string.Format(S.CheckForUpdates, UpdateChecker.Current.ToString(3)), TextWrapping = TextWrapping.Wrap },
            IsChecked = _draft.CheckForUpdates,
            Margin = new Thickness(0, 10, 0, 0),
        };
        advanced.Children.Add(_checkUpdates);
        root.Children.Add(_advanced = new Expander
        {
            Header = new TextBlock { Text = S.AdvancedGroup, FontWeight = FontWeights.SemiBold, FontSize = 15 },
            Content = advanced,
            Margin = new Thickness(0, 16, 0, 0),
        });

        // Buttons.
        _testMessage = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = System.Windows.Media.Brushes.Firebrick };
        root.Children.Add(_testMessage);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var testButton = Button(S.Test);
        testButton.Click += (_, _) => RunTest();
        var save = Button(S.Save);
        save.IsDefault = true;
        save.Click += (_, _) => { if (Collect()) DialogResult = true; };
        var cancel = Button(S.Cancel);
        cancel.IsCancel = true;
        buttons.Children.Add(testButton);
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 800 };
    }

    void ShowHotkey() => _hotkeyBox.Text = _hotkey?.ToString() ?? S.HotkeyOff;

    // The box records whatever combination is pressed while it has focus.
    void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = (KeyModifiers)(uint)Keyboard.Modifiers; // same bit values as RegisterHotKey
        if (key == Key.Tab && mods == KeyModifiers.None) return; // keep Tab for moving around the window
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift
            or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return; // wait for the actual key

        var pressed = new Hotkey(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!pressed.IsValid)
            _hotkeyMessage.Text = S.HotkeyNeedsModifier;
        else if (pressed == Hotkey.Emergency)
            _hotkeyMessage.Text = string.Format(S.HotkeyIsEmergency, Hotkey.Emergency);
        else if (!_hotkeyAvailable(pressed))
            _hotkeyMessage.Text = string.Format(S.HotkeyTaken, pressed);
        else
        {
            _hotkeyMessage.Text = "";
            _hotkey = pressed;
            ShowHotkey();
        }
    }

    void ShowCombo() => _comboBox.Text = _combo == GamepadButtons.None ? S.HotkeyOff : GamepadCombo.Format(_combo);

    // Recording: wait until the same buttons (two or more) stay held for a second.
    void StartComboCapture()
    {
        if (_controllerButtons() == null)
        {
            _comboMessage.Text = S.ComboNoController;
            return;
        }
        _comboMessage.Text = "";
        _comboBox.Text = S.ComboRecording;
        _comboSeen = GamepadButtons.None;
        _comboSeenSince = DateTime.UtcNow;
        _comboDeadline = DateTime.UtcNow.AddSeconds(10);
        _comboTimer.Start();
    }

    void StopComboCapture()
    {
        _comboTimer.Stop();
        ShowCombo();
    }

    void ComboCaptureTick()
    {
        var now = DateTime.UtcNow;
        var pressed = _controllerButtons();
        if (pressed == null)
        {
            StopComboCapture();
            _comboMessage.Text = S.ComboNoController;
            return;
        }
        if (pressed.Value != _comboSeen)
        {
            _comboSeen = pressed.Value;
            _comboSeenSince = now;
        }
        else if (GamepadCombo.IsValid(_comboSeen) && now - _comboSeenSince >= TimeSpan.FromSeconds(1))
        {
            _combo = _comboSeen;
            StopComboCapture();
            return;
        }
        if (now > _comboDeadline)
        {
            StopComboCapture();
            _comboMessage.Text = S.ComboTimeout;
        }
    }

    void RunTest()
    {
        _testMessage.Text = "";
        if (!_canTest())
        {
            _testMessage.Text = S.TestBusy;
            return;
        }
        if (!Collect()) return;
        if (!_test(_draft)) _testMessage.Text = S.TestFailed;
    }

    /// <summary>Copies the form into the draft. False (and a message) if something is invalid.</summary>
    bool Collect()
    {
        var picked = _tvChoices.FirstOrDefault(c => c.Button.IsChecked == true).Display;
        _draft.TvDevicePath = picked?.DevicePath ?? "";
        _draft.TvName = picked?.Name ?? "";

        _draft.Layout = _tvPrimary.IsChecked == true ? TvLayout.TvPrimary
            : _duplicate.IsChecked == true ? TvLayout.Duplicate
            : TvLayout.TvOnly;

        if (!int.TryParse(_grace.Text.Trim(), out int grace) || grace < 0 || grace > 600)
        {
            _advanced.IsExpanded = true;
            _grace.Focus();
            _grace.SelectAll();
            return false;
        }
        _draft.GraceSeconds = grace;
        _draft.Hotkey = _hotkey?.ToString() ?? "";
        _draft.ControllerRumble = _rumble.IsChecked == true;
        _draft.SwitchAudio = _switchAudio.IsChecked == true;
        _draft.AudioDeviceId = (_audioDevice.SelectedItem as AudioDevice)?.Id ?? "";
        _draft.ShortcutOpensBigPicture = _shortcutOpensBp.IsChecked == true;
        _draft.ShortcutOpensTvMenu = _shortcutOpensMenu.IsChecked == true;
        _draft.CheckForUpdates = _checkUpdates.IsChecked == true;
        _draft.ControllerCombo = _combo == GamepadButtons.None ? "" : GamepadCombo.Format(_combo);
        _draft.DesktopWhenBigPictureHidden = _desktopWhenHidden.IsChecked == true;
        _draft.ExtraProcesses = BigPictureWatcher.NormalizeProcessNames(new[] { _extra.Text }).ToList();
        _draft.FirstRunDone = true;
        return true;
    }

    static string DisplayLabel(int number, DisplayInfo d)
    {
        var parts = new List<string>();
        if (d.Width > 0) parts.Add($"{d.Width}x{d.Height}");
        if (d.Connection == OutputTechnology.Hdmi) parts.Add("HDMI");
        else if (d.Connection is OutputTechnology.DisplayPortExternal or OutputTechnology.DisplayPortUsbTunnel) parts.Add("DisplayPort");
        if (!d.Active) parts.Add(S.InactiveInWindows);
        string details = parts.Count > 0 ? $"  ({string.Join(", ", parts)})" : "";
        return $"{number}. {d}{details}";
    }

    static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        FontSize = 15,
        Margin = new Thickness(0, 16, 0, 4),
    };

    static TextBlock Paragraph(string text, bool bold = false) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Margin = new Thickness(0, 0, 0, 8),
    };

    static RadioButton Choice(string text, string group, bool isChecked) => new()
    {
        Content = text,
        GroupName = group,
        IsChecked = isChecked,
        Margin = new Thickness(0, 2, 0, 2),
    };

    static Button Button(string text) => new()
    {
        Content = text,
        MinWidth = 90,
        Padding = new Thickness(10, 4, 10, 4),
        Margin = new Thickness(8, 0, 0, 0),
    };
}
