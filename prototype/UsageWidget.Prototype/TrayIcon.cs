// PROTOTYPE: throwaway. The tray icon next to the clock: two small bars, Claude on top and Codex below (Claude 5h and 7d
// when Codex isn't shown), or the same two as rings, coloured like the widget's bars. Hover for the numbers; click shows
// or hides the Pill.
// Win11 lets no app draw inside the taskbar; a tray icon is the one place Windows keeps for us, and it never moves.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace UsageWidget.Prototype;

class TrayIcon : IDisposable
{
    static readonly Color Normal = ColorTranslator.FromHtml("#4FC3F7"), Amber = ColorTranslator.FromHtml("#FFB300"),
        Red = ColorTranslator.FromHtml("#EF5350"), // Stale is darker than the widget's grey, so it still stands out from the track on a light taskbar.
        Stale = ColorTranslator.FromHtml("#6A6A6A"),
        // Brighter than the widget's track: the taskbar behind it can be light or dark.
        Track = Color.FromArgb(0x80, 0x9E, 0x9E, 0x9E),
        // Fainter for the thinner rings, so a grey Stale arc still shows on a dark taskbar.
        RingTrack = Color.FromArgb(0x50, 0x9E, 0x9E, 0x9E);

    public record Bar(double? Pct, bool Stale);

    readonly Forms.NotifyIcon icon = new();
    string? key;

