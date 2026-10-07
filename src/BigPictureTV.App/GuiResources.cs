using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BigPictureTV.App;

/// <summary>How many Windows USER and GDI objects this app holds (each capped at 10,000 per program).</summary>
static class GuiResources
{
    public static int UserObjects => Count(1);
    public static int GdiObjects => Count(0);

    static int Count(uint kind)
    {
        using var self = Process.GetCurrentProcess();
        return (int)GetGuiResources(self.Handle, kind);
    }

    [DllImport("user32.dll")]
    static extern uint GetGuiResources(IntPtr process, uint flags);
}
