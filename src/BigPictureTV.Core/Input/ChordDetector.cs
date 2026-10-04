namespace BigPictureTV.Core.Input;

/// <summary>
/// Spots a key combination from raw key presses, whatever order the keys go
/// down in (F12 then Ctrl+Alt works too), and reports it once per press.
/// Not thread-safe: feed it from one thread.
/// </summary>
public sealed class ChordDetector
{
    readonly HashSet<uint> _down = new();
    readonly HashSet<Hotkey> _fired = new();

    /// <summary>The combinations to look for. Safe to replace from another thread.</summary>
    public IReadOnlyList<Hotkey> Hotkeys { get; set; } = Array.Empty<Hotkey>();

    /// <param name="vk">Virtual-key code of the key that went down.</param>
    /// <param name="isDown">
    /// Asks Windows whether a key is still held, to forget keys whose release
    /// we never saw (e.g. let go while the lock screen was up).
    /// </param>
    /// <returns>The combination this press completed, if any.</returns>
    public Hotkey? KeyDown(uint vk, Func<uint, bool>? isDown = null)
    {
        if (isDown != null) _down.RemoveWhere(k => k != vk && !isDown(k));
        if (!_down.Add(vk)) return null; // auto-repeat while held
        var mods = HeldModifiers();
        foreach (var hotkey in Hotkeys)
        {
            if (hotkey.Modifiers == mods && _down.Contains(hotkey.Key) && _fired.Add(hotkey))
                return hotkey;
        }
        return null;
    }

    public void KeyUp(uint vk)
    {
        _down.Remove(vk);
        // Once part of a combination is let go, it can fire again.
        _fired.RemoveWhere(h => !_down.Contains(h.Key) || (HeldModifiers() & h.Modifiers) != h.Modifiers);
    }

    KeyModifiers HeldModifiers()
    {
        var mods = KeyModifiers.None;
        foreach (uint k in _down) mods |= ModifierOf(k);
        return mods;
    }

    static KeyModifiers ModifierOf(uint vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => KeyModifiers.Shift,
        0x11 or 0xA2 or 0xA3 => KeyModifiers.Ctrl,
        0x12 or 0xA4 or 0xA5 => KeyModifiers.Alt,
        0x5B or 0x5C => KeyModifiers.Win,
        _ => KeyModifiers.None,
    };
}
