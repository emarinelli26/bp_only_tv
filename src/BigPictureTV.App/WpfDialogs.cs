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

    /// <summary>
    /// Shows the TV menu and waits. <paramref name="opened"/> gets a way to
    /// feed it controller actions and to close it, both safe from any thread.
    /// </summary>
    public static (TvApp? Chosen, int Selected) ShowTvMenu(IReadOnlyList<TvApp> apps, int selected, string? message,
        Action<Action<PadAction>, Action> opened)
    {
        var window = new TvMenuWindow(apps, selected, message);
        opened(window.Handle, () => window.Dispatcher.BeginInvoke(window.Close));
        window.ShowDialog();
        return (window.Chosen, window.Selected);
    }
}
