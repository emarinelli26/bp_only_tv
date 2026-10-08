using System;
using System.Runtime.InteropServices;

namespace BigPictureTV.App;

/// <summary>Makes an overlay window that never takes the focus, so the app in front keeps getting the keys.</summary>
static class NoActivate
{
    const int GWL_EXSTYLE = -20;
    const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;

    public static void Apply(IntPtr window) =>
        SetWindowLongPtr(window, GWL_EXSTYLE, new IntPtr(GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));

    [DllImport("user32.dll")]
    static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
