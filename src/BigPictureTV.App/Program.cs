using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using BigPictureTV.Core;

namespace BigPictureTV.App;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var log = new FileLog(AppPaths.LogFile, console: false);

        // Same name the PowerShell script and bptv use, so only one of them
        // ever drives the displays.
        using var mutex = new Mutex(false, @"Local\BigPictureTV");
        if (!mutex.WaitOne(0))
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
