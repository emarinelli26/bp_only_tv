namespace BigPictureTV.Core.Input;

/// <summary>
/// Notices a quick press of one button on its own (like Share/View to open
/// the TV menu): fires when it is let go, if nothing else was pressed in the
/// meantime and it wasn't held long, so combos and holds that use the same
/// button never trigger it.
/// </summary>
public sealed class TapDetector
{
    public static readonly TimeSpan MaxTap = TimeSpan.FromMilliseconds(800);

    // The buttons that can open the menu, as stored in the settings.
    public static readonly GamepadButtons[] Choices =
        { GamepadButtons.Back, GamepadButtons.Start, GamepadButtons.LS, GamepadButtons.RS };

    DateTime? _since;
    bool _spoiled;

    /// <returns>True the moment a clean tap of <paramref name="button"/> ends.</returns>
    public bool Update(GamepadButtons button, GamepadButtons pressed, DateTime now)
    {
        if (button == GamepadButtons.None)
        {
            _since = null;
            return false;
        }
        bool held = pressed.HasFlag(button);
        if (held)
        {
            if (_since == null)
            {
                _since = now;
                _spoiled = pressed != button; // pressed together with something else
            }
            else if (pressed != button)
            {
                _spoiled = true;
            }
            return false;
        }
        if (_since == null) return false;
        bool tap = !_spoiled && now - _since.Value <= MaxTap;
        _since = null;
        return tap;
    }

    /// <summary>Reads "Back" (also View, Select or Share), "Start" (Menu, Options), "LS" (L3) or "RS" (R3). None when empty or unknown.</summary>
    public static GamepadButtons Parse(string? text) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        "back" or "view" or "select" or "share" => GamepadButtons.Back,
        "start" or "menu" or "options" => GamepadButtons.Start,
        "ls" or "l3" => GamepadButtons.LS,
        "rs" or "r3" => GamepadButtons.RS,
        _ => GamepadButtons.None,
    };
}