    public TrayIcon(Action toggle, Action update, Action settings)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show or hide", null, (_, _) => toggle());
        menu.Items.Add("Update now", null, (_, _) => update());
        menu.Items.Add("Settings…", null, (_, _) => settings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => System.Windows.Application.Current.Shutdown());
        icon.ContextMenuStrip = menu;
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) toggle(); };
        icon.Text = "Claude: updating…";
        Render(new(null, true), new(null, true), icon.Text, rings: false);
        var before = OurEntries().ToHashSet();
        icon.Visible = true;
        System.Windows.Application.Current.Exit += (_, _) => Dispose(); // no ghost icon after Quit
        _ = PinAsync(before);
    }

    /// Redraws only when a fill or a colour changes; the tooltip is cheap and always set.
    /// `rings`: `top` is the outer ring and `bottom` the inner one.
    public void Render(Bar top, Bar bottom, string tip, bool rings)
    {
        icon.Text = tip.Length > 127 ? tip[..127] : tip; // tray tooltips are capped at 127 chars
        var newKey = $"{rings}|{Key(top)}|{Key(bottom)}";
        if (newKey == key) return;
        key = newKey;
        var old = icon.Icon;
        icon.Icon = rings ? DrawRings(top, bottom) : Draw(top, bottom);
        if (old != null) { DestroyIcon(old.Handle); old.Dispose(); }
    }

    // Whole percent is all a 14-unit bar can show. Any use above 0 draws a sliver, so it keys as at least 1.
    static string Key(Bar b) => $"{(b.Pct is { } p ? p <= 0 ? 0 : Math.Max(1, Math.Round(Math.Min(p, 100))) : -1)}{ColorOf(b).ToArgb()}";

    static Color ColorOf(Bar b) => b.Stale || b.Pct == null ? Stale : b.Pct >= 90 ? Red : b.Pct >= 75 ? Amber : Normal;

    /// On a 16-unit grid: a 14 × 4 track at y 3 and at y 9, each filled to its %, drawn at the tray's pixel size.
    /// Edges land on whole pixels, so the bars stay sharp at 125% and 150%.
    static Icon Draw(Bar top, Bar bottom)
    {
        var size = (int)Math.Round(16 * GetDpiForSystem() / 96.0);
        var u = size / 16f;
        int x = (int)Math.Round(u), h = (int)Math.Round(4 * u), gap = (int)Math.Round(2 * u), y0 = (size - 2 * h - gap) / 2;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half; // else a whole-pixel rect smears half a pixel up and left
        foreach (var (bar, y) in new[] { (top, y0), (bottom, y0 + h + gap) })
        {
            var rect = new RectangleF(x, y, size - 2 * x, h);
            using var track = Rounded(rect, u);
            using var trackBrush = new SolidBrush(Track);
            g.FillPath(trackBrush, track);
            if (bar.Pct is not { } p || p <= 0) continue;
            // Its own rounded shape (a clip would leave square, jagged corners); a short fill rounds less.
            var w = Math.Max(u, rect.Width * (float)Math.Min(p, 100) / 100);
            using var shape = Rounded(new RectangleF(rect.X, rect.Y, w, rect.Height), Math.Min(u, w / 2));
            using var fill = new SolidBrush(ColorOf(bar));
            g.FillPath(fill, shape);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    /// On a 16-unit grid: an outer ring of radius 7 and an inner one of 3.75, each 1.75 wide with a 1.5-unit gap, filled
    /// clockwise from the top like the widget's Rings look.
    static Icon DrawRings(Bar outer, Bar inner)
    {
        var size = (int)Math.Round(16 * GetDpiForSystem() / 96.0);
        var u = size / 16f;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var width = 1.75f * u;
        foreach (var (bar, r) in new[] { (outer, 7f * u), (inner, 3.75f * u) })
        {
            var rect = new RectangleF(size / 2f - r, size / 2f - r, 2 * r, 2 * r);
            using var track = new Pen(RingTrack, width);
            g.DrawEllipse(track, rect);
            if (bar.Pct is not { } p || p <= 0) continue;
            using var fill = new Pen(ColorOf(bar), width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (p >= 100) { g.DrawEllipse(fill, rect); continue; }
            // A round cap juts out half the pen past each end: start and stop that much inside, so the arc shows the
            // real % (an inner ring at 91% would otherwise look closed). A tiny % is just the two caps: a dot at the top.
            var cap = (float)(width / 2 / r * 180 / Math.PI);
            var sweep = (float)(p * 3.6) - 2 * cap;
            if (sweep >= 1) { g.DrawArc(fill, rect, -90 + cap, sweep); continue; }
            using var dot = new SolidBrush(fill.Color); // GDI+ fails on a near-zero arc with round caps
            g.FillEllipse(dot, size / 2f - width / 2, size / 2f - r - width / 2, width, width);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// Win11 hides a new tray icon behind the ^ arrow. Flip our own new entry to "show on taskbar", the same switch as
    /// Settings → Taskbar → Other system tray icons. Explorer writes the entry shortly after the icon appears; if no
    /// new one turns up, Windows reused the old one and the user's choice for it stands.
    static async Task PinAsync(HashSet<string> before)
    {
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(1000);
            using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (root == null) return;
            // Only an entry Windows just made: one the user moved behind ^ on purpose stays there.
            var ours = OurEntries().Where(name => !before.Contains(name)).ToList();
            if (ours.Count == 0) { if (i < 5) continue; return; }
            // Explorer only reacts to a change, and an entry already at 1 can stay hidden: so switch off, then on.
            foreach (var value in new[] { 0, 1 })
            {
                foreach (var name in ours)
                {
                    using var entry = root.OpenSubKey(name, writable: true);
                    entry?.SetValue("IsPromoted", value, Microsoft.Win32.RegistryValueKind.DWord);
                }
                await Task.Delay(800);
            }
            return;
        }
    }

    static List<string> OurEntries()
    {
        using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
        if (root == null) return [];
        return root.GetSubKeyNames().Where(name =>
        {
            using var entry = root.OpenSubKey(name);
            var path = entry?.GetValue("ExecutablePath") as string ?? "";
            var tip = entry?.GetValue("InitialTooltip") as string ?? "";
            return path.EndsWith(@"\dotnet.exe", StringComparison.OrdinalIgnoreCase) && tip.StartsWith("Claude");
        }).ToList();
    }

    /// QA only (--qa): the icon as the tray gets it, at this screen's size.
    internal static Bitmap QaDraw(Bar top, Bar bottom, bool rings)
    {
        using var ico = rings ? DrawRings(top, bottom) : Draw(top, bottom);
        var bmp = ico.ToBitmap();
        DestroyIcon(ico.Handle);
        return bmp;
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
}
