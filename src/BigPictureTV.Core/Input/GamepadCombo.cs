namespace BigPictureTV.Core.Input;

/// <summary>Controller buttons, with XInput's bit values. LT and RT use two bits XInput leaves free.</summary>
[Flags]
public enum GamepadButtons : ushort
{
    None = 0,
    Up = 0x0001,
    Down = 0x0002,
    Left = 0x0004,
    Right = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LS = 0x0040,
    RS = 0x0080,
    LB = 0x0100,
    RB = 0x0200,
    LT = 0x0400,
    RT = 0x0800,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

/// <summary>Buttons held together on a controller, stored in the settings as text like "Back+Start+LB".</summary>
public static class GamepadCombo
{
    // Clicking both sticks: controllers use Back/Start/Home combos for their
    // own shortcuts (a GameSir Nova Lite switches modes with Back+Start+LB
    // and drops off the PC), but leave the sticks alone.
    public const GamepadButtons Default = GamepadButtons.LS | GamepadButtons.RS;

    /// <summary>How long the combo must be held, so a game using those buttons doesn't trigger it.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(1.5);

    // The order buttons are written in.
    static readonly GamepadButtons[] Order =
    {
        GamepadButtons.Back, GamepadButtons.Start, GamepadButtons.LB, GamepadButtons.RB,
        GamepadButtons.LT, GamepadButtons.RT, GamepadButtons.A, GamepadButtons.B,
        GamepadButtons.X, GamepadButtons.Y, GamepadButtons.Up, GamepadButtons.Down,
        GamepadButtons.Left, GamepadButtons.Right, GamepadButtons.LS, GamepadButtons.RS,
    };

    /// <summary>At least two buttons, so a single press in a game never triggers it.</summary>
    public static bool IsValid(GamepadButtons buttons) => System.Numerics.BitOperations.PopCount((uint)buttons) >= 2;

    public static string Format(GamepadButtons buttons) =>
        string.Join("+", Order.Where(b => buttons.HasFlag(b)));

    /// <summary>Reads "Back+Start+LB" (any case; View/Select and Menu also work). Null if not a valid combo.</summary>
    public static GamepadButtons? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var buttons = GamepadButtons.None;
        foreach (var raw in text.Split('+'))
        {
            string name = raw.Trim().ToLowerInvariant() switch
            {
                "select" or "view" => "Back",
                "menu" => "Start",
                var n => n,
            };
            if (!Enum.TryParse<GamepadButtons>(name, ignoreCase: true, out var b) || b == GamepadButtons.None ||
                !Order.Contains(b))
                return null;
            buttons |= b;
        }
        return IsValid(buttons) ? buttons : null;
    }
}

/// <summary>Fires once when a combo has been held long enough on one controller.</summary>
public sealed class ComboDetector
{
    DateTime? _since;
    bool _fired;

    /// <returns>True the moment the combo has been held for <see cref="GamepadCombo.Hold"/>.</returns>
    public bool Update(GamepadButtons combo, GamepadButtons pressed, DateTime now)
    {
        if (combo == GamepadButtons.None || (pressed & combo) != combo)
        {
            _since = null;
            _fired = false;
            return false;
        }
        _since ??= now;
        if (_fired || now - _since.Value < GamepadCombo.Hold) return false;
        _fired = true; // again only after letting go
        return true;
    }
}
