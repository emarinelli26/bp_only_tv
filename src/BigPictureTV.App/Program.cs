using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using BigPictureTV.Core;

namespace BigPictureTV.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool menu = Array.Exists(args, a => string.Equals(a, "--menu", StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(AppPaths.DataDir);
        var log = new FileLog(AppPaths.LogFile, console: false);

        // Same name the PowerShell script and bptv use, so only one of them
        // ever drives the displays.
        using var mutex = new Mutex(false, @"Local\BigPictureTV");
        bool alone = mutex.WaitOne(0);
        if (menu)
        {
            // Only asks the app next to the clock (started here if needed) for the TV menu.
            if (alone) mutex.ReleaseMutex();
            string exe = Environment.ProcessPath ?? Application.ExecutablePath;
            if (MenuSignal.RequestAndWait(exe, appRunning: !alone)) return 0;
            log.Write("--menu: couldn't reach BigPictureTV.");
            return 1;
        }
        if (!alone)
        {
            MessageBox.Show(Strings.Current.AlreadyRunning, "BigPictureTV",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        TrayApp? tray = null;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            log.Write($"Unexpected error: {e.ExceptionObject}");
            tray?.Dispose(); // never leave the user stuck on the TV
        };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => log.Write($"Unexpected error: {e.Exception}");

        try
        {
            // The tray runs on a plain WinForms message loop; WPF only loads
            // when a window opens (see WpfDialogs).
            tray = new TrayApp(log);
            Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => tray.Dispose();
            Application.Run();
        }
        finally
        {
            tray?.Dispose();
            mutex.ReleaseMutex();
        }
        return 0;
    }
}
