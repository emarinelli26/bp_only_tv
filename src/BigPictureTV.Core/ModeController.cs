using BigPictureTV.Core.Display;

namespace BigPictureTV.Core;

public enum DisplayMode
{
    /// <summary>Normal desktop layout.</summary>
    Desktop,

    /// <summary>On the TV because Big Picture (or an extra process) is open; goes back on its own.</summary>
    TvAuto,

    /// <summary>On the TV because the user asked for it; stays until the user toggles back.</summary>
    TvManual,
}

/// <summary>
/// The single place that decides when to switch. Every input (the Big Picture
/// check, and later the hotkey, controller and tray menu) goes through here,
/// so they can't disagree. Thread-safe.
/// </summary>
public sealed class ModeController
{
    readonly IDisplaySwitcher _switcher;
    readonly ILog _log;
    readonly object _gate = new();

    DateTime? _closedSince;

    // Consecutive checks that found the displays no longer TV-only.
    int _mismatches;

    // Set after a switch attempt (failed, or undone by the user) while Big
    // Picture stays open, so we don't retry on every check. Cleared when it closes.
    bool _suppressed;

    public ModeController(IDisplaySwitcher switcher, ILog log)
    {
        _switcher = switcher;
        _log = log;
    }

    public DisplayMode Mode { get; private set; } = DisplayMode.Desktop;

    /// <summary>While paused, Big Picture opening does nothing. Manual toggles still work.</summary>
    public bool Paused { get; private set; }

    public TimeSpan Grace { get; set; } = TimeSpan.FromSeconds(5);

    public event Action<DisplayMode>? ModeChanged;

    /// <summary>Raised when something outside the app (Windows, Win+P, the TV) brought the other displays back.</summary>
    public event Action? LayoutChangedOutside;

    /// <summary>How many checks in a row must disagree before we believe it (ignores a momentary glitch).</summary>
    public int MismatchesToConfirm { get; set; } = 2;

    /// <summary>Call once at startup: picks up where a previous run left off.</summary>
    public void Start(bool bigPictureOpen)
    {
        lock (_gate)
        {
            if (!_switcher.HasSavedLayout) return;
            if (bigPictureOpen)
            {
                SetMode(DisplayMode.TvAuto);
            }
            else
            {
                // The last run ended while on the TV (crash, sign-out): put things back.
                _log.Write("Found a leftover saved layout from a previous run.");
                _switcher.RestoreDesktop();
            }
        }
    }

    /// <summary>Call regularly with whether Big Picture is open.</summary>
    public void Tick(bool bigPictureOpen, DateTime now)
    {
        lock (_gate)
        {
            switch (Mode)
            {
                case DisplayMode.Desktop:
                    if (!bigPictureOpen) { _suppressed = false; break; }
                    if (Paused || _suppressed) break;
                    _log.Write("Big Picture opened.");
                    _suppressed = true;
                    if (_switcher.SwitchToTv()) SetMode(DisplayMode.TvAuto);
                    break;

                case DisplayMode.TvAuto:
                    if (LayoutLeftTv(bigPictureOpen, MismatchesToConfirm)) break;
                    if (bigPictureOpen) { _closedSince = null; break; }
                    _closedSince ??= now;
                    if (now - _closedSince.Value >= Grace)
                    {
                        _log.Write("Big Picture closed.");
                        GoToDesktop();
                        _suppressed = false;
                    }
                    break;

                case DisplayMode.TvManual:
                    LayoutLeftTv(bigPictureOpen, MismatchesToConfirm);
                    break; // otherwise only the user ends manual mode
            }
        }
    }

    /// <summary>
    /// Checks right away that the displays still match the mode, e.g. before
    /// showing the menu, so it never offers "back to the desktop" when the
    /// desktop is already back.
    /// </summary>
    public void SyncWithDisplays(bool bigPictureOpen)
    {
        lock (_gate)
        {
            if (Mode != DisplayMode.Desktop) LayoutLeftTv(bigPictureOpen, required: 1);
        }
    }

    /// <summary>Hotkey / controller / menu: TV on if off, off if on.</summary>
    public void Toggle(bool bigPictureOpen)
    {
        lock (_gate)
        {
            // Already back on the desktop (changed outside the app): that is what the user wanted.
            if (Mode != DisplayMode.Desktop && LayoutLeftTv(bigPictureOpen, required: 1)) return;
            if (Mode == DisplayMode.Desktop)
            {
                if (_switcher.SwitchToTv()) SetMode(DisplayMode.TvManual);
            }
            else
            {
                GoToDesktop();
                // The user turned it off on purpose: don't switch back until Big Picture closes and reopens.
                _suppressed = bigPictureOpen;
            }
        }
    }

    /// <summary>Emergency restore: always goes back to the desktop, whatever the state.</summary>
    public void RestoreNow(bool bigPictureOpen)
    {
        lock (_gate)
        {
            if (Mode != DisplayMode.Desktop || _switcher.HasSavedLayout) GoToDesktop();
            _suppressed = bigPictureOpen;
        }
    }

    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            if (Paused == paused) return;
            Paused = paused;
            _log.Write(paused ? "Automatic switching paused." : "Automatic switching resumed.");
        }
    }

    /// <summary>On exit: never leave the user stuck on the TV.</summary>
    public void Shutdown()
    {
        lock (_gate)
        {
            if (Mode != DisplayMode.Desktop || _switcher.HasSavedLayout) GoToDesktop();
        }
    }

    // While on the TV, Windows can bring the other displays back on its own:
    // the TV went to standby or switched input, the driver reset, someone
    // pressed Win+P. Follow what the displays really show instead of fighting
    // it, and wait for Big Picture to reopen (or a toggle) to switch again.
    bool LayoutLeftTv(bool bigPictureOpen, int required)
    {
        if (_switcher.IsTvOnly())
        {
            _mismatches = 0;
            return false;
        }
        if (++_mismatches < required) return false;

        _log.Write("The displays were changed outside BigPictureTV; back in desktop mode.");
        _mismatches = 0;
        _switcher.ForgetSavedLayout();
        _closedSince = null;
        _suppressed = bigPictureOpen;
        SetMode(DisplayMode.Desktop);
        LayoutChangedOutside?.Invoke();
        return true;
    }

    void GoToDesktop()
    {
        _switcher.RestoreDesktop();
        _closedSince = null;
        SetMode(DisplayMode.Desktop);
    }

    void SetMode(DisplayMode mode)
    {
        _mismatches = 0;
        if (Mode == mode) return;
        Mode = mode;
        ModeChanged?.Invoke(mode);
    }
}
