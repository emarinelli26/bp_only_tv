using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BigPictureTV.App;

/// <summary>
/// After startup or after a window closes, hands memory the app no longer
/// uses back to Windows, so an app that mostly idles next to the clock
/// doesn't keep its startup peak.
/// </summary>
static class MemoryTrim
{
    /// <summary>Trims a few seconds from now, once things have settled.</summary>
    public static void Soon()
    {
        var timer = new Timer { Interval = 3000 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            Now();
        };
        timer.Start();
    }

    public static void Now()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        using var self = Process.GetCurrentProcess();
        EmptyWorkingSet(self.Handle);
    }

    [DllImport("psapi.dll")]
    static extern bool EmptyWorkingSet(IntPtr process);
}
