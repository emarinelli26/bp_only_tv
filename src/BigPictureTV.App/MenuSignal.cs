using System;
using System.Diagnostics;
using System.Threading;

namespace BigPictureTV.App;

/// <summary>
/// Lets "BigPictureTV.exe --menu" (for example a Steam shortcut) open the TV
/// menu in the copy already running next to the clock. That second copy
/// stays open until the menu closes, so Steam sees a "game" running the whole
/// time and gives it the controller instead of Big Picture.
/// </summary>
sealed class MenuSignal : IDisposable
{
    const string OpenName = @"Local\BigPictureTV.OpenMenu";
    const string ClosedName = @"Local\BigPictureTV.MenuClosed";
    const string AppMutexName = @"Local\BigPictureTV";

    readonly EventWaitHandle _open = new(false, EventResetMode.AutoReset, OpenName);
    readonly EventWaitHandle _closed = new(false, EventResetMode.AutoReset, ClosedName);
    readonly ManualResetEvent _stop = new(false);
    readonly Thread _thread;

    /// <param name="onRequest">Called on a background thread when another copy asks for the menu.</param>
    public MenuSignal(Action onRequest)
    {
        _thread = new Thread(() =>
        {
            var handles = new WaitHandle[] { _open, _stop };
            while (WaitHandle.WaitAny(handles) == 0) onRequest();
        }) { IsBackground = true, Name = "Menu requests" };
        _thread.Start();
    }

    /// <summary>The menu is open: a waiting copy keeps waiting.</summary>
    public void MenuOpened() => _closed.Reset();

    /// <summary>The menu closed: lets the copy that asked for it exit.</summary>
    public void MenuClosed() => _closed.Set();

    public void Dispose()
    {
        _closed.Set();
        _stop.Set();
        _thread.Join(TimeSpan.FromSeconds(1));
        _open.Dispose();
        _closed.Dispose();
    }

    /// <summary>
    /// In the second copy: asks the running app for the menu, starting it
    /// first if needed, and waits until the menu closes. False if the app
    /// couldn't be reached.
    /// </summary>
    public static bool RequestAndWait(string exePath, bool appRunning)
    {
        if (!appRunning)
        {
            try { Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false })?.Dispose(); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
        }

        // The running app creates both events at startup; give a fresh start time to get there.
        EventWaitHandle? open = null, closed = null;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!EventWaitHandle.TryOpenExisting(OpenName, out open) || !EventWaitHandle.TryOpenExisting(ClosedName, out closed))
        {
            open?.Dispose();
            if (DateTime.UtcNow > deadline) return false;
            Thread.Sleep(250);
        }
        using (open)
        using (closed)
        {
            // We were started by a click or by Steam, so Windows lets us hand
            // the right to come to the front over to the app.
            AllowSetForegroundWindow(-1);
            closed.Reset();
            open.Set();
            while (!closed.WaitOne(1000))
            {
                if (!Mutex.TryOpenExisting(AppMutexName, out var app)) break; // the app was closed
                app.Dispose();
            }
        }
        return true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
}
