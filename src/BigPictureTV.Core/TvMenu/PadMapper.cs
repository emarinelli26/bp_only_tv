using BigPictureTV.Core.Input;

namespace BigPictureTV.Core.TvMenu;

/// <summary>What a controller press means in the TV menu and in the apps it opens.</summary>
public enum PadAction
{
    Up,
    Down,
    Left,
    Right,
    Accept,     // A
    Back,       // B
    Search,     // Y
    PlayPause,  // Start
    Previous,   // LB
    Next,       // RB
    PageUp,     // LT
    PageDown,   // RT
    Option,     // X: in the menu, closes an open app
}

/// <summary>
/// Turns controller states, read many times a second, into actions: one per
/// press, and directions repeat while held, like a keyboard. View (Back) is
/// left out: it opens the menu (see <see cref="Input.BindingDetector"/>). Not thread-safe;
/// feed it from one thread.
/// </summary>
public sealed class PadMapper
{
    public static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan RepeatEvery = TimeSpan.FromMilliseconds(110);

    static readonly (GamepadButtons Button, PadAction Action)[] Presses =
    {
        (GamepadButtons.A, PadAction.Accept),
        (GamepadButtons.B, PadAction.Back),
        (GamepadButtons.X, PadAction.Option),
        (GamepadButtons.Y, PadAction.Search),
        (GamepadButtons.Start, PadAction.PlayPause),
        (GamepadButtons.LB, PadAction.Previous),
        (GamepadButtons.RB, PadAction.Next),
        (GamepadButtons.LT, PadAction.PageUp),
        (GamepadButtons.RT, PadAction.PageDown),
    };

    static readonly (GamepadButtons Button, PadAction Action)[] Directions =
    {
        (GamepadButtons.Up, PadAction.Up),
        (GamepadButtons.Down, PadAction.Down),
        (GamepadButtons.Left, PadAction.Left),
        (GamepadButtons.Right, PadAction.Right),
    };

    GamepadButtons _previous;
    PadAction? _direction;
    DateTime _nextRepeat;

    /// <summary>Call with the buttons held now (stick pushes count as the cross). Returns what happened since the last call.</summary>
    public List<PadAction> Update(GamepadButtons pressed, DateTime now)
    {
        var actions = new List<PadAction>();
        var down = pressed & ~_previous;

        // While other buttons are held, a combo (like the TV switch) is being
        // pressed: don't act on its parts.
        bool chord = System.Numerics.BitOperations.PopCount((uint)(pressed & ~DirectionMask)) > 1;
        if (!chord)
            foreach (var (button, action) in Presses)
                if (down.HasFlag(button)) actions.Add(action);

        // One direction at a time; the first pressed wins until let go.
        PadAction? direction = null;
        foreach (var (button, action) in Directions)
            if (pressed.HasFlag(button)) { direction = action; break; }
        if (direction == null)
        {
            _direction = null;
        }
        else if (direction != _direction)
        {
            _direction = direction;
            _nextRepeat = now + RepeatDelay;
            actions.Add(direction.Value);
        }
        else if (now >= _nextRepeat)
        {
            _nextRepeat = now + RepeatEvery;
            actions.Add(direction.Value);
        }

        _previous = pressed;
        return actions;
    }

    const GamepadButtons DirectionMask = GamepadButtons.Up | GamepadButtons.Down | GamepadButtons.Left | GamepadButtons.Right;
}
