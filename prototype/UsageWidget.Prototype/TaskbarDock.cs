// PROTOTYPE: throwaway. Docked mode: the widget is its own taskbar button. The icon shows the time to the Session
// Reset ("1h"), the label "70 28" shows Weekly % then Session %.
// Win11 lets no app reserve space beside the tray; an app button is the only space Windows makes room for.
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UsageWidget.Prototype;

class TaskbarDock : IDisposable
{
    static readonly Color Normal = Color.White, Amber = ColorTranslator.FromHtml("#FFB300"),
        Red = ColorTranslator.FromHtml("#EF5350"), Stale = ColorTranslator.FromHtml("#9E9E9E");

    readonly Window button;
    readonly IntPtr hwnd;
    IntPtr icon;
    string? key;
    bool disposed;

    /// A minimized, off-screen window: all it shows is its taskbar button. Clicking the button brings back the
    /// floating widget; "Close window" on the button's right-click menu does the same.
    public TaskbarDock(Action undock)
    {
        button = new Window
        {
            Title = "Claude usage", ShowInTaskbar = true, ShowActivated = false,
            Width = 1, Height = 1, Left = -10000, Top = -10000, WindowState = WindowState.Minimized,
        };
        hwnd = new WindowInteropHelper(button).EnsureHandle();
        button.StateChanged += (_, _) => { if (button.WindowState != WindowState.Minimized && !disposed) undock(); };
        button.Closed += (_, _) => { if (!disposed) { disposed = true; undock(); } };
        button.Show();
    }

    public void Render(Usage? last, string? error, Usage? codex)
    {
        // The button label is "70 28": Weekly % then Session %. On a full taskbar Windows leaves room for about six
        // characters, so no names or % signs. The icon shows the time to the Session Reset.
        // Codex follows as "· 17 7"; Claude comes first so a full taskbar cuts Codex, not Claude.
        button.Title = (last == null
            ? (error != null ? $"⚠ {error}" : "Claude: no Usage yet")
            : $"{Pct(last.WeeklyPct)} {Pct(last.SessionPct)}{(error != null ? " ⚠" : "")}") +
            (codex != null ? $" · {Pct(codex.WeeklyPct)} {Pct(codex.SessionPct)}" : "");

        var left = last == null ? "–" : Left(last.SessionReset);
        var color = Tint(last?.SessionPct, error);
        var newKey = $"{left}|{color.ToArgb()}";
        if (newKey == key) return; // only redraw when the time or colour changes
        key = newKey;

        var old = icon;
        icon = Draw(left, color);
        SendMessage(hwnd, WM_SETICON, ICON_BIG, icon);
        SendMessage(hwnd, WM_SETICON, ICON_SMALL, icon);
        if (old != IntPtr.Zero) DestroyIcon(old);
    }

    static string Pct(double pct) => $"{Math.Min(pct, 100):0}";

    static Color Tint(double? pct, string? error) =>
        error != null || pct == null ? Stale : pct >= 90 ? Red : pct >= 75 ? Amber : Normal;

    /// Time to the Session Reset, short: "2h", then "45m" in the last hour.
    static string Left(DateTimeOffset? at)
    {
        if (at == null) return "5h"; // no Session running: the full 5 hours are ahead
        var left = at.Value - DateTimeOffset.Now;
        return left <= TimeSpan.Zero ? "0m" : left.TotalHours >= 1 ? $"{(int)left.TotalHours}h" : $"{Math.Max(1, (int)left.TotalMinutes)}m";
    }

    /// The time left as large digits, drawn at the size the taskbar shows (24 px at 100%) so it stays sharp.
    /// Coloured like the Session %, so it turns amber or red as the Session limit nears.
    static IntPtr Draw(string text, Color color)
    {
        var size = (int)Math.Round(24 * GetDpiForSystem() / 96.0);
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        // Draw the text large, then scale the ink to fit. (A layout box the size of the icon clips wide text to nothing.)
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddString(text, new FontFamily("Segoe UI Semibold"), (int)System.Drawing.FontStyle.Regular, 100, new PointF(0, 0), StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        var k = Math.Min(size * 0.92f / bounds.Width, size * 0.62f / bounds.Height);
        using var m = new System.Drawing.Drawing2D.Matrix();
        m.Translate(size / 2f, size / 2f);
        m.Scale(k, k);
        m.Translate(-(bounds.Left + bounds.Width / 2), -(bounds.Top + bounds.Height / 2));
        path.Transform(m);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
        return bmp.GetHicon();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        button.Close();
        if (icon != IntPtr.Zero) DestroyIcon(icon);
    }

    const int WM_SETICON = 0x80;
    static readonly IntPtr ICON_SMALL = 0, ICON_BIG = 1;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
}
