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
            foreach (var (look, name) in PrototypeWindow.LookNames.Select((n, i) => (i, $"{(char)('A' + i)}-{n.ToLower()}")))
                foreach (var s in states)
                    Save(widget.QaCard(look, compact, s.Codex, s.Claude, s.Cx),
                        Path.Combine(folder, $"{name}{(compact ? "-compact" : "")}-{s.Name}.png"));
        // A few parts the suggested sets don't show, each on Bars full (or Numbers) with both sources.
        LookOptions Set(int look, Action<LookOptions> change)
        {
            var o = LookOptions.Default(look, false);
            change(o);
            return o;
        }
        foreach (var (look, si, file, o) in new[]
        {
            (0, 2, "pace", Set(0, o => { o.Tick = o.PaceWords = true; o.Rotate = false; })),
            (0, 2, "blocks", Set(0, o => o.BarStyle = BarStyle.Blocks)),
            (0, 3, "updated-clock", Set(0, o => { o.Updated = true; o.Reset = ResetShow.ClockTime; })),
            (0, 1, "session-only", Set(0, o => o.Limits = Limits.Session)),
            (0, 1, "side-by-side", Set(0, o => o.Arrange = Arrangement.SideBySide)),
            (5, 2, "updated", Set(5, o => o.Updated = true)),
            (1, 3, "session-only", Set(1, o => o.Limits = Limits.Session)),
            (2, 1, "no-labels", Set(2, o => o.Labels = false)),
            (3, 2, "rotate", Set(3, o => o.Rotate = true)),
        })
        {
            var s = states[si];
            Save(widget.QaCard(look, false, s.Codex, s.Claude, s.Cx, options: o),
                Path.Combine(folder, $"option-{(char)('A' + look)}-{PrototypeWindow.LookNames[look].ToLower()}-{file}.png"));
        }
        // Dev servers under the looks: full rows, and the short ones under a narrow look.
        var servers = new List<DevServer>
        {
            new(18244, "node", 3000, true) { Project = "usage-site", Title = "Usage Dashboard", Folder = @"C:\Users\you\Projects\usage-site", CommandLine = "node node_modules/next/dist/bin/next dev" },
            new(20112, "node", 5173, true) { Project = "design-tool", Folder = @"C:\Users\you\Projects\design-tool" },
            new(35808, "docker", 8069, true)
            {
                Project = "accounting", Title = "Odoo", Folder = @"\\wsl.localhost\Ubuntu\home\you\accounting", CommandLine = "accounting-odoo-1 · odoo:17",
                Wsl = new WslTarget("Ubuntu", ["accounting-db-1", "accounting-odoo-1"], null),
            },
        };
        foreach (var compact in new[] { false, true })
            foreach (var look in new[] { 0, 2 })
                Save(widget.QaCard(look, compact, true, states[1].Claude, states[1].Cx, servers),
                    Path.Combine(folder, $"servers-{(char)('A' + look)}-{PrototypeWindow.LookNames[look].ToLower()}{(compact ? "-compact" : "")}.png"));
        // The hover card, the same in every look, on a normal, a Stale and a no-Usage source.
        foreach (var (look, si, src, file) in new[] { (0, 1, 0, "normal"), (0, 3, 0, "stale"), (0, 5, 1, "signin") })
        {
            var s = states[si];
            widget.QaCard(look, false, s.Codex, s.Claude, s.Cx);
            SavePopup(widget.QaTip(src), Path.Combine(folder, $"tip-{file}.png"));
        }
        SavePopup(widget.QaServerTip(servers[0]), Path.Combine(folder, "tip-server.png"));
        SavePopup(widget.QaServerTip(servers[2]), Path.Combine(folder, "tip-server-wsl.png"));
        var confirm = Confirm.Make(null, "Close Usage Dashboard?",
            "Stops node (PID 18244) on localhost:3000 and everything it started. Unsaved work in it is lost.", "Close");
        var card = (FrameworkElement)confirm.Content;
        confirm.Content = null;
        Save(card, Path.Combine(folder, "confirm.png"));
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

    /// A tooltip has its template only while it is open in its popup.
    static void SavePopup(ToolTip tip, string path)
    {
        tip.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
        tip.IsOpen = true;
        tip.UpdateLayout();
        Save(tip, path, host: false);
        tip.IsOpen = false;
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
