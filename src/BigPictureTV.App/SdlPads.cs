using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BigPictureTV.Core;
using BigPictureTV.Core.Input;

namespace BigPictureTV.App;

/// <summary>
/// PlayStation (DualShock 4, DualSense), Switch and other HID controllers
/// that Windows' XInput doesn't see, through SDL's HIDAPI drivers (SDL is the
/// library most PC games use for controllers). Its other Windows back ends
/// (XInput, DirectInput, raw input, Windows.Gaming.Input) stay off: XInput
/// is read directly, and fewer moving parts in a program that runs all day.
/// All calls on one thread.
/// </summary>
sealed class SdlPads : IDisposable
{
    const uint SDL_INIT_GAMECONTROLLER = 0x2000;
    const short TriggerThreshold = 16384; // half way; SDL triggers go 0..32767
    const short StickThreshold = 16000;

    readonly ILog _log;
    readonly Dictionary<int, IntPtr> _open = new();

    SdlPads(ILog log) => _log = log;

    /// <summary>Null if SDL can't start here; the caller falls back to XInput.</summary>
    public static SdlPads? TryStart(ILog log)
    {
        try
        {
            // Keep reading while another program (Big Picture, the browser) is in front.
            SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            foreach (var off in new[]
            {
                "SDL_XINPUT_ENABLED", "SDL_DIRECTINPUT_ENABLED", "SDL_JOYSTICK_RAWINPUT", "SDL_JOYSTICK_WGI",
                "SDL_JOYSTICK_HIDAPI_XBOX", "SDL_JOYSTICK_HIDAPI_STEAM", "SDL_JOYSTICK_HIDAPI_STEAMDECK",
            })
                SDL_SetHint(off, "0");
            if (SDL_Init(SDL_INIT_GAMECONTROLLER) != 0)
            {
                log.Write($"SDL didn't start ({Utf8(SDL_GetError())}); using XInput for controllers.");
                return null;
            }
            return new SdlPads(log);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            log.Write($"SDL isn't available ({e.Message}); using XInput for controllers.");
            return null;
        }
    }

    /// <summary>Reads every connected controller; looks for new ones when <paramref name="probe"/> is set.</summary>
    public List<(int Id, GamepadButtons Buttons, GamepadButtons Stick)> Read(bool probe)
    {
        SDL_PumpEvents();
        SDL_FlushEvents(0, 0xFFFF); // nobody reads the queue; keep it empty

        var gone = new List<int>();
        foreach (var (id, pad) in _open)
            if (SDL_GameControllerGetAttached(pad) == 0) gone.Add(id);
        foreach (var id in gone)
        {
            SDL_GameControllerClose(_open[id]);
            _open.Remove(id);
            _log.Write($"Controller {SdlId(id)} disconnected.");
        }

        if (probe)
        {
            int count = SDL_NumJoysticks();
            for (int i = 0; i < count; i++)
            {
                int id = SDL_JoystickGetDeviceInstanceID(i);
                if (id < 0 || _open.ContainsKey(id) || SDL_IsGameController(i) == 0) continue;
                var pad = SDL_GameControllerOpen(i);
                if (pad == IntPtr.Zero) continue;
                _open[id] = pad;
                _log.Write($"Controller {SdlId(id)} connected: {Utf8(SDL_GameControllerName(pad))}.");
            }
        }

        var result = new List<(int, GamepadButtons, GamepadButtons)>(_open.Count);
        foreach (var (id, pad) in _open) result.Add((id, Buttons(pad), Stick(pad)));
        return result;
    }

    // SDL's button numbers, mapped to the XInput names the rest of the app uses.
    static readonly (int Sdl, GamepadButtons Button)[] Map =
    {
        (0, GamepadButtons.A), (1, GamepadButtons.B), (2, GamepadButtons.X), (3, GamepadButtons.Y),
        (4, GamepadButtons.Back), (6, GamepadButtons.Start), (7, GamepadButtons.LS), (8, GamepadButtons.RS),
        (9, GamepadButtons.LB), (10, GamepadButtons.RB), (11, GamepadButtons.Up), (12, GamepadButtons.Down),
        (13, GamepadButtons.Left), (14, GamepadButtons.Right),
        (20, GamepadButtons.Back), // PlayStation touchpad click, like Share/View
    };

