using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using BigPictureTV.Core;
using BigPictureTV.Core.Input;

namespace BigPictureTV.App;

/// <summary>
/// Watches controllers on its own thread for the shortcuts from the settings
/// (switch to the TV, open the TV menu) and, while the TV menu wants them,
/// reports every button state. Xbox-style controllers are read with XInput;
/// PlayStation, Switch and other HID controllers with SDL's HIDAPI drivers.
/// Reads about 30 times a second while one is connected, and looks for new
/// ones every 2 seconds.
/// </summary>
sealed class GamepadService : IDisposable
{
    const int MaxControllers = 4;
    const int SdlIds = 100; // SDL controllers are numbered from here, after the XInput slots
    const uint ERROR_SUCCESS = 0;
    const byte TriggerThreshold = 128;
    const short StickThreshold = 16000; // about half way
    static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(33);
    static readonly TimeSpan ProbeEvery = TimeSpan.FromSeconds(2);
    // One controller can show up twice (Steam presents a PlayStation one as
    // an Xbox one too): a shortcut seen again this soon is the same press.
    static readonly TimeSpan SamePress = TimeSpan.FromMilliseconds(500);

    readonly ILog _log;
    readonly Action _onCombo;
    readonly Action _onMenuButton;
    readonly Thread _thread;
    readonly ManualResetEventSlim _stop = new();
    readonly Dictionary<int, BindingDetector> _comboDetectors = new();
    readonly Dictionary<int, BindingDetector> _menuDetectors = new();
    readonly bool[] _connected = new bool[MaxControllers];
    DateTime _lastCombo, _lastMenu;
    SdlPads? _sdl;
    volatile bool _dropSdl;
    uint _lastError;
    volatile int _pressed = -1;
    bool _available = true;

    /// <summary>Buttons that switch between the TV and the desktop; None turns it off. Safe to set from any thread.</summary>
    public GamepadButtons Combo { get; set; }

    /// <summary>Seconds to hold <see cref="Combo"/>; 0 is a quick tap.</summary>
    public double ComboHold { get; set; } = GamepadCombo.Hold.TotalSeconds;

    /// <summary>Buttons that open the TV menu; None turns it off. Safe to set from any thread.</summary>
    public GamepadButtons MenuButton { get; set; }

    /// <summary>Seconds to hold <see cref="MenuButton"/>; 0 is a quick tap.</summary>
    public double MenuHold { get; set; }

    /// <summary>Buzz the controller when the combo fires. Safe to set from any thread.</summary>
    public bool Rumble { get; set; }

    /// <summary>
    /// Called on the watcher thread with the buttons held across all
    /// controllers, on every read while one is connected; left stick pushes
    /// count as the cross. Null: nobody listens. Safe to set from any thread.
    /// </summary>
    public Action<GamepadButtons>? Listener { get; set; }

    /// <summary>Buttons held right now across all controllers, or null if none is connected.</summary>
    public GamepadButtons? Pressed => _pressed < 0 ? null : (GamepadButtons)_pressed;

    /// <summary>Whether PlayStation/Switch controllers are read (SDL loaded and still on).</summary>
    public bool ReadsOtherControllers => _sdl != null && !_dropSdl;

