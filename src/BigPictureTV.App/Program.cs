using System;
using System.IO;
using System.Threading;
using System.Windows;
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
                MessageBoxButton.OK, MessageBoxImage.Information);
            return 1;
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        TrayApp? tray = null;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            log.Write($"Unexpected error: {e.ExceptionObject}");
            tray?.Dispose(); // never leave the user stuck on the TV
        };
        app.DispatcherUnhandledException += (_, e) =>
        {
            log.Write($"Unexpected error: {e.Exception}");
            e.Handled = true;
        };

        try
        {
            tray = new TrayApp(log);
            app.SessionEnding += (_, _) => tray.Dispose();
            app.Run();
        }
        finally
        {
            tray?.Dispose();
            mutex.ReleaseMutex();
        }
        return 0;
    }
}
