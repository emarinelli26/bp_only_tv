namespace BigPictureTV.Core.Input;

/// <summary>Modifier keys, with the values RegisterHotKey expects.</summary>
[Flags]
public enum KeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// A key combination such as Ctrl+Alt+F12: modifiers plus a Windows
/// virtual-key code. Stored in the settings as text.
/// </summary>
public readonly record struct Hotkey(KeyModifiers Modifiers, uint Key)
{
    /// <summary>Switches between the TV and the desktop unless the user picks another.</summary>
    public static readonly Hotkey DefaultToggle = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x7B); // F12

    /// <summary>Always brings the desktop back. Fixed, so it can be documented and never forgotten.</summary>
    public static readonly Hotkey Emergency = new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift, 0x7B);

    // Keys that only make sense as part of a combination, never as the key itself.
    static readonly HashSet<uint> ModifierKeys = new()
    {
        0x10, 0x11, 0x12, // Shift, Ctrl, Alt
        0x5B, 0x5C,       // left/right Windows
        0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, // left/right Shift, Ctrl, Alt
    };

    static readonly Dictionary<uint, string> Names = BuildNames();

    static Dictionary<uint, string> BuildNames()
    {
        var names = new Dictionary<uint, string>
        {
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x1B] = "Esc",
            [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2C] = "PrintScreen", [0x2D] = "Insert", [0x2E] = "Delete",
            [0x6A] = "NumMultiply", [0x6B] = "NumPlus", [0x6D] = "NumMinus", [0x6E] = "NumDecimal", [0x6F] = "NumDivide",
            [0x91] = "ScrollLock",
        };
        for (uint i = 0; i < 10; i++)
        {
            names[0x30 + i] = i.ToString();
            names[0x60 + i] = $"Num{i}";
        }
        for (uint i = 0; i < 26; i++) names[0x41 + i] = ((char)('A' + i)).ToString();
        for (uint i = 0; i < 24; i++) names[0x70 + i] = $"F{i + 1}";
        return names;
    }

    /// <summary>
    /// A usable combination: a real key plus at least one modifier, except
    /// F13 to F24, which no keyboard types on its own.
    /// </summary>
    public bool IsValid =>
        Key != 0 && !ModifierKeys.Contains(Key) &&
        (Modifiers != KeyModifiers.None || Key is >= 0x7C and <= 0x87);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        parts.Add(Names.TryGetValue(Key, out var name) ? name : $"0x{Key:X2}");
        return string.Join("+", parts);
    }

    /// <summary>Reads "Ctrl+Alt+F12" (any case, spaces allowed). Null if it isn't a valid combination.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var mods = KeyModifiers.None;
        uint? key = null;
        foreach (var raw in text.Split('+'))
        {
            string part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= KeyModifiers.Ctrl; continue;
                case "alt": mods |= KeyModifiers.Alt; continue;
                case "shift": mods |= KeyModifiers.Shift; continue;
                case "win" or "windows": mods |= KeyModifiers.Win; continue;
            }
            if (key != null) return null; // two keys
            key = KeyFromName(part);
            if (key == null) return null;
        }
        if (key == null) return null;
        var hotkey = new Hotkey(mods, key.Value);
        return hotkey.IsValid ? hotkey : null;
    }

    static uint? KeyFromName(string name)
    {
        if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(name[2..], System.Globalization.NumberStyles.HexNumber, null, out uint code) &&
            code is > 0 and < 0x100)
            return code;
        foreach (var (vk, n) in Names)
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return vk;
        return null;
    }
}
