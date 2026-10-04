using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BigPictureTV.App;

/// <summary>Draws the tray icon (a small TV) in code, so there are no image files to ship.</summary>
public static class TrayIcons
{
    public static Icon Desktop { get; } = Draw(Color.FromArgb(160, 160, 160), paused: false);
    public static Icon Tv { get; } = Draw(Color.FromArgb(70, 160, 255), paused: false);
    public static Icon Paused { get; } = Draw(Color.FromArgb(160, 160, 160), paused: true);

    static Icon Draw(Color screen, bool paused)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var frame = new SolidBrush(Color.FromArgb(40, 40, 40));
            using var fill = new SolidBrush(screen);
            using var white = new SolidBrush(Color.White);
            g.FillRectangle(frame, 1, 4, 30, 20);         // bezel
            g.FillRectangle(fill, 3, 6, 26, 16);          // screen
            g.FillRectangle(frame, 13, 24, 6, 3);         // neck
            g.FillRectangle(frame, 8, 27, 16, 3);         // stand
            if (paused)
            {
                g.FillRectangle(white, 11, 9, 4, 10);
                g.FillRectangle(white, 17, 9, 4, 10);
            }
        }
        IntPtr handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
}
