namespace BigPictureTV.Core.Input;

/// <summary>
/// Notices a quick press of exactly some buttons (one, like Share to open
/// the TV menu, or several together): fires when they are all let go, if
/// nothing else was pressed in the meantime and it wasn't held long, so
/// combos and holds that share a button never trigger it.
/// </summary>
public sealed class TapDetector
{
    public static readonly TimeSpan MaxTap = TimeSpan.FromMilliseconds(800);

    DateTime? _since;
    GamepadButtons _seen;

    /// <returns>True the moment a clean tap of <paramref name="buttons"/> ends.</returns>
    public bool Update(GamepadButtons buttons, GamepadButtons pressed, DateTime now)
    {
        if (pressed != GamepadButtons.None)
        {
            _since ??= now;
            _seen |= pressed;
            return false;
        }
        if (_since == null) return false;
        bool tap = buttons != GamepadButtons.None && _seen == buttons && now - _since.Value <= MaxTap;
        _since = null;
        _seen = GamepadButtons.None;
        return tap;
    }
}

/// <summary>
/// A controller shortcut from the settings: some buttons and how long to
/// hold them. Zero means a quick tap (see <see cref="TapDetector"/>).
/// </summary>
public sealed class BindingDetector
{
    public const double MaxHoldSeconds = 5;

    readonly TapDetector _tap = new();
    readonly ComboDetector _hold = new();

    public bool Update(GamepadButtons buttons, double holdSeconds, GamepadButtons pressed, DateTime now)
    {
        bool tapped = _tap.Update(buttons, pressed, now);
        bool held = _hold.Update(buttons, pressed, now, TimeSpan.FromSeconds(Math.Clamp(holdSeconds, 0.1, MaxHoldSeconds)));
        if (buttons == GamepadButtons.None) return false;
        return holdSeconds <= 0 ? tapped : held;
    }
}
