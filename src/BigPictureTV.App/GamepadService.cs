using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using BigPictureTV.Core;
using BigPictureTV.Core.Input;

namespace BigPictureTV.App;

/// <summary>
/// Watches controllers on its own thread for the combo from the settings,
/// through SDL (Xbox, PlayStation, Switch and more), or XInput (Xbox-style
/// only) if SDL can't load. Reads connected controllers about 30 times a second
/// and only looks for new ones every 2 seconds, so it costs next to nothing.
/// </summary>
sealed class GamepadService : IDisposable
{
    const int MaxControllers = 4;
    const uint ERROR_SUCCESS = 0;
    const byte TriggerThreshold = 128;
    const short StickThreshold = 16000; // about half way
    static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(33);
    static readonly TimeSpan ProbeEvery = TimeSpan.FromSeconds(2);

    readonly ILog _log;
    readonly Action _onCombo;
    readonly Thread _thread;
    readonly ManualResetEventSlim _stop = new();
    readonly Dictionary<int, ComboDetector> _detectors = new();
    SdlPads? _sdl;
    readonly bool[] _connected = new bool[MaxControllers];
    uint _lastError;
    volatile int _pressed = -1;
    bool _available = true;

    /// <summary>The combo to look for; None turns it off. Safe to set from any thread.</summary>
    public GamepadButtons Combo { get; set; }

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

    /// <param name="onCombo">Called on the watcher thread; hand it to the UI thread.</param>
    public GamepadService(ILog log, Action onCombo)
    {
        _log = log;
        _onCombo = onCombo;
        _thread = new Thread(Run) { IsBackground = true, Name = "Controller", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

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
                foreach (var (id, buttons, pushed) in _sdl != null ? _sdl.Read(probe) : ReadXInput(probe))
                {
                    pressed = (pressed < 0 ? 0 : pressed) | (int)buttons;
                    stick |= pushed;
                    if (!_detectors.TryGetValue(id, out var detector)) _detectors[id] = detector = new ComboDetector();
                    if (detector.Update(Combo, buttons, now))
                    {
                        _log.Write($"Controller {id}: {GamepadCombo.Format(Combo)} held.");
                        if (Rumble) Buzz(id);
                        _onCombo();
                    }
                }
                _pressed = pressed;
                if (pressed >= 0) Listener?.Invoke((GamepadButtons)pressed | stick);
                _stop.Wait(pressed < 0 ? ProbeEvery : PollEvery);
            }
        }
        finally
        {
            _sdl?.Dispose();
        }
    }

    // Fallback when SDL can't load: Xbox-style controllers only.
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
                _log.Write(_connected[i] ? $"Controller {i} connected." : $"Controller {i} disconnected (XInput error {_lastError}).");
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
            _log.Write("XInput isn't available on this PC; controller combo off.");
            _available = false;
            return null;
        }
    }

    // A short buzz so the player knows the combo worked, even with the TV still dark.
    void Buzz(int index)
    {
        if (_sdl != null)
        {
            _sdl.Buzz(index);
            return;
        }
        var on = new XINPUT_VIBRATION { wLeftMotorSpeed = 30000, wRightMotorSpeed = 30000 };
        var off = new XINPUT_VIBRATION();
        XInputSetState((uint)index, ref on);
        _stop.Wait(200);
        XInputSetState((uint)index, ref off);
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
