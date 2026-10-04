using System;
using System.Runtime.InteropServices;
using System.Threading;
using BigPictureTV.Core;
using BigPictureTV.Core.Input;

namespace BigPictureTV.App;

/// <summary>
/// Watches Xbox-style (XInput) controllers on its own thread for the combo
/// from the settings. Reads connected controllers about 30 times a second
/// and only looks for new ones every 2 seconds, so it costs next to nothing.
/// </summary>
sealed class GamepadService : IDisposable
{
    const int MaxControllers = 4;
    const uint ERROR_SUCCESS = 0;
    const byte TriggerThreshold = 128;
    static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(33);
    static readonly TimeSpan ProbeEvery = TimeSpan.FromSeconds(2);

    readonly ILog _log;
    readonly Action _onCombo;
    readonly Thread _thread;
    readonly ManualResetEventSlim _stop = new();
    readonly ComboDetector[] _detectors = new ComboDetector[MaxControllers];
    readonly bool[] _connected = new bool[MaxControllers];
    uint _lastError;
    volatile int _pressed = -1;
    bool _available = true;

    /// <summary>The combo to look for; None turns it off. Safe to set from any thread.</summary>
    public GamepadButtons Combo { get; set; }

    /// <summary>Buttons held right now across all controllers, or null if none is connected.</summary>
    public GamepadButtons? Pressed => _pressed < 0 ? null : (GamepadButtons)_pressed;

    /// <param name="onCombo">Called on the watcher thread; hand it to the UI thread.</param>
    public GamepadService(ILog log, Action onCombo)
    {
        _log = log;
        _onCombo = onCombo;
        for (int i = 0; i < MaxControllers; i++) _detectors[i] = new ComboDetector();
        _thread = new Thread(Run) { IsBackground = true, Name = "Controller", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    void Run()
    {
        var nextProbe = DateTime.MinValue;
        while (!_stop.IsSet)
        {
            var now = DateTime.UtcNow;
            bool probe = now >= nextProbe;
            if (probe) nextProbe = now + ProbeEvery;

            int pressed = -1;
            for (int i = 0; i < MaxControllers && _available; i++)
            {
                if (!_connected[i] && !probe) continue;
                var buttons = Read(i);
                bool was = _connected[i];
                _connected[i] = buttons != null;
                if (was != _connected[i])
                    _log.Write(_connected[i] ? $"Controller {i + 1} connected." : $"Controller {i + 1} disconnected (XInput error {_lastError}).");
                if (buttons == null) continue;

                pressed = (pressed < 0 ? 0 : pressed) | (int)buttons.Value;
                if (_detectors[i].Update(Combo, buttons.Value, now))
                {
                    _log.Write($"Controller {i + 1}: {GamepadCombo.Format(Combo)} held.");
                    Rumble(i);
                    _onCombo();
                }
            }
            _pressed = pressed;
            _stop.Wait(pressed < 0 ? ProbeEvery : PollEvery);
        }
    }

    GamepadButtons? Read(int index)
    {
        try
        {
            _lastError = XInputGetState((uint)index, out var state);
            if (_lastError != ERROR_SUCCESS) return null;
            var buttons = (GamepadButtons)(state.Gamepad.wButtons & ~0x0C00); // drop undocumented bits we reuse
            if (state.Gamepad.bLeftTrigger >= TriggerThreshold) buttons |= GamepadButtons.LT;
            if (state.Gamepad.bRightTrigger >= TriggerThreshold) buttons |= GamepadButtons.RT;
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
    void Rumble(int index)
    {
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
