// PROTOTYPE: throwaway. A tray icon, no hotkey. Settings (look, size, position...) live in Settings.cs.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace UsageWidget.Prototype;

class PrototypeWindow : Window
{
    static readonly Brush Normal = Theme.Normal, Amber = Theme.Amber, Red = Theme.Red, Track = Theme.Track,
        Text = Theme.Primary, Dim = Theme.Tertiary, Edge = Theme.Edge;

    readonly (string Key, string Name, Func<UIElement> Build)[] variants;
    /// In the order of Settings.Look; the Settings window shows the same names.
    internal static readonly string[] LookNames = ["Rings", "Pill", "Strip", "Gauge", "Meter", "Target", "Numbers", "Mini", "Pace", "Halo"];
    readonly Settings settings = Settings.Load();
    readonly DispatcherTimer updateTimer;
    TrayIcon? tray;
    // Hidden from the tray icon, or because a full-screen app is in front.
    bool hiddenByUser, fullScreen;
    SettingsWindow? settingsWindow;
    bool anchored = true;
    int current;
    Usage? last;
    DateTime? lastOk;
    string? error;
    // Codex Usage, shown under Claude's when Codex is on this machine (ADR 0002).
    Usage? codexLast;
    DateTime? codexOk;
    string? codexError;
    DispatcherTimer? retryTimer;
    DateTime flashUntil;
    // Built once: Render swaps only the gauge, so a Close button isn't rebuilt under the mouse every second.
    readonly ContentControl gauge = new();
    readonly StackPanel serversPanel = new();
    readonly Border frame;
    string serversKey = "";
    List<DevServer> servers = [];
    // While a hover card is open the per-second Render waits, so the card isn't rebuilt away under the mouse.
    bool tipOpen;

