using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BigPictureTV.Core;
using BigPictureTV.Core.Input;
using BigPictureTV.Core.TvMenu;

namespace BigPictureTV.App;

/// <summary>
/// An app opened from the TV menu that is still running: its process (for
/// web tiles, the browser's main process, found by its profile) and, for web
/// tiles, the DevTools channel to its page.
/// </summary>
sealed class RunningApp : IDisposable
{
    readonly ILog _log;
    volatile bool _gone;

    public RunningApp(TvApp app, ILog log)
    {
        App = app;
        _log = log;
    }

    public TvApp App { get; }
    public string Key => App.Key;
    public bool IsWeb => App.Kind == TvAppKind.Web;

    /// <summary>Browser profile folder of a web tile.</summary>
    public string? Profile { get; init; }

    /// <summary>Executable name (no .exe) of the browser of a web tile.</summary>
    public string BrowserName { get; init; } = "";

    public Process? Process { get; private set; }
    public CdpPage? Page { get; private set; }
    public bool Gone => _gone;

    /// <summary>Raised once, on any thread, when the app is closed or quits.</summary>
    public event Action<RunningApp>? Exited;

    /// <param name="handOff">
    /// If it quits sooner than this it was a launcher handing over to another
    /// process: the app is still on screen, so it doesn't count as closed.
    /// </param>
    public void Follow(Process process, TimeSpan handOff = default)
    {
        var old = Process;
        Process = process;
        if (old != null && old.Id != process.Id) old.Dispose();
        var since = DateTime.UtcNow;
        void OnExit()
        {
            if (!ReferenceEquals(process, Process)) return;
            if (DateTime.UtcNow - since < handOff)
            {
                _log.Write($"{App} handed over to another process.");
                ForgetProcess();
                return;
            }
            MarkGone();
        }
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => OnExit();
            if (process.HasExited) OnExit();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _log.Write($"Can't follow {App}: {e.Message}");
        }
    }

    /// <summary>Lets go of a process that handed over to another one, without counting it as closed.</summary>
    public void ForgetProcess()
    {
        var old = Process;
        Process = null;
        old?.Dispose();
    }

    public void Attach(CdpPage page)
    {
        Page = page;
        page.Closed += MarkGone;
    }

    void MarkGone()
    {
        if (_gone) return;
        _gone = true;
        Exited?.Invoke(this);
    }

    /// <summary>Shows its window again, in front.</summary>
    public void BringToFront()
    {
        var window = MainWindow();
        if (window != IntPtr.Zero)
        {
            if (IsIconic(window)) ShowWindow(window, SW_RESTORE);
            KeySender.ForceForeground(window);
        }
        if (Page is { } page) _ = Quiet(page.BringToFrontAsync());
    }

    /// <summary>Minimizes its window (going to the desktop); it keeps running, music keeps playing.</summary>
    public void Minimize()
    {
        var window = MainWindow();
        if (window != IntPtr.Zero) ShowWindow(window, SW_MINIMIZE);
    }

    public bool OwnsForegroundWindow()
    {
        int front = KeySender.ForegroundProcessId();
        if (front == 0) return false;
        try { return Process != null && front == Process.Id; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>Sends a controller action to a web page: through DevTools if connected, else as keys if its window is in front.</summary>
    public void Send(PadAction action)
    {
        if (!IsWeb) return;
        var keys = KeysFor(action);
        if (keys == null) return;
        if (Page is { } page)
        {
            if (TvInterface) Queue(() => SendToTvAsync(page, action, keys.Value));
            else Queue(() => PressAsync(page, keys.Value));
            return;
        }
        if (keys.Value.Key == KeySender.VK_MEDIA_PLAY_PAUSE)
            KeySender.Send(keys.Value); // media keys work anywhere
        else if (keys.Value.Key < 0xE0 && keys.Value.Key != CdpKeys.BrowserSearch && (OwnsForegroundWindow() ||
                 string.Equals(KeySender.ForegroundProcessName(), BrowserName, StringComparison.OrdinalIgnoreCase)))
            KeySender.Send(keys.Value);
    }

    // Page commands run one after another, in the order pressed.
    Task _pageWork = Task.CompletedTask;
    readonly object _pageWorkLock = new();

    void Queue(Func<Task> work)
    {
        lock (_pageWorkLock) _pageWork = _pageWork.ContinueWith(_ => Quiet(work())).Unwrap();
    }

    static Task PressAsync(CdpPage page, Hotkey keys)
    {
        if (keys == AltLeft) return page.GoBackAsync(); // browser shortcut, not a page key
        return CdpKeys.For(keys) is { } k ? page.PressAsync(k.Key, k.Code, k.VirtualKey, k.Modifiers, k.Text) : Task.CompletedTask;
    }

    static Task PressGamepadAsync(CdpPage page, uint key) => page.PressAsync("Unidentified", "", (int)key, 0);

    // YouTube's TV interface believes it runs on a PS5 (see the user agent),
    // where the console hands it controller buttons as these key codes
    // (Cobalt's kSbKeyGamepad*). Triangle searches (a space inside search),
    // Square deletes, L1/R1 change video and L2/R2 seek in the player and
    // jump 4 keys on the keyboard, as on the console.
    const uint GamepadSquare = 0x8002, GamepadTriangle = 0x8003, GamepadL2 = 0x8006, GamepadR2 = 0x8007;

    async Task SendToTvAsync(CdpPage page, PadAction action, Hotkey keys)
    {
        switch (action)
        {
            case PadAction.Search when App.SearchKey.Trim().Length == 0:
                await PressGamepadAsync(page, GamepadTriangle);
                return;
            case PadAction.Option:
                await PressGamepadAsync(page, GamepadSquare);
                return;
            case PadAction.PageUp or PadAction.PageDown:
                // On the home and browse pages L2 starts voice search, which
                // only shows a microphone error on a PC: leave them out there.
                var href = await page.EvaluateAsync("location.href") ?? "";
                if (href.Contains("watch", StringComparison.OrdinalIgnoreCase) ||
                    href.Contains("search", StringComparison.OrdinalIgnoreCase))
                    await PressGamepadAsync(page, action == PadAction.PageUp ? GamepadL2 : GamepadR2);
                return;
            default:
                await PressAsync(page, keys);
                return;
        }
    }

    static readonly Hotkey AltLeft = new(KeyModifiers.Alt, 0x25);
    static Hotkey Press(uint key) => new(KeyModifiers.None, key);

    // Pages with a TV interface (YouTube TV) understand a TV remote's keys,
    // like a console's app does: LB/RB previous/next video, LT/RT rewind and
    // fast forward. Other pages get keyboard keys to move around instead.
    public bool TvInterface => App.UserAgent.Trim().Length > 0;

    Hotkey? KeysFor(PadAction action) => action switch
    {
        PadAction.Up => Press(0x26),
        PadAction.Down => Press(0x28),
        PadAction.Left => Press(0x25),
        PadAction.Right => Press(0x27),
        PadAction.Accept => Press(0x0D),
        PadAction.Back => Hotkey.ParseAny(App.BackKey) ?? AltLeft,
        PadAction.Search => Hotkey.ParseAny(App.SearchKey) ?? Press(TvInterface ? CdpKeys.BrowserSearch : 0x71), // F2: the extension's search
        PadAction.PlayPause => Press(KeySender.VK_MEDIA_PLAY_PAUSE),
        PadAction.Previous => TvInterface ? Press(CdpKeys.MediaPrevious) : new Hotkey(KeyModifiers.Shift, 0x09),
        PadAction.Next => TvInterface ? Press(CdpKeys.MediaNext) : Press(0x09),
        PadAction.PageUp => TvInterface ? Press(CdpKeys.MediaRewind) : Press(0x21),
        PadAction.PageDown => TvInterface ? Press(CdpKeys.MediaFastForward) : Press(0x22),
        PadAction.Option => Press(0x77), // F8: the extension's full-window player; on TV pages, Square (see SendToTvAsync)
        _ => null,
    };

    async Task Quiet(Task task)
    {
        try { await task; }
        catch (Exception e) { _log.Write($"Page command failed: {e.Message}"); }
    }

    /// <summary>Closes it: politely first (browsers then keep logins), then by force.</summary>
    public void Close()
    {
        _gone = true; // closing on purpose: no Exited
        Page?.Dispose();
        var process = Process;
        if (process != null)
        {
            try
            {
                if (!process.HasExited && (!process.CloseMainWindow() || !process.WaitForExit(3000)))
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _log.Write($"Closing {App} failed: {e.Message}");
            }
        }
        if (Profile != null) BrowserProcesses.Close(BrowserName, Profile, _log); // anything left on that profile
        Dispose();
    }

    /// <summary>Whether its main window is up yet.</summary>
    public bool HasWindow => MainWindow() != IntPtr.Zero;

    IntPtr MainWindow()
    {
        try
        {
            var process = Process;
            if (process == null || process.HasExited) return IntPtr.Zero;
            process.Refresh();
            return process.MainWindowHandle;
        }
        catch (InvalidOperationException) { return IntPtr.Zero; }
    }

    public void Dispose()
    {
        Page?.Dispose();
        Process?.Dispose();
    }

    const int SW_MINIMIZE = 6, SW_RESTORE = 9;

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr window);
}

