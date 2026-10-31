// PROTOTYPE: throwaway. A tray icon, no hotkey. Settings (look, size, position...) live in Settings.cs.
using System.Diagnostics;
using System.Globalization;
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

    readonly (string Key, string Name, Func<Source, LookOptions, FrameworkElement> Build)[] variants;
    /// In the order of Settings.Look; the Settings window shows the same names.
    internal static readonly string[] LookNames = LookOptions.Names;
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
    // The look is too narrow for full server rows; set by Render.
    bool narrow;

    public PrototypeWindow()
    {
        variants = new Func<Source, LookOptions, FrameworkElement>[] { BarsBlock, RingsBlock, TargetBlock, GaugeBlock, StripBlock, NumbersBlock }
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
        stack.Children.Add(Arranged(v.Build, Options));
        gauge.Content = stack;
        // The same tight spacing in every look and set: Compact shows fewer parts, not less space.
        frame.Padding = new Thickness(8, 6, 8, 6);
        frame.CornerRadius = new CornerRadius(10);
        stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        narrow = stack.DesiredSize.Width < 150;
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
        List<DevServer>? servers = null, LookOptions? options = null)
    {
        Content = null;
        this.servers = servers ?? []; // never the real ones, so the files don't change with what runs on this PC
        qaCodex = codex;
        current = look;
        settings.Scale = 1; // never saved: QA doesn't touch the settings file
        settings.Compact = compact;
        qaOptions = options ?? LookOptions.Default(look, compact);
        (last, lastOk, error) = claude;
        (codexLast, codexOk, codexError) = cx;
        Render();
        return frame;
    }

    internal ContextMenu QaMenu() => BuildMenu();

    /// QA only: the hover card of source `index` in the card QaCard last drew.
    internal ToolTip QaTip(int index) => (ToolTip)Tip(new Border(), Sources().ElementAt(index)).ToolTip;

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
        var halo = narrow;
        var key = $"{halo}|" + string.Join(",", servers.Select(s => $"{s.Pid}:{s.Port}:{s.Project}"));
        if (key == serversKey) return;
        serversKey = key;

        serversPanel.Children.Clear();
        if (servers.Count == 0) return;
        serversPanel.Children.Add(new Border { Height = 1, Background = Theme.Divider, Margin = new Thickness(0, 5, 0, 3) });
        foreach (var server in servers)
        {
            // A narrow look has no room for the full row: just :port and ×, the project is in the hover card.
            var row = new DockPanel { Height = halo ? 16 : 17, Background = Brushes.Transparent };
            var close = halo
                ? new Button { Style = (Style)FindResource("ServerX"), Margin = new Thickness(6, 0, 0, 0) }
                : new Button { Style = (Style)FindResource("ServerClose"), Content = "Close", Margin = new Thickness(8, 0, 0, 0) };
            close.ToolTip = StopsWhat(server);
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
        var runs = server.Wsl is { } w
            ? w.Containers.Length > 0 ? $"Docker in WSL ({w.Distro}) · {string.Join(", ", w.Containers)}" : $"{server.Name} in WSL ({w.Distro}) · PID {w.LinuxPid}"
            : $"{server.Name} · PID {server.Pid}";
        card.Children.Add(Wrapped($"{runs} · {(server.LoopbackOnly ? "only this PC" : "reachable from the network")} · " +
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

    /// What Close stops: the process and what it started, or for a WSL port the containers or Linux process behind it.
    static string StopsWhat(DevServer server) => server.Wsl is { } w
        ? w.Containers.Length > 0 ? $"Stop the {server.Project} containers in WSL ({string.Join(", ", w.Containers)})"
            : $"Stop {server.Name} (PID {w.LinuxPid}) in WSL"
        : $"Stop {server.Name} (PID {server.Pid}) and everything it started";

    async void ConfirmClose(DevServer server)
    {
        var body = server.Wsl is { Containers.Length: > 0 }
            ? $"{StopsWhat(server)}, which serve {server.Url}. They keep their data and start again with docker compose up."
            : $"{StopsWhat(server)} on {server.Url}. Unsaved work in it is lost.";
        if (!Confirm.Ask(this, $"Close {server.Project}?", body, "Close"))
            return;
        // Stopping containers takes a few seconds, so it runs off the UI thread.
        if (await Task.Run(() => DevServers.Close(server)) is { } err)
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

    // Each source (Claude, then Codex) is one block. A look draws the block from the source and the parts the current
    // set shows (LookOptions); the blocks sit stacked or side by side. Spacing is the same in every look and set.
    record Source(string Icon, Usage? U, DateTime? Ok, string? Err)
    {
        public bool Stale => Err != null;
        public string Name => Icon == "claude" ? "Claude" : "Codex";
    }

    /// One limit as a look shows it: its name, its %, and the share of its Window gone by (null until it has started).
    record Limit(string Name, double? Pct, double? Gone);

    IEnumerable<Source> Sources()
    {
        yield return new("claude", last, lastOk, error);
        if (ShowCodex) yield return new("codex", codexLast, codexOk, codexError);
    }

    /// The parts the current look shows: its Full or Compact set (QA: the set it was handed).
    LookOptions Options => qaOptions ?? settings.Options(current);
    LookOptions? qaOptions;

    /// The limits the set shows: both, the 5h one, or whichever is higher now.
    static List<Limit> LimitsOf(Source src, LookOptions o)
    {
        var now = DateTimeOffset.Now;
        double? Gone(DateTimeOffset? reset, TimeSpan window) => reset is { } at ? Math.Clamp(1 - (at - now) / window, 0, 1) : null;
        var session = new Limit("5h", src.U?.SessionPct, Gone(src.U?.SessionReset, TimeSpan.FromHours(5)));
        var weekly = new Limit("7d", src.U?.WeeklyPct, Gone(src.U?.WeeklyReset, TimeSpan.FromDays(7)));
        return o.Limits switch
        {
            Limits.Session => [session],
            Limits.Higher => [weekly.Pct > session.Pct ? weekly : session],
            _ => [session, weekly],
        };
    }

    /// The blocks stacked or side by side, a divider between them, and one "Updated" line if the set shows it.
    UIElement Arranged(Func<Source, LookOptions, FrameworkElement> block, LookOptions o)
    {
        var side = o.Arrange == Arrangement.SideBySide;
        var row = new StackPanel { Orientation = side ? Orientation.Horizontal : Orientation.Vertical };
        foreach (var src in Sources())
        {
            if (row.Children.Count > 0)
                row.Children.Add(side
                    ? new Border { Width = 1, Background = Theme.Divider, Margin = new Thickness(8, 2, 8, 2) }
                    : new Border { Height = 1, Background = Theme.Divider, Margin = new Thickness(0, 4, 0, 4) });
            row.Children.Add(Tip(block(src, o), src));
        }
        if (!o.Updated) return row;
        // Both sources update together, so one line for the widget.
        var all = new StackPanel();
        all.Children.Add(row);
        var at = Sources().Where(s => !s.Stale).Max(s => s.Ok);
        all.Children.Add(Label(at is { } t ? $"Updated {t:HH:mm}" : "Updating…", 11, Dim, HorizontalAlignment.Right, top: 2));
        return all;
    }

    // Bars: per limit a row of name · % · bar, the reset and the status on the right. Was Pill, Pace, Meter and Mini.
    FrameworkElement BarsBlock(Source src, LookOptions o)
    {
        var lims = LimitsOf(src, o);
        var one = lims.Count == 1;
        var block = new DockPanel { Background = Brushes.Transparent };
        if (o.Logo) Dock(block, LogoColumn(src, one ? 18 : 20, one ? 13 : 15));
        var rows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var l in lims)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 16 };
            if (o.Labels) row.Children.Add(Fixed(Label(l.Name, 12, Dim, HorizontalAlignment.Left), 16));
            if (o.Pct)
            {
                var pct = Fixed(Label(Pct(l.Pct), one ? 13 : 14, NumberColor(src), HorizontalAlignment.Left, bold: true, right: 5), one ? 32 : 34);
                pct.TextAlignment = TextAlignment.Right;
                row.Children.Add(pct);
            }
            row.Children.Add(o.BarStyle == BarStyle.Blocks ? Blocks(l.Pct, src.Stale)
                : PaceBar(l, o.BarLength == BarLength.Long ? 72 : 44, one ? 3 : 4, src.Stale, o.Tick));
            if (o.PaceWords)
            {
                // No verdict on old numbers, or before the Window has started.
                var fast = l.Pct - l.Gone * 100 > 5;
                var words = src.Stale || l.Pct == null || l.Gone == null ? "" : fast ? "fast" : "on pace";
                row.Children.Add(Fixed(Label(words, 11, fast && !src.Stale ? Amber : Dim, HorizontalAlignment.Left, left: 8), 44));
            }
            rows.Children.Add(row);
        }
        Dock(block, rows);

        var status = StatusPart(src, o);
        if (one)
        {
            // One row: the reset and the ⚠ in line after the bar.
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            if (ResetPart(src, o) is { } r) line.Children.Add(r);
            if (status != null) { status.Margin = new Thickness(line.Children.Count > 0 ? 6 : 0, 0, 0, 0); line.Children.Add(status); }
            block.Children.Add(line);
        }
        else if (o.Rotate)
        {
            var side = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            if (Rotated(src, o) is { } r) { r.HorizontalAlignment = HorizontalAlignment.Right; side.Children.Add(r); }
            if (status != null) { status.HorizontalAlignment = HorizontalAlignment.Right; side.Children.Add(status); }
            block.Children.Add(side);
        }
        else
        {
            // Two rows: the reset beside the first and the status beside the second; one alone sits in the middle.
            var side = new Grid { Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            var parts = new[] { ResetPart(src, o), status }.OfType<FrameworkElement>().ToArray();
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i].HorizontalAlignment = HorizontalAlignment.Right;
                parts[i].VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(parts[i], i);
                if (parts.Length == 1) Grid.SetRowSpan(parts[i], 2);
                side.Children.Add(parts[i]);
            }
            block.Children.Add(side);
        }
        return block;
    }

    /// A bar with, if asked, a white tick where an even spread of the use until the Reset would be. Past it: too fast.
    FrameworkElement PaceBar(Limit l, double w, double h, bool stale, bool tick)
    {
        var bar = Bar(l.Pct, w, h, horizontal: true, stale);
        if (!tick || l.Gone is not { } t) return bar;
        var grid = new Grid { Width = w, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(bar);
        grid.Children.Add(new Border
        {
            Width = 2, Height = h + 6, Background = stale ? Theme.StaleText : Text, Opacity = 0.9, CornerRadius = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(Math.Clamp(w * t - 1, 0, w - 2), 0, 0, 0),
        });
        return grid;
    }

    /// Ten LED blocks. The last three are always amber and red, lit or not, so the edge shows.
    static FrameworkElement Blocks(double? pct, bool stale)
    {
        var lit = pct is { } p && p > 0 ? Math.Clamp((int)Math.Round(p / 10, MidpointRounding.AwayFromZero), 1, 10) : 0;
        var blocks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 10; i++)
            blocks.Children.Add(new Border
            {
                Width = 6, Height = 9, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, i < 9 ? 2 : 0, 0),
                // The last lit block takes the value's own colour, so 91% shows red even before block 10 lights.
                Background = i < lit
                    ? stale ? Theme.StaleFill : i == lit - 1 && pct >= 75 ? ColorFor(pct!.Value, false) : i < 7 ? Normal : i < 9 ? Amber : Red
                    : i < 7 ? Theme.MeterOff : i < 9 ? Theme.MeterOffAmber : Theme.MeterOffRed,
            });
        return blocks;
    }

    // Rings: a ring per limit with its % and name inside; the logo and the reset centred under them.
    FrameworkElement RingsBlock(Source src, LookOptions o)
    {
        // A little bigger when it holds both the % and the name.
        double d = o.Labels && o.Pct ? 44 : 40;
        var rings = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var l in LimitsOf(src, o))
        {
            var ring = new Grid { Width = d, Height = d, Margin = new Thickness(rings.Children.Count > 0 ? 6 : 0, 0, 0, 0) };
            RingArc(ring, l.Pct, d, 4, src.Stale);
            var inside = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            if (o.Pct) inside.Children.Add(Tight(Label(Pct(l.Pct), 12, NumberColor(src), HorizontalAlignment.Center, bold: true), 13));
            if (o.Labels) inside.Children.Add(Tight(Label(l.Name, 9, Dim, HorizontalAlignment.Center), 9));
            ring.Children.Add(inside);
            rings.Children.Add(ring);
        }
        return WithFooter(src, o, rings);
    }

    // Target: one double ring per source (the first limit outside) around its logo, like the tray icon; the numbers
    // to the right and the reset on its side to the left. Was Target and Halo.
    FrameworkElement TargetBlock(Source src, LookOptions o)
    {
        var lims = LimitsOf(src, o);
        double d = o.Pct || o.Reset != ResetShow.Off ? 44 : 34, stroke = d > 40 ? 4.5 : 3.5;
        var ring = new Grid { Width = d, Height = d, Background = Brushes.Transparent };
        RingArc(ring, lims[0].Pct, d - 1, stroke, src.Stale);
        if (lims.Count > 1) RingArc(ring, lims[1].Pct, d - 2 * stroke - 4, stroke, src.Stale);
        if (o.Logo)
        {
            var logo = SourceLogo(src, d > 40 ? 14 : 12);
            logo.HorizontalAlignment = HorizontalAlignment.Center;
            ring.Children.Add(logo);
        }
        // With the icon alone there is no line for it: a small badge on the ring says the numbers are old.
        var badge = src.Err != null && o.Stale == StaleShow.Icon;
        if (badge) ring.Children.Add(Badge(src.Err!));

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var reset = Rotated(src, o);
        double gap = reset != null ? 4 : 8;
        if (reset != null) { row.Children.Add(reset); ring.Margin = new Thickness(gap, 0, 0, 0); }
        row.Children.Add(ring);
        if (o.Pct)
        {
            var nums = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(gap, 0, 0, 0) };
            foreach (var l in lims)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Height = 19 };
                if (o.Labels) line.Children.Add(Fixed(Label(l.Name, 11, Dim, HorizontalAlignment.Left, right: 6), 22));
                line.Children.Add(Label(Pct(l.Pct), 14, NumberColor(src), HorizontalAlignment.Left, bold: true));
                nums.Children.Add(line);
            }
            row.Children.Add(nums);
        }
        var col = new StackPanel { Background = Brushes.Transparent };
        col.Children.Add(row);
        if (!badge && StatusPart(src, o) is { } status) { status.Margin = new Thickness(0, 2, 0, 0); col.Children.Add(status); }
        return col;
    }

    static FrameworkElement Badge(string err)
    {
        var badge = new Grid { Width = 13, Height = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -3, -3) };
        badge.Children.Add(new Ellipse { Fill = Theme.CardBg });
        var warn = Glyph(Theme.WarningGlyph, 8, NeedsAction(err) ? Theme.Critical : Theme.Tertiary);
        warn.HorizontalAlignment = HorizontalAlignment.Center;
        warn.Margin = new Thickness(0, 0, 0, 1);
        badge.Children.Add(warn);
        return badge;
    }

    // Gauge: a half dial per source (the first limit outside), the first % big in the middle and the second under it.
    FrameworkElement GaugeBlock(Source src, LookOptions o)
    {
        var lims = LimitsOf(src, o);
        var two = lims.Count > 1;
        // Drawn in a 100-wide box and shown 84 wide.
        var dial = new Canvas { Width = 100, Height = o.Pct && (two || o.Labels) ? 66 : 56, LayoutTransform = new ScaleTransform(0.84, 0.84) };
        for (var i = 0; i < lims.Count; i++)
        {
            double r = !two ? 40 : i == 0 ? 42 : 32;
            dial.Children.Add(HalfArc(50, 52, r, 100, 6, Track));
            if (lims[i].Pct is { } p && p > 0) dial.Children.Add(HalfArc(50, 52, r, p, 6, ColorFor(p, src.Stale)));
        }
        if (o.Pct)
        {
            TextAt(dial, Pct(lims[0].Pct), 18, true, NumberColor(src), two ? 45 : 50);
            var small = two ? $"{(o.Labels ? lims[1].Name + " " : "")}{Pct(lims[1].Pct)}" : o.Labels ? lims[0].Name : "";
            if (small != "") TextAt(dial, small, 13, false, Dim, 63);
        }
        if (!o.Rotate) return WithFooter(src, o, dial, icon: false);
        // Rotated: the reset stands left of the dial, as in Target, and the footer keeps only the logo.
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (Rotated(src, o) is { } reset) { row.Children.Add(reset); dial.Margin = new Thickness(4, 0, 0, 0); }
        row.Children.Add(dial);
        return WithFooter(src, o, row, icon: false, reset: false);
    }

    /// Text centred in the dial's box with its baseline at `baseline`, like SVG text.
    static void TextAt(Canvas canvas, string text, double size, bool bold, Brush color, double baseline)
    {
        var label = Label(text, size, color, HorizontalAlignment.Left, bold: bold);
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(Theme.Font, FontStyles.Normal, label.FontWeight, FontStretches.Normal), size, color, 1);
        Canvas.SetLeft(label, (canvas.Width - ft.WidthIncludingTrailingWhitespace) / 2);
        Canvas.SetTop(label, baseline - ft.Baseline);
        canvas.Children.Add(label);
    }

    /// Rings and Gauge: the logo and the reset on one centred line under `top`. A lone ⚠ joins that line; a reason
    /// goes on a line of its own.
    FrameworkElement WithFooter(Source src, LookOptions o, FrameworkElement top, bool icon = true, bool reset = true)
    {
        var col = new StackPanel { Background = Brushes.Transparent };
        top.HorizontalAlignment = HorizontalAlignment.Center;
        col.Children.Add(top);
        var status = StatusPart(src, o);
        var inFoot = status != null && o.Stale == StaleShow.Icon;
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        void Add(FrameworkElement el)
        {
            if (foot.Children.Count > 0) el.Margin = new Thickness(5, 0, 0, 0);
            foot.Children.Add(el);
        }
        if (o.Logo) Add(SourceLogo(src, 13));
        if (reset && ResetPart(src, o, icon) is { } r) Add(r);
        if (inFoot) Add(status!);
        if (foot.Children.Count > 0) col.Children.Add(foot);
        if (status != null && !inFoot) col.Children.Add(Centered(status, top: 2));
        return col;
    }

    // Strip: a filling column per limit, the % on top and the name under; the logo above, the reset and status below.
    FrameworkElement StripBlock(Source src, LookOptions o)
    {
        var col = new StackPanel { Background = Brushes.Transparent };
        if (o.Logo) col.Children.Add(Centered(SourceLogo(src, 14), bottom: 3));
        var bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var l in LimitsOf(src, o))
        {
            var c = new StackPanel { Width = 34 };
            if (o.Pct) c.Children.Add(Label(Pct(l.Pct), 13, NumberColor(src), HorizontalAlignment.Center, bold: true, bottom: 3));
            c.Children.Add(Centered(Bar(l.Pct, 8, 52, horizontal: false, src.Stale)));
            if (o.Labels) c.Children.Add(Label(l.Name, 11, Dim, HorizontalAlignment.Center, top: 2));
            bars.Children.Add(c);
        }
        col.Children.Add(bars);
        var resetPart = ResetPart(src, o);
        if (resetPart != null) col.Children.Add(Centered(resetPart, top: 4));
        if (StatusPart(src, o) is { } status) col.Children.Add(Centered(status, top: resetPart != null ? 0 : 4));
        return col;
    }

    // Numbers: just the %, big, amber and red once high, a hairline bar under each; the reset and status to the right.
    FrameworkElement NumbersBlock(Source src, LookOptions o)
    {
        var block = new DockPanel { Background = Brushes.Transparent };
        if (o.Logo) Dock(block, LogoColumn(src, 20, 15));
        foreach (var l in LimitsOf(src, o))
        {
            var big = new TextBlock
            {
                FontFamily = Theme.Font, FontWeight = FontWeights.SemiBold, LineHeight = 22, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = src.Stale ? Theme.StaleText : l.Pct >= 90 ? Red : l.Pct >= 75 ? Amber : Text,
            };
            big.Inlines.Add(new System.Windows.Documents.Run(l.Pct is { } p ? $"{p:0}" : "—") { FontSize = 20 });
            if (l.Pct != null) big.Inlines.Add(new System.Windows.Documents.Run("%") { FontSize = 12 });
            var num = new StackPanel { Width = 42, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            num.Children.Add(big);
            if (o.Hairline)
            {
                var line = Bar(l.Pct, 38, 2, horizontal: true, src.Stale);
                line.HorizontalAlignment = HorizontalAlignment.Left;
                line.Margin = new Thickness(0, 1, 0, 1);
                num.Children.Add(line);
            }
            if (o.Labels) num.Children.Add(Tight(Label(l.Name, 11, Dim, HorizontalAlignment.Left), 13));
            Dock(block, num);
        }
        // The last child fills the rest of the row, so it sits at the right. A plain reset and the status share one
        // line, so a one-row block stays one row; a rotated reset has the status under it.
        var inline = !o.Rotate;
        var side = new StackPanel
        {
            Orientation = inline ? Orientation.Horizontal : Orientation.Vertical, Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
        };
        if ((o.Rotate ? Rotated(src, o) : ResetPart(src, o)) is { } r) { r.HorizontalAlignment = HorizontalAlignment.Right; side.Children.Add(r); }
        if (StatusPart(src, o) is { } status)
        {
            status.HorizontalAlignment = HorizontalAlignment.Right;
            var gap = side.Children.Count > 0;
            status.Margin = inline ? new Thickness(gap ? 6 : 0, 0, 0, 0) : new Thickness(0, gap ? 1 : 0, 0, 0);
            side.Children.Add(status);
        }
        block.Children.Add(side);
        return block;
    }

    static void Dock(DockPanel panel, UIElement el)
    {
        DockPanel.SetDock(el, System.Windows.Controls.Dock.Left);
        panel.Children.Add(el);
    }

    /// The logo at the left of a row look, in a column of its own so the rows line up.
    static FrameworkElement LogoColumn(Source src, double width, double size)
    {
        var logo = SourceLogo(src, size);
        logo.HorizontalAlignment = HorizontalAlignment.Left;
        return new Border { Width = width, Child = logo, VerticalAlignment = VerticalAlignment.Center };
    }

    /// The time to the Session Reset, or its clock time; null when the set hides it. It stays true while the numbers
    /// are Stale, so it is only dimmed. `icon`: the ↻ in front.
    static FrameworkElement? ResetPart(Source src, LookOptions o, bool icon = true)
    {
        if (o.Reset == ResetShow.Off || src.U == null) return null;
        var color = src.Stale ? Theme.StaleText : Theme.Secondary;
        var text = Label(ResetText(src.U, o), 12, color, HorizontalAlignment.Left);
        if (!icon) return text;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Glyph(Theme.ResetGlyph, 10, color, right: 4));
        row.Children.Add(text);
        return row;
    }

    /// The reset time on its side, read bottom to top, with no ↻ so it fits the height beside it.
    static FrameworkElement? Rotated(Source src, LookOptions o)
    {
        if (o.Reset == ResetShow.Off || src.U == null) return null;
        var text = Tight(Label(ResetText(src.U, o), 11, src.Stale ? Theme.StaleText : Theme.Secondary, HorizontalAlignment.Center), 11);
        text.LayoutTransform = new RotateTransform(-90);
        return text;
    }

    static string ResetText(Usage u, LookOptions o) =>
        o.Reset == ResetShow.ClockTime && u.SessionReset is { } at ? at.ToLocalTime().ToString("HH:mm") : Countdown(u.SessionReset);

    /// Only while Stale: the ⚠, and its short reason unless the set shows the icon alone. The full reason is in the hover card.
    static FrameworkElement? StatusPart(Source src, LookOptions o)
    {
        if (src.Err is not { } err) return null;
        var color = NeedsAction(err) ? Theme.Critical : Theme.Tertiary;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Glyph(Theme.WarningGlyph, 10, color));
        if (o.Stale == StaleShow.Reason) row.Children.Add(Label(Short(err), 11, color, HorizontalAlignment.Left, left: 4));
        return row;
    }

    static Brush NumberColor(Source src) => src.Stale ? Theme.StaleText : Text;

    static T Fixed<T>(T el, double width) where T : FrameworkElement
    {
        el.Width = width;
        return el;
    }

    /// A line exactly `height` tall, so small text sits as close as it does on the proposal page.
    static TextBlock Tight(TextBlock text, double height)
    {
        text.LineHeight = height;
        text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        return text;
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

    /// Hover a source for a card with both limits, both Resets, and why and since when it is Stale, whatever the look
    /// hides. It opens above the source, since the widget sits on the taskbar.
    FrameworkElement Tip(FrameworkElement el, Source src)
    {
        var tip = new ToolTip { Style = (Style)FindResource("CardTip"), Content = Card(src) };
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
