using System;
using System.Collections.Generic;
using BigPictureTV.Core;
using BigPictureTV.Core.Audio;
using BigPictureTV.Core.Display;
using BigPictureTV.Core.Input;
using BigPictureTV.Core.TvMenu;

namespace BigPictureTV.App;

/// <summary>
/// The only place the tray app touches WPF. WPF is large, so it loads the
/// first time a window opens instead of at startup, which keeps the app's
/// memory low while it just sits next to the clock.
/// </summary>
static class WpfDialogs
{
    static SettingsWindow? _settings;

    public static bool SettingsOpen => _settings != null;

    public static void ActivateSettings() => _settings?.Activate();

    /// <summary>Shows the settings window and waits. Null if the user cancelled.</summary>
    public static (AppSettings Settings, bool StartWithWindows)? ShowSettings(AppSettings settings,
        IReadOnlyList<DisplayInfo> displays, bool firstRun, bool startWithWindows,
        Func<AppSettings, bool> test, Func<bool> canTest, Func<Hotkey, bool> hotkeyAvailable,
        Func<GamepadButtons?> controllerButtons, IReadOnlyList<AudioDevice> audioOutputs)
    {
        _settings = new SettingsWindow(settings, displays, firstRun, startWithWindows, test, canTest, hotkeyAvailable,
            controllerButtons, audioOutputs);
        try
        {
            return _settings.ShowDialog() == true ? (_settings.Result, _settings.StartWithWindows) : null;
        }
        finally
        {
            _settings = null;
        }
    }

    public static void ShowTestCountdown() => new TestDialog().ShowDialog();

    /// <summary>The TV menu, made once and kept (hidden) for the rest of the run.</summary>
    public static ITvMenuView CreateTvMenu() => new TvMenuWindow();

    /// <summary>The on-screen keyboard, made once and kept (hidden) like the menu.</summary>
    public static ITvKeyboardView CreateKeyboard() => new KeyboardWindow();
}