    /// <param name="onCombo">Called on the watcher thread; hand it to the UI thread.</param>
    /// <param name="onMenuButton">Called on the watcher thread when the menu button is pressed.</param>
    public GamepadService(ILog log, Action onCombo, Action onMenuButton)
    {
        _log = log;
        _onCombo = onCombo;
        _onMenuButton = onMenuButton;
        _thread = new Thread(Run) { IsBackground = true, Name = "Controller", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>Stops reading PlayStation/Switch controllers (SDL) for the rest of the run. Safe from any thread.</summary>
    public void DropOtherControllers() => _dropSdl = true;

    void Run()
    {
        _sdl = SdlPads.TryStart(_log);
        try
        {
            var nextProbe = DateTime.MinValue;
            while (!_stop.IsSet)
            {
                var now = DateTime.UtcNow;
                bool probe = now >= nextProbe;
                if (probe) nextProbe = now + ProbeEvery;

                int pressed = -1;
                var stick = GamepadButtons.None;
                try
                {
                    if (_dropSdl && _sdl != null)
                    {
                        _sdl.Dispose();
                        _sdl = null;
                        _log.Write("Stopped reading PlayStation/Switch controllers.");
                    }
                    var pads = ReadXInput(probe);
                    if (_sdl != null)
                        foreach (var (id, b, p) in _sdl.Read(probe)) pads.Add((SdlIds + id, b, p));

                    bool combo = false, menu = false;
                    foreach (var (id, buttons, pushed) in pads)
                    {
                        pressed = (pressed < 0 ? 0 : pressed) | (int)buttons;
                        stick |= pushed;
                        if (Detector(_comboDetectors, id).Update(Combo, ComboHold, buttons, now)) combo = true;
                        if (Detector(_menuDetectors, id).Update(MenuButton, MenuHold, buttons, now)) menu = true;
                    }
                    if (combo && now - _lastCombo > SamePress)
                    {
                        _lastCombo = now;
                        _log.Write($"Controller: {GamepadCombo.Format(Combo)} pressed (switch).");
                        if (Rumble) BuzzAll();
                        _onCombo();
                    }
                    if (menu && now - _lastMenu > SamePress)
                    {
                        _lastMenu = now;
                        _log.Write($"Controller: {GamepadCombo.Format(MenuButton)} pressed (TV menu).");
                        _onMenuButton();
                    }
                    _pressed = pressed;
                    if (pressed >= 0) Listener?.Invoke((GamepadButtons)pressed | stick);
                }
                catch (Exception e)
                {
                    // Never let a controller hiccup take the whole app down.
                    _log.Write($"Controller watcher error: {e}");
                    _stop.Wait(ProbeEvery);
                }
                _stop.Wait(pressed < 0 ? ProbeEvery : PollEvery);
            }
        }
        finally
        {
            _sdl?.Dispose();
        }
    }

    static BindingDetector Detector(Dictionary<int, BindingDetector> detectors, int id)
    {
        if (!detectors.TryGetValue(id, out var detector)) detectors[id] = detector = new BindingDetector();
        return detector;
    }

    // Xbox-style controllers (and anything Steam presents as one).
    List<(int Id, GamepadButtons Buttons, GamepadButtons Stick)> ReadXInput(bool probe)
    {
        var pads = new List<(int, GamepadButtons, GamepadButtons)>();
        for (int i = 0; i < MaxControllers && _available; i++)
        {
            if (!_connected[i] && !probe) continue;
            var buttons = Read(i, out var pushed);
            bool was = _connected[i];
            _connected[i] = buttons != null;
            if (was != _connected[i])
                _log.Write(_connected[i] ? $"Controller {i} (XInput) connected." : $"Controller {i} (XInput) disconnected (error {_lastError}).");
            if (buttons != null) pads.Add((i, buttons.Value, pushed));
        }
        return pads;
    }

    GamepadButtons? Read(int index, out GamepadButtons stick)
    {
        stick = GamepadButtons.None;
        try
        {
            _lastError = XInputGetState((uint)index, out var state);
            if (_lastError != ERROR_SUCCESS) return null;
            var buttons = (GamepadButtons)(state.Gamepad.wButtons & ~0x0C00); // drop undocumented bits we reuse
            if (state.Gamepad.bLeftTrigger >= TriggerThreshold) buttons |= GamepadButtons.LT;
            if (state.Gamepad.bRightTrigger >= TriggerThreshold) buttons |= GamepadButtons.RT;
            var g = state.Gamepad;
            if (Math.Abs((int)g.sThumbLX) >= Math.Abs((int)g.sThumbLY))
            {
                if (g.sThumbLX >= StickThreshold) stick = GamepadButtons.Right;
                else if (g.sThumbLX <= -StickThreshold) stick = GamepadButtons.Left;
            }
            else
            {
                if (g.sThumbLY >= StickThreshold) stick = GamepadButtons.Up;
                else if (g.sThumbLY <= -StickThreshold) stick = GamepadButtons.Down;
            }
            return buttons;
        }
        catch (DllNotFoundException)
        {
            _log.Write("XInput isn't available on this PC.");
            _available = false;
            return null;
        }
    }

    // A short buzz so the player knows the combo worked, even with the TV still dark.
    void BuzzAll()
    {
        _sdl?.BuzzAll();
        var on = new XINPUT_VIBRATION { wLeftMotorSpeed = 30000, wRightMotorSpeed = 30000 };
        var off = new XINPUT_VIBRATION();
        for (uint i = 0; i < MaxControllers; i++) if (_connected[i]) XInputSetState(i, ref on);
        _stop.Wait(200);
        for (uint i = 0; i < MaxControllers; i++) if (_connected[i]) XInputSetState(i, ref off);
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join(TimeSpan.FromSeconds(1));
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger, bRightTrigger;
        public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_VIBRATION
    {
        public ushort wLeftMotorSpeed, wRightMotorSpeed;
    }

    // xinput1_4 ships with Windows 8 and later.
    [DllImport("xinput1_4.dll")]
    static extern uint XInputGetState(uint index, out XINPUT_STATE state);

    [DllImport("xinput1_4.dll")]
    static extern uint XInputSetState(uint index, ref XINPUT_VIBRATION vibration);
}