    public PrototypeWindow()
    {
        variants = new Func<UIElement>[] { Rings, Pill, Strip, Gauge, Meter, Target, Numbers, Mini, Pace, Halo }
            .Select((build, i) => (((char)('A' + i)).ToString(), LookNames[i], build)).ToArray();

        Title = "PROTOTYPE Coding Agent Usage Meter";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        current = Math.Clamp(settings.Look, 0, variants.Length - 1);
        Topmost = settings.Topmost;
        ShowInTaskbar = false;
        ShowActivated = false; // coming back after a full-screen app must not take its focus
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = settings.IdleOpacity;

        // Stay just above the clock until the user drags it away; after that, only keep it on screen.
        SizeChanged += (_, _) => { if (anchored) PlaceBottomRight(); else KeepOnScreen(); };
        MouseEnter += (_, _) => Opacity = 1;
        MouseLeave += (_, _) => Opacity = settings.IdleOpacity;
        MouseLeftButtonDown += OnLeftDown;
        // Ctrl + wheel resizes, like zoom in a browser.
        MouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            settings.Scale = Math.Clamp(Math.Round(settings.Scale + (e.Delta > 0 ? 0.1 : -0.1), 1), 0.5, 3);
            settings.Save();
            Render();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Right) Switch(current + 1);
            if (e.Key == Key.Left) Switch(current - 1);
        };
        ContextMenu = BuildMenu();

        var body = new StackPanel();
        body.Children.Add(gauge);
        body.Children.Add(serversPanel);
        frame = new Border
        {
            Background = Theme.CardBg, BorderBrush = Edge, BorderThickness = new Thickness(1),
            Child = body,
        };
        Content = frame;

        Render();
        UpdateServers();
        Loaded += async (_, _) =>
        {
            RestorePosition();
            tray = new TrayIcon(ToggleShown, () => { _ = UpdateUsage(); UpdateServers(); }, OpenSettings);
            Render();
            await UpdateUsage();
        };

        updateTimer = new DispatcherTimer(TimeSpan.FromMinutes(settings.UpdateMinutes), DispatcherPriority.Normal, async (_, _) => await UpdateUsage(), Dispatcher);
        new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => { if (!tipOpen) Render(); }, Dispatcher);
        new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Normal, (_, _) => UpdateServers(), Dispatcher);
        new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Normal, (_, _) => CheckFullScreen(), Dispatcher);
    }

    /// From the tray icon: hide the Pill, or bring it back on top where it was.
    void ToggleShown()
    {
        hiddenByUser = IsVisible && !hiddenByUser;
        if (hiddenByUser) Hide();
        else
        {
            ShowOnTop();
            RestorePosition();
        }
    }

    void ShowOnTop()
    {
        Show();
        Topmost = false; Topmost = settings.Topmost; // to the front of other topmost windows
    }

    /// Out of the way while a full-screen app, game or presentation is in front; back when it closes.
    void CheckFullScreen()
    {
        var full = SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4; // busy, D3D, presentation
        // Only on a change, so a tray click during full-screen keeps the widget shown until it ends.
        if (full == fullScreen) return;
        fullScreen = full;
        if (hiddenByUser) return;
        if (full) Hide(); else ShowOnTop();
    }

    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    /// The saved spot if it is still on a screen (a monitor may be unplugged), else just above the clock.
    void RestorePosition()
    {
        var (left, top) = (settings.Left, settings.Top);
        anchored = left is not { } l || top is not { } t ||
            l < SystemParameters.VirtualScreenLeft || t < SystemParameters.VirtualScreenTop ||
            l > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 20 ||
            t > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 20;
        if (anchored) PlaceBottomRight();
        else { Left = left!.Value; Top = top!.Value; KeepOnScreen(); }
    }

    void ResetPosition()
    {
        settings.Left = settings.Top = null;
        settings.Save();
        RestorePosition();
    }

    /// Called by the Settings window after every change.
    void ApplySettings()
    {
        current = Math.Clamp(settings.Look, 0, variants.Length - 1);
        Topmost = settings.Topmost;
        if (!IsMouseOver) Opacity = settings.IdleOpacity;
        updateTimer.Interval = TimeSpan.FromMinutes(settings.UpdateMinutes);
        if (ShowCodex && codexOk == null && codexError == null) _ = UpdateUsage(); // Codex just turned on
        Render();
    }

    void OpenSettings()
    {
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(settings, ApplySettings, ResetPosition);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }

    /// 8 px above the taskbar, right edge lined up with the clock's. (Top right sat on app window buttons and got lost.)
    void PlaceBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - SnapGap;
        Top = area.Bottom - ActualHeight - SnapGap;
    }

    const double SnapGap = 8, SnapDistance = 40;

    /// Dropped within 40 px of its spot above the clock, it snaps there and stays there when its size changes.
    bool NearClock()
    {
        var area = SystemParameters.WorkArea;
        return Math.Abs(area.Right - SnapGap - (Left + ActualWidth)) < SnapDistance &&
            Math.Abs(area.Bottom - SnapGap - (Top + ActualHeight)) < SnapDistance;
    }

    /// Across all screens, so a widget on a second monitor stays there.
    void KeepOnScreen()
    {
        double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
        double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
        Left = Math.Clamp(Left, l, Math.Max(l, r - ActualWidth));
        Top = Math.Clamp(Top, t, Math.Max(t, b - ActualHeight));
    }

    async Task UpdateUsage()
    {
        var codex = ShowCodex ? CodexUsageClient.FetchAsync() : null;
        var (usage, err) = await ClaudeUsageClient.FetchAsync();
        if (usage != null) { last = usage; lastOk = DateTime.Now; }
        error = err;
        if (codex != null)
        {
            var (cUsage, asOf, cErr) = await codex;
            // Logs can be older than an earlier Update; keep whichever is newer.
            if (cUsage != null && (codexOk == null || asOf >= codexOk)) { codexLast = cUsage; codexOk = asOf; }
            codexError = cErr;
        }
        // One retry once the wait the server asked for is over; a newer Update replaces it.
        retryTimer?.Stop();
        if (ClaudeUsageClient.RetryAfter is { } wait)
        {
            retryTimer = new DispatcherTimer { Interval = wait + TimeSpan.FromSeconds(5) };
            retryTimer.Tick += async (_, _) => { retryTimer?.Stop(); await UpdateUsage(); };
            retryTimer.Start();
        }
        Render();
    }

    void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        var (left, top) = (Left, Top);
        DragMove();
        if (Left == left && Top == top) { _ = UpdateUsage(); UpdateServers(); } // a click, not a drag
        else if (NearClock()) ResetPosition();
        else
        {
            anchored = false;
            KeepOnScreen();
            (settings.Left, settings.Top) = (Left, Top);
            settings.Save();
        }
    }

    void Switch(int index)
    {
        current = settings.Look = (index + variants.Length) % variants.Length;
        settings.Save();
        flashUntil = DateTime.Now.AddSeconds(2);
        Render();
    }

    void Render()
    {
        var v = variants[current];
        var stack = new StackPanel();
        if (DateTime.Now < flashUntil)
            stack.Children.Add(Label($"◀ {v.Key} · {v.Name} ▶", 10, Amber, HorizontalAlignment.Center));
        stack.Children.Add(v.Build());
        gauge.Content = stack;
        // Text carries its own space above the letters; a logo or a ring on top doesn't, so it gets more padding.
        var textOnTop = current is 1 or 4 or 6 or 7 or 8 || (current == 2 && !ShowCodex);
        // Mini is small by design, so it always gets the Compact padding. Halo is only rings: the same space all round.
        frame.Padding = current == 9 ? new Thickness(Gap(8, 5)) :
            settings.Compact || current == 7 ? new Thickness(8, textOnTop ? 4.5 : 7.5, 8, 6) : new Thickness(14, 12, 14, 12);
        frame.CornerRadius = new CornerRadius(Gap(10, 8));
        if (frame.LayoutTransform is not ScaleTransform s || s.ScaleX != settings.Scale)
            frame.LayoutTransform = new ScaleTransform(settings.Scale, settings.Scale);
        BuildServers();

        RenderTray();
    }

    /// Each bar is the higher of a source's two limits, the one that stops you first. Without Codex: Claude 5h, then 7d.
    /// As rings, the first tooltip line is the outer ring: Claude (or 5h), then Codex (or 7d) inside.
    void RenderTray()
    {
        if (tray == null) return;
        string Line(Source s) => $"{s.Name}  " + (s.U is not { } u ? "no usage yet" :
            $"5h {u.SessionPct:0}% · 7d {u.WeeklyPct:0}% · resets {Countdown(u.SessionReset)}") + (s.Err is { } e ? $" (old: {Short(e)})" : "");
        var src = Sources().ToArray();
        var claude = src[0];
        var rings = settings.TrayStyle == 1;
        if (src.Length > 1)
            tray.Render(new(Max(claude.U), claude.Stale), new(Max(src[1].U), src[1].Stale), $"{Line(claude)}\n{Line(src[1])}", rings);
        else
            tray.Render(new(claude.U?.SessionPct, claude.Stale), new(claude.U?.WeeklyPct, claude.Stale), Line(claude), rings);
    }

    static double? Max(Usage? u) => u == null ? null : Math.Max(u.SessionPct, u.WeeklyPct);

    /// Codex only when it is on this machine and the user hasn't turned it off.
    bool ShowCodex => qaCodex ?? (settings.ShowCodex && CodexUsageClient.Installed);
    bool? qaCodex;

    /// QA only (--qa): the card with made-up Usage, taken out of the window so it can be drawn to a file.
    internal FrameworkElement QaCard(int look, bool compact, bool codex, (Usage?, DateTime?, string?) claude, (Usage?, DateTime?, string?) cx,
        List<DevServer>? servers = null)
    {
        Content = null;
        this.servers = servers ?? []; // never the real ones, so the files don't change with what runs on this PC
        qaCodex = codex;
        current = look;
        settings.Scale = 1; // never saved: QA doesn't touch the settings file
        settings.Compact = compact;
        (last, lastOk, error) = claude;
        (codexLast, codexOk, codexError) = cx;
        Render();
        return frame;
    }

    internal ContextMenu QaMenu() => BuildMenu();

    /// QA only: the hover card of source `index` in the card QaCard last drew.
    internal ToolTip QaTip(int index) => (ToolTip)Tip(new Border(), Sources().ElementAt(index), mini: current == 9).ToolTip;

    internal ToolTip QaServerTip(DevServer server) => ServerCard(server);

    static readonly Dictionary<string, BitmapImage> Logos = [];

    /// The Claude spark or the Codex mark, `size` px square. With Codex shown they replace the names.
    internal static Image Logo(string name, double size)
    {
        if (!Logos.TryGetValue(name, out var bmp))
        {
            bmp = new BitmapImage(new Uri($"pack://application:,,,/Icons/{name}.png"));
            bmp.Freeze();
            Logos[name] = bmp;
        }
        var img = new Image { Source = bmp, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        // The thin spark reads smaller than the solid knot in the same box; scaled without changing the layout.
        if (name == "claude") img.RenderTransform = new ScaleTransform(1.04, 1.04, size / 2, size / 2);
        // Pure white reads heavier than the spark's orange; about #E0E0E0 on the card.
        if (name == "codex") img.Opacity = 0.87;
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }

    /// The full state and the controls, from right-click → Help.
    void ShowHelp()
    {
        var v = variants[current];
        MessageBox.Show(this,
            $"Variant {v.Key} ({v.Name})\n" +
            (last == null
                ? "No Usage yet\n"
                : $"Session limit: {last.SessionPct:0}%  resets in {Countdown(last.SessionReset)}\n" +
                  $"Weekly limit: {last.WeeklyPct:0}%  resets {last.WeeklyReset?.ToLocalTime().ToString("ddd HH:mm") ?? "—"}\n") +
            $"Last update: {(lastOk is { } t ? t.ToString("HH:mm:ss") : "never")}" +
            (error != null ? $"\n⚠ {error}" : "") +
            (!ShowCodex ? "" : "\n\nCodex\n" +
                (codexLast == null
                    ? "No Usage yet\n"
                    : $"Session limit: {codexLast.SessionPct:0}%  resets in {Countdown(codexLast.SessionReset)}\n" +
                      $"Weekly limit: {codexLast.WeeklyPct:0}%  resets {codexLast.WeeklyReset?.ToLocalTime().ToString("ddd HH:mm") ?? "—"}\n") +
                $"Last update: {(codexOk is { } c ? c.ToString("HH:mm:ss") : "never")}" +
                (codexError != null ? $"\n⚠ {codexError}{(codexLast != null ? " (showing the Codex logs)" : "")}" : "")) +
            "\n\nClick: update · drag: move · Ctrl+wheel: size · ←/→: variant · right-click: menu" +
            "\nDev servers: click localhost to open it · hover a row for its folder · Close stops it",
            "Coding Agent Usage Meter", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// Looks for dev servers; the rows are rebuilt only when the list changes.
    void UpdateServers()
    {
        servers = DevServers.Scan();
        BuildServers();
    }

    /// The rows under the look, hidden when there are none. Rebuilt when the servers, the look or Compact change,
    /// so a Close button isn't rebuilt under the mouse every second.
    void BuildServers()
    {
        var halo = current == 9;
        var key = $"{halo}{settings.Compact}|" + string.Join(",", servers.Select(s => $"{s.Pid}:{s.Port}"));
        if (key == serversKey) return;
        serversKey = key;

        serversPanel.Children.Clear();
        if (servers.Count == 0) return;
        serversPanel.Children.Add(new Border { Height = 1, Background = Theme.Divider, Margin = new Thickness(0, Gap(8, 5), 0, Gap(6, 3)) });
        foreach (var server in servers)
        {
            // Halo is too narrow for the full row: just :port and ×, the project is in the hover card.
            var row = new DockPanel { Height = halo ? Gap(18, 16) : Gap(20, 17), Background = Brushes.Transparent };
            var close = halo
                ? new Button { Style = (Style)FindResource("ServerX"), Margin = new Thickness(6, 0, 0, 0) }
                : new Button { Style = (Style)FindResource("ServerClose"), Content = "Close", Margin = new Thickness(8, 0, 0, 0) };
            close.ToolTip = $"Stop {server.Name} (PID {server.Pid}) and everything it started";
            close.Click += (_, _) => ConfirmClose(server);
            DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
            row.Children.Add(close);
            var start = new StackPanel { Orientation = Orientation.Horizontal };
            start.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Theme.Running, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            start.Children.Add(Link(server, halo ? $":{server.Port}" : server.Url));
            if (!halo)
            {
                var project = Label(server.Project, 11, Text, HorizontalAlignment.Left, left: 8);
                project.TextTrimming = TextTrimming.CharacterEllipsis;
                project.MaxWidth = 160;
                start.Children.Add(project);
            }
            row.Children.Add(start);
            row.ToolTip = ServerCard(server);
            ToolTipService.SetPlacement(row, System.Windows.Controls.Primitives.PlacementMode.Top);
            serversPanel.Children.Add(row);
        }
    }

    /// The hover card on a server row: the project, its folder, and what runs it.
    ToolTip ServerCard(DevServer server)
    {
        var card = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Theme.Running, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(Label(server.Project, 12, Text, HorizontalAlignment.Left, bold: true));
        card.Children.Add(head);
        card.Children.Add(Wrapped(server.Folder ?? "folder unknown", 12, Theme.Secondary, top: 2));
        card.Children.Add(Wrapped($"{server.Name} · PID {server.Pid} · {(server.LoopbackOnly ? "only this PC" : "reachable from the network")} · " +
            $"click to open {server.Url}", 11, Dim, top: 4));
        if (server.CommandLine is { } cmd) card.Children.Add(Wrapped(cmd, 11, Dim, top: 2));
        return new ToolTip { Style = (Style)FindResource("CardTip"), Content = card };
    }

    static TextBlock Wrapped(string text, double size, Brush color, double top = 0)
    {
        var label = Label(text, size, color, HorizontalAlignment.Left, top: top);
        label.TextWrapping = TextWrapping.Wrap;
        return label;
    }

    /// `localhost:port` (or `:port`), opening it in the browser. A Button, so the click doesn't start a window drag.
    UIElement Link(DevServer server, string text)
    {
        var link = new Button
        {
            Content = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Normal, FontFamily = Theme.Font,
            Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) },
        };
        link.MouseEnter += (_, _) => link.Foreground = Text;
        link.MouseLeave += (_, _) => link.Foreground = Normal;
        link.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo($"http://{server.Url}") { UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Open dev server", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        return link;
    }

    void ConfirmClose(DevServer server)
    {
        if (!Confirm.Ask(this, $"Close {server.Project}?",
                $"Stops {server.Name} (PID {server.Pid}) on {server.Url} and everything it started. Unsaved work in it is lost.",
                "Close"))
            return;
        if (DevServers.Close(server) is { } err)
            MessageBox.Show(this, $"Could not close {server.Url}:\n{err}", "Close dev server",
                MessageBoxButton.OK, MessageBoxImage.Error);
        UpdateServers();
    }

    ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action click, string gesture = "")
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
            return item;
        }
        Item("Update now", () => _ = UpdateUsage(), "");
        menu.Items.Add(new Separator());
        var looks = variants.Select((v, i) => Item(v.Name, () => Switch(i), "")).ToArray();
        menu.Items.Add(new Separator());
        var compact = Item("Compact", () => { settings.Compact = !settings.Compact; settings.Save(); Render(); });
        Item("Settings…", OpenSettings);
        Item("Help…", ShowHelp);
        menu.Items.Add(new Separator());
        Item("Quit", () => Application.Current.Shutdown());
        // Tick the current look each time the menu opens.
        void Tick()
        {
            for (var i = 0; i < looks.Length; i++) looks[i].IsChecked = i == current;
            compact.IsChecked = settings.Compact;
        }
        menu.Opened += (_, _) => Tick();
        Tick();
        return menu;
    }

    // Each source (Claude, then Codex) is one block with the same parts in every look: its logo (only when Codex is
    // shown), Session and Weekly, the time to the Session Reset, and a status line: the Update time, or why the
    // numbers are Stale. Blocks have fixed widths, so the card doesn't jump when a state changes.
    record Source(string Icon, Usage? U, DateTime? Ok, string? Err)
    {
        public bool Stale => Err != null;
        public string Name => Icon == "claude" ? "Claude" : "Codex";
    }

    /// A space in the normal look, or the smaller one when Compact.
    double Gap(double normal, double compact) => settings.Compact ? compact : normal;

    IEnumerable<Source> Sources()
    {
        yield return new("claude", last, lastOk, error);
        if (ShowCodex) yield return new("codex", codexLast, codexOk, codexError);
    }

    /// Blocks side by side, a divider between them (Rings, Strip).
    UIElement SideBySide(Func<Source, FrameworkElement> block)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var src in Sources())
        {
            if (row.Children.Count > 0)
                row.Children.Add(new Border { Width = 1, Background = Theme.Divider, Margin = new Thickness(Gap(14, 8), 2, Gap(14, 8), 2) });
            row.Children.Add(Tip(block(src), src));
        }
        return row;
    }

    // A: two ring gauges per source.
    UIElement Rings() => SideBySide(src =>
    {
        var col = new StackPanel { Width = Gap(104, 94) };
        if (ShowCodex) col.Children.Add(Centered(SourceLogo(src, 14), bottom: Gap(8, 5)));
        var rings = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        rings.Children.Add(Ring(src.U?.SessionPct, "5h", src.Stale));
        rings.Children.Add(new Border { Width = Gap(12, 6) });
        rings.Children.Add(Ring(src.U?.WeeklyPct, "7d", src.Stale));
        col.Children.Add(rings);
        col.Children.Add(Centered(ResetIn(src), top: Gap(8, 4)));
        col.Children.Add(Centered(Status(src), top: Gap(2, 0)));
        return col;
    });

    UIElement Ring(double? pct, string name, bool stale, double size = 44)
    {
        var grid = new Grid { Width = size, Height = size };
        RingArc(grid, pct, size, 4, stale);
        grid.Children.Add(Label(Pct(pct), 12, stale ? Theme.StaleText : Text, HorizontalAlignment.Center, bold: true));

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(Label(name, 11, Dim, HorizontalAlignment.Center, top: Gap(4, 1.5)));
        return stack;
    }

    /// A ring of outer diameter `d`, centred in `grid`, filled clockwise from the top. A round cap juts out half the
    /// stroke past each end, so the arc starts and stops that much inside: a 95% ring still shows its gap. A tiny % is
    /// just the two caps, a dot at the top.
    static void RingArc(Grid grid, double? pct, double d, double stroke, bool stale)
    {
        grid.Children.Add(new Ellipse { Width = d, Height = d, Stroke = Track, StrokeThickness = stroke });
        if (pct is not { } p || p <= 0) return;
        var color = ColorFor(p, stale);
        if (p >= 99.9) { grid.Children.Add(new Ellipse { Width = d, Height = d, Stroke = color, StrokeThickness = stroke }); return; }
        double r = (d - stroke) / 2, cx = grid.Width / 2, cy = grid.Height / 2, cap = stroke / 2 / r;
        double a0 = cap, a1 = p / 100 * 2 * Math.PI - cap;
        Point At(double a) => new(cx + r * Math.Sin(a), cy - r * Math.Cos(a));
        if (a1 - a0 < 0.02)
        {
            grid.Children.Add(new Ellipse
            {
                Width = stroke, Height = stroke, Fill = color, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(cx - stroke / 2, cy - r - stroke / 2, 0, 0),
            });
            return;
        }
        var fig = new PathFigure { StartPoint = At(a0) };
        fig.Segments.Add(new ArcSegment(At(a1), new Size(r, r), 0, a1 - a0 > Math.PI, SweepDirection.Clockwise, true));
        grid.Children.Add(new Path
        {
            Data = new PathGeometry([fig]), Stroke = color, StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
    }

    // B: per source, two rows of name · % · bar, with the Reset and the status on the right.
    UIElement Pill() => Stacked(src =>
    {
        var grid = Rows(src, ShowCodex ? Gap(28, 22) : 0, Gap(22, 20), Gap(46, 40), Gap(100, 80), 84);
        foreach (var (row, name, pct) in new[] { (0, "5h", src.U?.SessionPct), (1, "7d", src.U?.WeeklyPct) })
        {
            Put(grid, Label(name, 12, Dim, HorizontalAlignment.Left), row, 1);
            Put(grid, Label(Pct(pct), 14, src.Stale ? Theme.StaleText : Text, HorizontalAlignment.Right, bold: true, right: Gap(10, 6)), row, 2);
            Put(grid, Bar(pct, Gap(88, 72), 4, horizontal: true, src.Stale), row, 3);
        }
        ResetAndStatus(grid, src, 4);
        return grid;
    });

    // C: per source, two filling columns like batteries.
    UIElement Strip() => SideBySide(src =>
    {
        var col = new StackPanel { Width = Gap(88, 82) };
        if (ShowCodex) col.Children.Add(Centered(SourceLogo(src, 14), bottom: Gap(8, 2)));
        var bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (name, pct) in new[] { ("5h", src.U?.SessionPct), ("7d", src.U?.WeeklyPct) })
        {
            var c = new StackPanel { Width = Gap(38, 37) };
            c.Children.Add(Label(Pct(pct), 13, src.Stale ? Theme.StaleText : Text, HorizontalAlignment.Center, bold: true, bottom: 4));
            c.Children.Add(Centered(Bar(pct, 8, 64, horizontal: false, src.Stale)));
            c.Children.Add(Label(name, 11, Dim, HorizontalAlignment.Center, top: Gap(4, 2)));
            bars.Children.Add(c);
        }
        col.Children.Add(bars);
        col.Children.Add(Centered(ResetIn(src), top: Gap(8, 4)));
        col.Children.Add(Centered(Status(src), top: Gap(2, 0)));
        return col;
    });

    // D: a half dial per source, 5h on the outside and 7d inside, the 5h % big in the middle.
    UIElement Gauge() => SideBySide(src =>
    {
        var col = new StackPanel { Width = Gap(104, 96) };
        if (ShowCodex) col.Children.Add(Centered(SourceLogo(src, 14), bottom: Gap(6, 3)));
        const double w = 100, h = 58, stroke = 6, cy = 52;
        var dial = new Grid { Width = w, Height = h, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (r, pct) in new[] { (42.0, src.U?.SessionPct), (32.0, src.U?.WeeklyPct) })
        {
            dial.Children.Add(HalfArc(w / 2, cy, r, 100, stroke, Track));
            if (pct is { } p && p > 0) dial.Children.Add(HalfArc(w / 2, cy, r, p, stroke, ColorFor(p, src.Stale)));
        }
        dial.Children.Add(new TextBlock
        {
            Text = Pct(src.U?.SessionPct), FontSize = 17, FontWeight = FontWeights.SemiBold, FontFamily = Theme.Font,
            Foreground = src.Stale ? Theme.StaleText : Text, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0),
        });
        dial.Children.Add(new TextBlock
        {
            Text = $"7d {Pct(src.U?.WeeklyPct)}", FontSize = 10, FontFamily = Theme.Font, Foreground = Dim,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 44, 0, 0),
        });
        col.Children.Add(dial);
        col.Children.Add(Centered(ResetIn(src), top: Gap(6, 3)));
        col.Children.Add(Centered(Status(src), top: Gap(2, 0)));
        return col;
    });

    /// A half dial's arc, from 9 o'clock over the top, `pct` of the way to 3 o'clock.
    static Path HalfArc(double cx, double cy, double r, double pct, double stroke, Brush color)
    {
        var a = Math.PI * (1 + Math.Clamp(pct, 0.5, 100) / 100); // 180° at 0%, 360° at 100%
        var fig = new PathFigure { StartPoint = new Point(cx - r, cy) };
        fig.Segments.Add(new ArcSegment(new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a)), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        return new Path
        {
            Data = new PathGeometry([fig]), Stroke = color, StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
    }

    // E: per source, ten LED blocks per limit. The last three are always amber and red, lit or not, so the edge shows.
    UIElement Meter() => Stacked(src =>
    {
        var grid = Rows(src, ShowCodex ? Gap(26, 22) : 0, Gap(22, 20), Gap(46, 40), Gap(96, 92), 84);
        foreach (var (row, name, pct) in new[] { (0, "5h", src.U?.SessionPct), (1, "7d", src.U?.WeeklyPct) })
        {
            Put(grid, Label(name, 12, Dim, HorizontalAlignment.Left), row, 1);
            Put(grid, Label(Pct(pct), 14, src.Stale ? Theme.StaleText : Text, HorizontalAlignment.Right, bold: true, right: Gap(8, 6)), row, 2);
            var lit = pct is { } p && p > 0 ? Math.Clamp((int)Math.Round(p / 10, MidpointRounding.AwayFromZero), 1, 10) : 0;
            var blocks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (var i = 0; i < 10; i++)
                blocks.Children.Add(new Border
                {
                    Width = 7, Height = 10, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, i < 9 ? 2 : 0, 0),
                    // The last lit block takes the value's own colour, so 91% shows red even before block 10 lights.
                    Background = i < lit
                        ? src.Stale ? Theme.StaleFill : i == lit - 1 && pct >= 75 ? ColorFor(pct.Value, false) : i < 7 ? Normal : i < 9 ? Amber : Red
                        : i < 7 ? Theme.MeterOff : i < 9 ? Theme.MeterOffAmber : Theme.MeterOffRed,
                });
            Put(grid, blocks, row, 3);
        }
        ResetAndStatus(grid, src, 4);
        return grid;
    });

    // F: per source, one double ring around its logo (5h outside, 7d inside), like the tray icon; numbers beside it.
    UIElement Target() => SideBySide(src =>
    {
        var col = new StackPanel { Width = Gap(118, 108) };
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        var ring = new Grid { Width = 52, Height = 52 };
        RingArc(ring, src.U?.SessionPct, 52, 5, src.Stale);
        RingArc(ring, src.U?.WeeklyPct, 36, 5, src.Stale);
        var logo = SourceLogo(src, 14);
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        ring.Children.Add(logo);
        top.Children.Add(ring);
        var nums = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Gap(10, 8), 0, 0, 0) };
        foreach (var (name, pct) in new[] { ("5h", src.U?.SessionPct), ("7d", src.U?.WeeklyPct) })
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Height = 20 };
            var label = Label(name, 11, Dim, HorizontalAlignment.Left);
            label.Width = 18;
            line.Children.Add(label);
            line.Children.Add(Label(Pct(pct), 14, src.Stale ? Theme.StaleText : Text, HorizontalAlignment.Left, bold: true, left: 4));
            nums.Children.Add(line);
        }
        top.Children.Add(nums);
        col.Children.Add(top);
        var reset = ResetIn(src);
        reset.Margin = new Thickness(0, Gap(6, 3), 0, 0);
        col.Children.Add(reset);
        var status = Status(src);
        status.Margin = new Thickness(0, Gap(2, 0), 0, 0);
        col.Children.Add(status);
        return col;
    });

    // G: per source, just the two numbers, big; amber and red once they get high. A hairline under each shows the fill.
    UIElement Numbers() => Stacked(src =>
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
        if (ShowCodex)
        {
            var logo = SourceLogo(src, 16);
            logo.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(new Border { Width = Gap(26, 22), Child = logo });
        }
        foreach (var (name, pct) in new[] { ("5h", src.U?.SessionPct), ("7d", src.U?.WeeklyPct) })
        {
            var color = src.Stale ? Theme.StaleText : pct >= 90 ? Red : pct >= 75 ? Amber : Text;
            var big = new TextBlock { FontFamily = Theme.Font, FontWeight = FontWeights.SemiBold, Foreground = color };
            big.Inlines.Add(new System.Windows.Documents.Run(pct is { } p ? $"{p:0}" : "—") { FontSize = 24 });
            if (pct != null) big.Inlines.Add(new System.Windows.Documents.Run("%") { FontSize = 14 });
            var num = new StackPanel { Width = Gap(62, 56) };
            num.Children.Add(big);
            var line = Bar(pct, 52, 2, horizontal: true, src.Stale);
            line.HorizontalAlignment = HorizontalAlignment.Left;
            line.Margin = new Thickness(0, Gap(2, 1), 0, Gap(3, 1));
            num.Children.Add(line);
            num.Children.Add(Label(name, 11, Dim, HorizontalAlignment.Left));
            row.Children.Add(num);
        }
        var right = new StackPanel { Width = 84, VerticalAlignment = VerticalAlignment.Center };
        var reset = ResetIn(src);
        reset.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(reset);
        var status = Status(src);
        status.HorizontalAlignment = HorizontalAlignment.Right;
        status.Margin = new Thickness(0, Gap(6, 3), 0, 0);
        right.Children.Add(status);
        row.Children.Add(right);
        return row;
    }, Gap(10, 6));

    // H: one thin line per source: two hairline bars (5h on top), the higher %, the time to Reset. The rest is in the tooltip.
    UIElement Mini() => Stacked(src =>
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 18, Background = Brushes.Transparent };
        if (ShowCodex)
        {
            var logo = SourceLogo(src, 12);
            logo.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(logo);
        }
        var bars = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        bars.Children.Add(Bar(src.U?.SessionPct, 56, 3, horizontal: true, src.Stale));
        var weekly = Bar(src.U?.WeeklyPct, 56, 3, horizontal: true, src.Stale);
        weekly.Margin = new Thickness(0, 2, 0, 0);
        bars.Children.Add(weekly);
        row.Children.Add(bars);
        var hi = Label(Pct(Max(src.U)), 12, src.Stale ? Theme.StaleText : Text, HorizontalAlignment.Left, bold: true);
        hi.Width = 34;
        hi.TextAlignment = TextAlignment.Right;
        row.Children.Add(hi);
        row.Children.Add(Label(src.U == null ? "" : Countdown(src.U.SessionReset), 11, src.Stale ? Theme.StaleText : Theme.Secondary,
            HorizontalAlignment.Left, left: 8));
        if (src.Err is { } err)
        {
            var warn = Glyph(Theme.WarningGlyph, 10, NeedsAction(err) ? Theme.Critical : Theme.Tertiary);
            warn.Margin = new Thickness(6, 1, 0, 0);
            row.Children.Add(warn);
        }
        return row;
    }, Gap(6, 4));

    // I: the Pill's bars plus a white tick where an even spread of the use until the Reset would be. Past it: too fast.
    UIElement Pace() => Stacked(src =>
    {
        var grid = Rows(src, ShowCodex ? Gap(28, 22) : 0, Gap(22, 20), Gap(46, 40), Gap(100, 80), Gap(60, 54), 100);
        var now = DateTimeOffset.Now;
        foreach (var (row, name, pct, reset, window) in new[]
        {
            (0, "5h", src.U?.SessionPct, src.U?.SessionReset, TimeSpan.FromHours(5)),
            (1, "7d", src.U?.WeeklyPct, src.U?.WeeklyReset, TimeSpan.FromDays(7)),
        })
        {
            Put(grid, Label(name, 12, Dim, HorizontalAlignment.Left), row, 1);
            Put(grid, Label(Pct(pct), 14, src.Stale ? Theme.StaleText : Text, HorizontalAlignment.Right, bold: true, right: Gap(10, 6)), row, 2);
            double w = Gap(88, 72), h = 4;
            var bar = new Grid { Width = w, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(Bar(pct, w, h, horizontal: true, src.Stale));
            // The share of the Window gone by; unknown until the Window has started.
            double? gone = reset is { } at ? Math.Clamp(1 - (at - now) / window, 0, 1) : null;
            if (gone is { } t)
                bar.Children.Add(new Border
                {
                    Width = 2, Height = h + 6, Background = src.Stale ? Theme.StaleText : Text, CornerRadius = new CornerRadius(1),
                    HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(Math.Clamp(w * t - 1, 0, w - 2), 0, 0, 0),
                });
            Put(grid, bar, row, 3);
            // No verdict on old numbers.
            if (pct is { } p && gone is { } g && !src.Stale)
            {
                var fast = p - g * 100 > 5;
                Put(grid, Label(fast ? "fast" : "on pace", 11, fast ? Amber : Dim, HorizontalAlignment.Left, left: Gap(10, 8)), row, 4);
            }
        }
        ResetAndStatus(grid, src, 5);
        return grid;
    });

    // J: Target with no text: the rings around each logo are the whole widget. Hover a ring for its numbers.
    UIElement Halo()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var src in Sources())
        {
            double d = Gap(44, 34), stroke = Gap(4.5, 3.5);
            var ring = new Grid { Width = d, Height = d, Background = Brushes.Transparent, Margin = new Thickness(row.Children.Count > 0 ? Gap(10, 6) : 0, 0, 0, 0) };
            RingArc(ring, src.U?.SessionPct, d, stroke, src.Stale);
            RingArc(ring, src.U?.WeeklyPct, d - 2 * stroke - 4, stroke, src.Stale);
            var logo = SourceLogo(src, Gap(14, 12));
            logo.HorizontalAlignment = HorizontalAlignment.Center;
            ring.Children.Add(logo);
            // No status line to say the numbers are old, so a small badge does.
            if (src.Err is { } err)
            {
                var badge = new Grid { Width = 13, Height = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -3, -3) };
                badge.Children.Add(new Ellipse { Fill = Theme.CardBg });
                var warn = Glyph(Theme.WarningGlyph, 8, NeedsAction(err) ? Theme.Critical : Theme.Tertiary);
                warn.HorizontalAlignment = HorizontalAlignment.Center;
                warn.Margin = new Thickness(0, 0, 0, 1);
                badge.Children.Add(warn);
                ring.Children.Add(badge);
            }
            row.Children.Add(Tip(ring, src, mini: true));
        }
        return row;
    }

    /// Blocks one under the other, a divider between them (Pill and the row looks).
    UIElement Stacked(Func<Source, FrameworkElement> block, double? gap = null)
    {
        var stack = new StackPanel();
        foreach (var src in Sources())
        {
            if (stack.Children.Count > 0)
                stack.Children.Add(new Border { Height = 1, Background = Theme.Divider, Margin = new Thickness(0, gap ?? Gap(8, 5.5), 0, gap ?? Gap(8, 2.5)) });
            stack.Children.Add(Tip(block(src), src));
        }
        return stack;
    }

    /// Two rows (5h, 7d) in fixed-width columns; column 0 holds the logo when Codex is shown.
    Grid Rows(Source src, params double[] widths)
    {
        var grid = new Grid { Background = Brushes.Transparent };
        foreach (var w in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Gap(22, 18)) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Gap(22, 18)) });
        if (ShowCodex)
        {
            var logo = SourceLogo(src, 16);
            logo.Margin = new Thickness(0, 1, 0, 0);
            logo.HorizontalAlignment = HorizontalAlignment.Left;
            Put(grid, logo, 0, 0, rowSpan: 2);
        }
        return grid;
    }

    static void Put(Grid grid, UIElement el, int row, int col, int rowSpan = 1)
    {
        Grid.SetRow(el, row); Grid.SetColumn(el, col); Grid.SetRowSpan(el, rowSpan);
        grid.Children.Add(el);
    }

    /// The Reset on the first row and the status on the second, right-aligned in column `col`.
    void ResetAndStatus(Grid grid, Source src, int col)
    {
        var reset = ResetIn(src);
        reset.HorizontalAlignment = HorizontalAlignment.Right;
        Put(grid, reset, 0, col);
        var status = Status(src);
        status.HorizontalAlignment = HorizontalAlignment.Right;
        Put(grid, status, 1, col);
    }

    /// The time to the Session Reset. It stays true while the numbers are Stale, so it is only dimmed.
    FrameworkElement ResetIn(Source src)
    {
        var color = src.Stale ? Theme.StaleText : Theme.Secondary;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Visibility = src.U == null ? Visibility.Hidden : Visibility.Visible };
        row.Children.Add(Glyph(Theme.ResetGlyph, 10, color, right: 4));
        row.Children.Add(Label(src.U == null ? "—" : Countdown(src.U.SessionReset), 12, color, HorizontalAlignment.Left));
        return row;
    }

    /// The Update time, or a short reason the numbers are Stale; the full reason is in the tooltip.
    FrameworkElement Status(Source src)
    {
        if (src.Err is not { } err)
            return Label(src.Ok is { } t ? $"Updated {t:HH:mm}" : "Updating…", 11, Dim, HorizontalAlignment.Left);
        var color = NeedsAction(err) ? Theme.Critical : Theme.Tertiary;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Glyph(Theme.WarningGlyph, 10, color, right: 4));
        row.Children.Add(Label(Short(err), 11, color, HorizontalAlignment.Left));
        return row;
    }

    /// Hover a source for a card with both limits, both Resets, and why and since when it is Stale. It opens above
    /// the source, since the widget sits on the taskbar. `mini`: Halo's one line with just the numbers.
    FrameworkElement Tip(FrameworkElement el, Source src, bool mini = false)
    {
        var tip = new ToolTip { Style = (Style)FindResource(mini ? "MiniTip" : "CardTip"), Content = mini ? MiniCard(src) : Card(src) };
        tip.Opened += (_, _) => tipOpen = true;
        tip.Closed += (_, _) => tipOpen = false;
        el.ToolTip = tip;
        ToolTipService.SetPlacement(el, System.Windows.Controls.Primitives.PlacementMode.Top);
        ToolTipService.SetInitialShowDelay(el, 300);
        return el;
    }

    FrameworkElement Card(Source src)
    {
        var u = src.U;
        var numbers = src.Stale ? Theme.StaleText : Text;
        var card = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(SourceLogo(src, 14));
        head.Children.Add(Label(src.Name, 13, numbers, HorizontalAlignment.Left, bold: true, left: 6));
        card.Children.Add(head);
        if (u == null) card.Children.Add(Label("No Usage yet", 12, Theme.Secondary, HorizontalAlignment.Left));
        else
            foreach (var (name, pct, when) in new[]
            {
                ("5h", u.SessionPct, $"resets in {Countdown(u.SessionReset)}"),
                ("7d", u.WeeklyPct, $"resets {u.WeeklyReset?.ToLocalTime().ToString("ddd HH:mm") ?? "—"}"),
            })
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Height = 20 };
                var label = Label(name, 12, Dim, HorizontalAlignment.Left);
                label.Width = 22;
                line.Children.Add(label);
                var value = Label(Pct(pct), 13, numbers, HorizontalAlignment.Left, bold: true, right: 8);
                value.Width = 38;
                value.TextAlignment = TextAlignment.Right;
                line.Children.Add(value);
                line.Children.Add(Bar(pct, 70, 4, horizontal: true, src.Stale));
                line.Children.Add(Label(when, 12, src.Stale ? Theme.StaleText : Theme.Secondary, HorizontalAlignment.Left, left: 10));
                card.Children.Add(line);
            }
        if (src.Err is not { } err)
        {
            card.Children.Add(Label(src.Ok is { } t ? $"Updated {t:HH:mm} · click to update" : "Updating…", 11, Dim, HorizontalAlignment.Left, top: 6));
            return card;
        }
        var color = NeedsAction(err) ? Theme.Critical : Theme.Tertiary;
        var foot = new DockPanel { Margin = new Thickness(0, 6, 0, 0), MaxWidth = 300, HorizontalAlignment = HorizontalAlignment.Left };
        var warn = Glyph(Theme.WarningGlyph, 10, color, right: 5);
        warn.VerticalAlignment = VerticalAlignment.Top;
        warn.Margin = new Thickness(0, 3, 5, 0);
        foot.Children.Add(warn);
        foot.Children.Add(Wrapped($"{err}." + (u != null ? $" These numbers are from {Time(src.Ok)}." : ""), 11, color));
        card.Children.Add(new Border { Height = 1, Background = Theme.Divider, Margin = new Thickness(0, 6, 0, 0) });
        card.Children.Add(foot);
        return card;
    }

    FrameworkElement MiniCard(Source src)
    {
        var u = src.U;
        var numbers = src.Stale ? Theme.StaleText : Text;
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        if (u == null) line.Children.Add(Label($"{src.Name}: no Usage yet", 12, Theme.Secondary, HorizontalAlignment.Left));
        else
        {
            foreach (var (name, pct) in new[] { ("5h", u.SessionPct), ("7d", u.WeeklyPct) })
            {
                line.Children.Add(Label(name, 12, Dim, HorizontalAlignment.Left, left: line.Children.Count > 0 ? 8 : 0));
                line.Children.Add(Label(Pct(pct), 12, numbers, HorizontalAlignment.Left, left: 4));
            }
            var color = src.Stale ? Theme.StaleText : Theme.Secondary;
            line.Children.Add(Glyph(Theme.ResetGlyph, 10, color, right: 4));
            line.Children[^1].SetValue(MarginProperty, new Thickness(8, 1, 4, 0));
            line.Children.Add(Label(Countdown(u.SessionReset), 12, color, HorizontalAlignment.Left));
        }
        if (src.Err is { } err)
            line.Children.Add(Label(Short(err), 12, NeedsAction(err) ? Theme.Critical : Theme.Tertiary, HorizontalAlignment.Left, left: 8));
        return line;
    }

    /// Only the user can fix these: sign in, or run the agent once to refresh its token.
    static bool NeedsAction(string err) =>
        err.Contains("sign in", StringComparison.OrdinalIgnoreCase) || err.StartsWith("Token expired", StringComparison.Ordinal);

    /// The part before a colon is the short reason; the rest is in the tooltip.
    static string Short(string err)
    {
        if (err.Contains("sign in", StringComparison.OrdinalIgnoreCase)) return "Sign in";
        var head = err.Split(':')[0];
        return head.Length <= 14 ? head : "Update failed";
    }

    /// Grey, like its numbers, while they are Stale.
    static FrameworkElement SourceLogo(Source src, double size)
    {
        var logo = Logo(src.Icon, size);
        if (!src.Stale) return logo;
        return new Rectangle
        {
            Width = size, Height = size, Fill = Theme.StaleFill, OpacityMask = new ImageBrush(logo.Source),
            RenderTransform = logo.RenderTransform, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    static T Centered<T>(T el, double top = 0, double bottom = 0) where T : FrameworkElement
    {
        el.HorizontalAlignment = HorizontalAlignment.Center;
        el.Margin = new Thickness(0, top, 0, bottom);
        return el;
    }

    static TextBlock Glyph(string glyph, double size, Brush color, double right = 0) => new()
    {
        Text = glyph, FontFamily = Theme.Icons, FontSize = size, Foreground = color,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, right, 0),
    };

    /// Rounded track; the fill is clipped to the track's shape, so even a small value shows at its true length.
    FrameworkElement Bar(double? pct, double w, double h, bool horizontal, bool stale)
    {
        var fill = Math.Clamp(pct ?? 0, 0, 100) / 100;
        var round = Math.Min(w, h) / 2;
        var track = new Grid
        {
            Width = w, Height = h, Background = Track, VerticalAlignment = VerticalAlignment.Center,
            Clip = new RectangleGeometry(new Rect(0, 0, w, h), round, round),
        };
        if (pct is { } p && fill > 0)
        {
            // The track's clip shapes the start. The far end rounds no more than half the fill's length, so a short
            // fill keeps a flat-sided shape and a full one meets the track's rounded end.
            var length = Math.Max(2, (horizontal ? w : h) * fill);
            var r = Math.Min(round, length / 2);
            track.Children.Add(new Border
            {
                Background = ColorFor(p, stale),
                CornerRadius = horizontal ? new CornerRadius(0, r, r, 0) : new CornerRadius(r, r, 0, 0),
                Width = horizontal ? length : w,
                Height = horizontal ? h : length,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Bottom,
            });
        }
        return track;
    }

    static Brush ColorFor(double pct, bool stale) => stale ? Theme.StaleFill : pct >= 90 ? Red : pct >= 75 ? Amber : Normal;

    static string Time(DateTime? at) => at is { } t ? t.ToString("HH:mm") : "never";

    static string Countdown(DateTimeOffset? at)
    {
        if (at == null) return "not started";
        var left = at.Value - DateTimeOffset.Now;
        if (left <= TimeSpan.Zero) return "now";
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours}h {left.Minutes:00}m" : $"{left.Minutes}m";
    }

    static string Pct(double? pct) => pct is { } p ? $"{p:0}%" : "—";

    static TextBlock Label(string text, double size, Brush color, HorizontalAlignment align,
        bool bold = false, double left = 0, double top = 0, double right = 0, double bottom = 0) => new()
    {
        Text = text, FontSize = size, Foreground = color, HorizontalAlignment = align,
        VerticalAlignment = VerticalAlignment.Center, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Margin = new Thickness(left, top, right, bottom), FontFamily = Theme.Font,
    };

}
