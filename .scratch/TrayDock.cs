// PROTOTYPE: throwaway. Docked mode: two tray icons next to the clock, Weekly % on the left, Session % on the right
// with the time to its Reset underneath.
// Win11 lets no app reserve space inside the taskbar; pinned tray icons are the only space Windows keeps for us.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace UsageWidget.Prototype;

class TrayDock : IDisposable
{
    static readonly Color Normal = Color.White, Amber = ColorTranslator.FromHtml("#FFB300"),
        Red = ColorTranslator.FromHtml("#EF5350"), Stale = ColorTranslator.FromHtml("#9E9E9E");

    readonly Forms.NotifyIcon session = new(), weekly = new();
    string? sessionKey, weeklyKey;

    public TrayDock(Action undock, Func<Task> update)
    {
        var before = OurEntries().ToHashSet();
        foreach (var icon in new[] { weekly, session }) // the first icon added lands leftmost: Weekly left, Session right
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Undock", null, (_, _) => undock());
            menu.Items.Add("Update now", null, async (_, _) => await update());
            menu.Items.Add("Quit", null, (_, _) => System.Windows.Application.Current.Shutdown());
            icon.ContextMenuStrip = menu;
            icon.MouseClick += async (_, e) => { if (e.Button == Forms.MouseButtons.Left) await update(); };
            icon.Text = icon == session ? "Claude 5h" : "Claude 7d";
            icon.Visible = true;
        }
        System.Windows.Application.Current.Exit += (_, _) => Dispose(); // no ghost icons after Quit
        _ = PinAsync(before);
    }

    /// Win11 hides new tray icons behind the ^ arrow. Flip our own entries to "show on taskbar", the same switch as
    /// Settings → Taskbar → Other system tray icons. Explorer writes the entries shortly after the icons appear.
    /// Each run can get fresh entries, so wait for two new ones; if none turn up, Windows reused the old ones.
    static async Task PinAsync(HashSet<string> before)
    {
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(1000);
            using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (root == null) return;
            var ours = OurEntries();
            if (ours.Count(name => !before.Contains(name)) < 2 && i < 5) continue;
            // Explorer only reacts to a change, and an entry already at 1 stays hidden: so switch off, then on.
            foreach (var value in new[] { 0, 1 })
            {
                foreach (var name in ours)
                {
                    using var key = root.OpenSubKey(name, writable: true);
                    key?.SetValue("IsPromoted", value, Microsoft.Win32.RegistryValueKind.DWord);
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
            using var key = root.OpenSubKey(name);
            var path = key?.GetValue("ExecutablePath") as string ?? "";
            var tip = key?.GetValue("InitialTooltip") as string ?? "";
            return path.EndsWith(@"\dotnet.exe", StringComparison.OrdinalIgnoreCase) && (tip.Contains("Claude") || tip.Contains("limit"));
        }).ToList();
    }

    public void Render(Usage? last, string? error, string countdown)
    {
        var warn = error != null ? $"\n⚠ {error}" : "";
        Set(session, ref sessionKey, last?.SessionPct, error, last == null ? null : Left(last.SessionReset),
            $"Session limit (5h): {Pct(last?.SessionPct)}\nresets in {countdown}{warn}");
        Set(weekly, ref weeklyKey, last?.WeeklyPct, error, null,
            $"Weekly limit (7d): {Pct(last?.WeeklyPct)}\nresets {(last == null ? "—" : last.WeeklyReset.ToLocalTime().ToString("ddd HH:mm"))}{warn}");
    }

    /// Time to the Reset, short enough for the second line of a tray icon: "2h", then "45m" in the last hour.
    static string Left(DateTimeOffset at)
    {
        var left = at - DateTimeOffset.Now;
        return left <= TimeSpan.Zero ? "0m" : left.TotalHours >= 1 ? $"{(int)left.TotalHours}h" : $"{Math.Max(1, (int)left.TotalMinutes)}m";
    }

    static void Set(Forms.NotifyIcon icon, ref string? key, double? pct, string? error, string? below, string tip)
    {
        icon.Text = tip.Length > 127 ? tip[..127] : tip; // tray tooltips are capped at 127 chars
        var text = pct is not { } p ? "–" : p >= 99.5 ? "!!" : $"{p:0}";
        var color = error != null || pct == null ? Stale : pct >= 90 ? Red : pct >= 75 ? Amber : Normal;
        var newKey = $"{text}|{below}|{color.ToArgb()}";
        if (newKey == key) return; // only redraw when the digits or colour change
        key = newKey;

        var old = icon.Icon;
        icon.Icon = Draw(text, below, color);
        if (old != null) { DestroyIcon(old.Handle); old.Dispose(); }
    }

    /// Light digits, centred. With a second line, the % sits on top and the smaller, dimmer line under it.
    static Icon Draw(string text, string? below, Color color)
    {
        var size = Forms.SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (below == null)
            Fill(g, text, "Segoe UI Semibold", color, size, size * 0.5f, size / 2f);
        else
        {
            Fill(g, text, "Segoe UI Semibold", color, size, size * 0.40f, size * 0.24f);
            Fill(g, below, "Segoe UI Semibold", Normal, size, size * 0.40f, size * 0.76f);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    /// Draw the text large, then scale the ink to the wanted height (never wider than the icon), centred on y.
    /// (A layout box the size of the icon clips wide text to nothing.)
    static void Fill(Graphics g, string text, string font, Color color, int size, float height, float y)
    {
        using var path = new GraphicsPath();
        path.AddString(text, new FontFamily(font), (int)FontStyle.Regular, 100, new PointF(0, 0), StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        var k = Math.Min(size / bounds.Width, height / bounds.Height);
        using var m = new Matrix();
        m.Translate(size / 2f, y);
        m.Scale(k, k);
        m.Translate(-(bounds.Left + bounds.Width / 2), -(bounds.Top + bounds.Height / 2));
        path.Transform(m);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    static string Pct(double? pct) => pct is { } p ? $"{p:0}%" : "—";

    public void Dispose()
    {
        session.Visible = weekly.Visible = false;
        session.Dispose();
        weekly.Dispose();
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
}
