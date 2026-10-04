using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BigPictureTV.Core.Display;

namespace BigPictureTV.App;

/// <summary>Shows a big number on each active display for a few seconds, like Windows' "Identify".</summary>
public static class IdentifyOverlay
{
    /// <param name="numbered">Displays in the order the settings list shows them (1, 2, 3...).</param>
    public static void Show(IReadOnlyList<DisplayInfo> numbered)
    {
        var forms = new List<Form>();
        for (int i = 0; i < numbered.Count; i++)
        {
            var d = numbered[i];
            if (!d.Active || d.GdiName.Length == 0) continue;
            var screen = Screen.AllScreens.FirstOrDefault(s =>
                string.Equals(s.DeviceName, d.GdiName, StringComparison.OrdinalIgnoreCase));
            if (screen == null) continue;
            forms.Add(Create(i + 1, d.ToString(), screen.Bounds));
        }

        foreach (var f in forms) f.Show();
        var timer = new Timer { Interval = 3000 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            foreach (var f in forms) f.Close();
        };
        timer.Start();
    }

    static Form Create(int number, string name, Rectangle bounds)
    {
        var size = new Size(360, 240);
        var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            TopMost = true,
            BackColor = Color.FromArgb(30, 30, 30),
            Opacity = 0.9,
            Size = size,
            Location = new Point(bounds.Left + (bounds.Width - size.Width) / 2, bounds.Top + (bounds.Height - size.Height) / 2),
        };
        form.Controls.Add(new Label
        {
            Text = name,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 16),
            Dock = DockStyle.Bottom,
            Height = 50,
            TextAlign = ContentAlignment.MiddleCenter,
        });
        form.Controls.Add(new Label
        {
            Text = number.ToString(),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 96, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        });
        return form;
    }
}
