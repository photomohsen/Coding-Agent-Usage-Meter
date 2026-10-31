// PROTOTYPE: throwaway. `--qa <folder>` draws every look and state, the menu and Settings to PNG files, with made-up
// Usage, so a change can be checked (and reviewed) without waiting for a real error or a high Usage.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UsageWidget.Prototype;

static class Qa
{
    // A dark desktop, so the card's edge and corners show as they do on a real wallpaper.
    static readonly Brush Backdrop = new SolidColorBrush(Color.FromRgb(0x2B, 0x30, 0x3B));

    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var now = DateTimeOffset.Now;
        var at = DateTime.Now.AddMinutes(-3);
        Usage U(double s, double w) => new(s, now.AddHours(2).AddMinutes(14), w, now.AddDays(3));
        var states = new (string Name, bool Codex, (Usage?, DateTime?, string?) Claude, (Usage?, DateTime?, string?) Cx)[]
        {
            ("1-claude-only", false, (U(42, 28), at, null), (null, null, null)),
            ("2-both-normal", true, (U(42, 28), at, null), (U(13, 18), at, null)),
            ("3-both-high", true, (U(82, 95), at, null), (U(91, 60), at, null)),
            ("4-claude-stale", true, (U(42, 28), at.AddHours(-1), "Token expired: run claude in a terminal once to refresh it"), (U(13, 18), at, null)),
            ("5-codex-stale", true, (U(42, 28), at, null), (U(13, 18), at.AddHours(-2), "Open Codex to sign in again")),
            ("6-codex-none", true, (U(42, 28), at, null), (null, null, "Sign in to Codex first")),
        };
        var widget = new PrototypeWindow();
        foreach (var compact in new[] { false, true })
            foreach (var (look, name) in new[] { (0, "A-rings"), (1, "B-pill"), (2, "C-strip") })
                foreach (var s in states)
                    Save(widget.QaCard(look, compact, s.Codex, s.Claude, s.Cx),
                        Path.Combine(folder, $"{name}{(compact ? "-compact" : "")}-{s.Name}.png"));
        // A ContextMenu can only be drawn while it is open in its own popup.
        var menu = widget.QaMenu();
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
        menu.IsOpen = true;
        menu.UpdateLayout();
        Save(menu, Path.Combine(folder, "menu.png"), host: false);
        menu.IsOpen = false;
        Save(SettingsWindow.QaContent(), Path.Combine(folder, "settings.png"));
        widget.Close();
        SaveTray(Path.Combine(folder, "tray.png"));
    }

    /// Every tray icon state as bars and as rings, at its real pixel size and blown up 8× (no smoothing), on a dark and a
    /// light taskbar.
    static void SaveTray(string path)
    {
        var icons = new (TrayIcon.Bar Top, TrayIcon.Bar Bottom)[]
        {
            (new(62, false), new(30, false)), (new(82, false), new(40, false)), (new(95, false), new(91, false)),
            (new(62, true), new(30, false)), (new(62, false), new(null, true)), (new(3, false), new(100, false)),
            (new(0.3, false), new(91, false)), // a sliver, and a 91% that must not look full
        };
        using var first = TrayIcon.QaDraw(icons[0].Top, icons[0].Bottom, false);
        int px = first.Width, big = px * 8, cell = big + 24;
        using var sheet = new System.Drawing.Bitmap(icons.Length * cell + 24, 4 * (cell + px + 12) + 12);
        using var g = System.Drawing.Graphics.FromImage(sheet);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        System.Drawing.Color dark = System.Drawing.Color.FromArgb(0x1C, 0x1C, 0x1C), light = System.Drawing.Color.FromArgb(0xEE, 0xEE, 0xEE);
        foreach (var (bg, row, rings) in new[] { (dark, 0, false), (light, 1, false), (dark, 2, true), (light, 3, true) })
        {
            var y = 12 + row * (cell + px + 12);
            using var brush = new System.Drawing.SolidBrush(bg);
            g.FillRectangle(brush, 0, y - 12, sheet.Width, cell + px + 12);
            for (var i = 0; i < icons.Length; i++)
            {
                using var bmp = TrayIcon.QaDraw(icons[i].Top, icons[i].Bottom, rings);
                g.DrawImage(bmp, 12 + i * cell, y, big, big);
                g.DrawImageUnscaled(bmp, 12 + i * cell, y + big + 8);
            }
        }
        sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    /// At 2× the user's 130% size, so small text and edges can be judged.
    static void Save(FrameworkElement el, string path, double scale = 2.6, bool host = true)
    {
        FrameworkElement target = el;
        if (host)
        {
            target = new Border { Background = Backdrop, Padding = new Thickness(16), Child = el };
            target.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            target.Arrange(new Rect(target.DesiredSize));
            target.UpdateLayout();
        }
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth * scale), (int)Math.Ceiling(target.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(target);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using (var file = File.Create(path)) png.Save(file);
        if (target is Border b) b.Child = null;
    }
}