    static GamepadButtons Buttons(IntPtr pad)
    {
        var buttons = GamepadButtons.None;
        foreach (var (sdl, button) in Map)
            if (SDL_GameControllerGetButton(pad, sdl) != 0) buttons |= button;
        if (SDL_GameControllerGetAxis(pad, 4) >= TriggerThreshold) buttons |= GamepadButtons.LT;
        if (SDL_GameControllerGetAxis(pad, 5) >= TriggerThreshold) buttons |= GamepadButtons.RT;
        return buttons;
    }

    static GamepadButtons Stick(IntPtr pad)
    {
        int rx = SDL_GameControllerGetAxis(pad, 2), ry = SDL_GameControllerGetAxis(pad, 3);
        var right = Math.Abs(ry) <= Math.Abs(rx) ? GamepadButtons.None
            : ry >= StickThreshold ? GamepadButtons.RStickDown : ry <= -StickThreshold ? GamepadButtons.RStickUp : GamepadButtons.None;
        int x = SDL_GameControllerGetAxis(pad, 0), y = SDL_GameControllerGetAxis(pad, 1); // SDL: down is positive
        if (Math.Abs(x) >= Math.Abs(y))
            return right | (x >= StickThreshold ? GamepadButtons.Right : x <= -StickThreshold ? GamepadButtons.Left : GamepadButtons.None);
        return right | (y >= StickThreshold ? GamepadButtons.Down : y <= -StickThreshold ? GamepadButtons.Up : GamepadButtons.None);
    }

    /// <summary>
    /// Starts SDL's controller support over, so it lists the controllers
    /// again. Its own notice of a newly plugged or paired controller doesn't
    /// always come (seen with one connected after the app started). False
    /// if SDL won't start again; then stop using it.
    /// </summary>
    public bool Restart()
    {
        foreach (var pad in _open.Values) SDL_GameControllerClose(pad);
        _open.Clear();
        SDL_QuitSubSystem(SDL_INIT_GAMECONTROLLER);
        if (SDL_Init(SDL_INIT_GAMECONTROLLER) == 0) return true;
        _log.Write($"SDL didn't start again ({Utf8(SDL_GetError())}); using XInput for controllers.");
        return false;
    }

    public void BuzzAll()
    {
        foreach (var pad in _open.Values) SDL_GameControllerRumble(pad, 30000, 30000, 200);
    }

    public void Dispose()
    {
        foreach (var pad in _open.Values) SDL_GameControllerClose(pad);
        _open.Clear();
        SDL_QuitSubSystem(SDL_INIT_GAMECONTROLLER);
    }

    static int SdlId(int id) => 100 + id;

    static string Utf8(IntPtr text) => text == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(text) ?? "";

    const string Lib = "SDL2";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern int SDL_Init(uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern void SDL_QuitSubSystem(uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern int SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr SDL_GetError();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern void SDL_PumpEvents();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern void SDL_FlushEvents(uint minType, uint maxType);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern int SDL_NumJoysticks();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern int SDL_JoystickGetDeviceInstanceID(int deviceIndex);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern int SDL_IsGameController(int deviceIndex);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr SDL_GameControllerOpen(int deviceIndex);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern void SDL_GameControllerClose(IntPtr pad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern int SDL_GameControllerGetAttached(IntPtr pad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr SDL_GameControllerName(IntPtr pad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern byte SDL_GameControllerGetButton(IntPtr pad, int button);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern short SDL_GameControllerGetAxis(IntPtr pad, int axis);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern int SDL_GameControllerRumble(IntPtr pad, ushort low, ushort high, uint durationMs);
}