/// <summary>DevTools names for the keys the controller sends.</summary>
static class CdpKeys
{
    // Key codes TV remotes send to web apps. Search and play/pause are
    // Windows keys too; rewind and fast forward exist only on TVs (227, 228).
    public const uint BrowserSearch = 0xAA, MediaNext = 0xB0, MediaPrevious = 0xB1, MediaPlayPause = 0xB3;
    public const uint MediaRewind = 0xE3, MediaFastForward = 0xE4;

    public static (string Key, string Code, int VirtualKey, int Modifiers, string? Text)? For(Hotkey keys)
    {
        int mods = (keys.Modifiers.HasFlag(KeyModifiers.Alt) ? 1 : 0) | (keys.Modifiers.HasFlag(KeyModifiers.Ctrl) ? 2 : 0) |
                   (keys.Modifiers.HasFlag(KeyModifiers.Win) ? 4 : 0) | (keys.Modifiers.HasFlag(KeyModifiers.Shift) ? 8 : 0);
        int vk = (int)keys.Key;
        (string, string, string?)? named = vk switch
        {
            0x25 => ("ArrowLeft", "ArrowLeft", null),
            0x26 => ("ArrowUp", "ArrowUp", null),
            0x27 => ("ArrowRight", "ArrowRight", null),
            0x28 => ("ArrowDown", "ArrowDown", null),
            0x0D => ("Enter", "Enter", "\r"),
            0x1B => ("Escape", "Escape", null),
            0x09 => ("Tab", "Tab", null),
            0x08 => ("Backspace", "Backspace", null),
            0x20 => (" ", "Space", " "),
            0x21 => ("PageUp", "PageUp", null),
            0x22 => ("PageDown", "PageDown", null),
            0x23 => ("End", "End", null),
            0x24 => ("Home", "Home", null),
            0x2E => ("Delete", "Delete", null),
            (int)BrowserSearch => ("BrowserSearch", "BrowserSearch", null),
            (int)MediaNext => ("MediaTrackNext", "MediaTrackNext", null),
            (int)MediaPrevious => ("MediaTrackPrevious", "MediaTrackPrevious", null),
            (int)MediaPlayPause => ("MediaPlayPause", "MediaPlayPause", null),
            (int)MediaRewind => ("MediaRewind", "MediaRewind", null),
            (int)MediaFastForward => ("MediaFastForward", "MediaFastForward", null),
            >= 0x70 and <= 0x7B => ($"F{vk - 0x6F}", $"F{vk - 0x6F}", null),
            >= 0x41 and <= 0x5A => (((char)(vk + 32)).ToString(), $"Key{(char)vk}", mods is 0 or 8 ? ((char)(mods == 8 ? vk : vk + 32)).ToString() : null),
            >= 0x30 and <= 0x39 => (((char)vk).ToString(), $"Digit{(char)vk}", mods == 0 ? ((char)vk).ToString() : null),
            _ => null,
        };
        if (named is not { } n) return null;
        return (n.Item1, n.Item2, vk, mods, n.Item3);
    }
}
