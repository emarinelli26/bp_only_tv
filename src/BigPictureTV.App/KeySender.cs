using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BigPictureTV.Core.Input;

namespace BigPictureTV.App;

/// <summary>Types keys into whatever window is in front, as if pressed on a keyboard.</summary>
static class KeySender
{
    public const uint VK_BACK = 0x08, VK_RETURN = 0x0D, VK_LEFT = 0x25, VK_RIGHT = 0x27;
    public const uint VK_MEDIA_PLAY_PAUSE = 0xB3;

    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

    // Keys that live outside the old keypad and need the "extended" flag, or
    // Windows reads the arrows as number pad keys.
    static readonly HashSet<uint> Extended = new()
    {
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, // PageUp..Down, Insert, Delete
        0x5B, 0x5C, 0x6F, 0xA3, 0xA5, 0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3,
    };

    public static void Send(Hotkey keys)
    {
        var mods = new List<uint>();
        if (keys.Modifiers.HasFlag(KeyModifiers.Ctrl)) mods.Add(0x11);
        if (keys.Modifiers.HasFlag(KeyModifiers.Alt)) mods.Add(0x12);
        if (keys.Modifiers.HasFlag(KeyModifiers.Shift)) mods.Add(0x10);
        if (keys.Modifiers.HasFlag(KeyModifiers.Win)) mods.Add(0x5B);

        var inputs = new List<INPUT>();
        foreach (var m in mods) inputs.Add(Key(m, up: false));
        inputs.Add(Key(keys.Key, up: false));
        inputs.Add(Key(keys.Key, up: true));
        for (int i = mods.Count - 1; i >= 0; i--) inputs.Add(Key(mods[i], up: true));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    /// <summary>Types text as characters, whatever the keyboard layout (accents and ñ included).</summary>
    public static void Type(string text)
    {
        var inputs = new List<INPUT>();
        foreach (char c in text)
        {
            inputs.Add(Unicode(c, up: false));
            inputs.Add(Unicode(c, up: true));
        }
        if (inputs.Count > 0) SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    public static void Send(uint key) => Send(new Hotkey(KeyModifiers.None, key));

    /// <summary>Id of the process that owns the window in front, or 0.</summary>
    public static int ForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(window, out uint pid);
        return (int)pid;
    }

    /// <summary>Executable name (no .exe) of the process that owns the window in front, or "".</summary>
    public static string ForegroundProcessName()
    {
        int pid = ForegroundProcessId();
        if (pid == 0) return "";
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return "";
        }
    }

    /// <summary>
    /// Brings a window to the front even when the user last touched another
    /// program (a controller press is no keyboard input for Windows): joining
    /// the input of the window in front for a moment lifts that restriction.
    /// </summary>
    public static void ForceForeground(IntPtr window)
    {
        var front = GetForegroundWindow();
        if (front == window) return;
        uint ours = GetCurrentThreadId();
        uint theirs = front == IntPtr.Zero ? 0 : GetWindowThreadProcessId(front, out _);
        bool joined = theirs != 0 && theirs != ours && AttachThreadInput(ours, theirs, true);
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (joined) AttachThreadInput(ours, theirs, false);
        }
    }

    static INPUT Key(uint vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT
        {
            wVk = (ushort)vk,
            wScan = (ushort)MapVirtualKey(vk, 0),
            dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (Extended.Contains(vk) ? KEYEVENTF_EXTENDEDKEY : 0),
        },
    };

    static INPUT Unicode(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) },
    };

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // The union in INPUT is as large as its biggest member (MOUSEINPUT); pad to it.
    // Offset 8 is the 64-bit layout, the only one the app is built for.
    [StructLayout(LayoutKind.Explicit)]
    struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
        [FieldOffset(8)] public MOUSEINPUT padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    static extern bool AttachThreadInput(uint attach, uint attachTo, bool join);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();
}
