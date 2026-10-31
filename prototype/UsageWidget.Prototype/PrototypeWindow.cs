// PROTOTYPE: throwaway. No tray or hotkey. Settings (look, size, position...) live in Settings.cs.
using System.Diagnostics;
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
    readonly Settings settings = Settings.Load();
    readonly DispatcherTimer updateTimer;
    TaskbarDock? dock;
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

    public PrototypeWindow(bool startDocked)
    {
        variants = [("A", "Rings", Rings), ("B", "Pill", Pill), ("C", "Strip", Strip)];

        Title = "PROTOTYPE Coding Agent Usage Meter";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        current = Math.Clamp(settings.Look, 0, variants.Length - 1);
        if (startDocked) settings.Docked = true;
        Topmost = settings.Topmost;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = settings.IdleOpacity;

        // Stay just above the clock until the user drags it; after that, only keep it on screen.
        // (Keeping the right edge put by the change in width pushed it off screen after Docked mode.)
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
            if (settings.Docked) Dock();
            await UpdateUsage();
        };

        updateTimer = new DispatcherTimer(TimeSpan.FromMinutes(settings.UpdateMinutes), DispatcherPriority.Normal, async (_, _) => await UpdateUsage(), Dispatcher);
        new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Render(), Dispatcher);
        new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Normal, (_, _) => UpdateServers(), Dispatcher);
    }

    void Dock()
    {
        if (dock != null) return;
        settings.Docked = true;
        settings.Save();
        Hide();
        dock = new TaskbarDock(Undock);
        Render();
    }

    void Undock()
    {
        if (dock == null) return;
        settings.Docked = false;
        settings.Save();
        dock.Dispose();
        dock = null;
        Show();
        RestorePosition();
    }

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
        if (settings.Docked) Dock(); else Undock();
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

    /// Just above the clock. (Top right sat on app window buttons and got lost.)
    void PlaceBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = area.Bottom - ActualHeight - 12;
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
        var textOnTop = current == 1 || (current == 2 && !ShowCodex);
        frame.Padding = settings.Compact ? new Thickness(8, textOnTop ? 4.5 : 7.5, 8, 6) : new Thickness(14, 12, 14, 12);
        frame.CornerRadius = new CornerRadius(Gap(10, 8));
        if (frame.LayoutTransform is not ScaleTransform s || s.ScaleX != settings.Scale)
            frame.LayoutTransform = new ScaleTransform(settings.Scale, settings.Scale);

        dock?.Render(last, error, ShowCodex ? codexLast : null);
    }

    /// Codex only when it is on this machine and the user hasn't turned it off.
    bool ShowCodex => qaCodex ?? (settings.ShowCodex && CodexUsageClient.Installed);
    bool? qaCodex;

    /// QA only (--qa): the card with made-up Usage, taken out of the window so it can be drawn to a file.
    internal FrameworkElement QaCard(int look, bool compact, bool codex, (Usage?, DateTime?, string?) claude, (Usage?, DateTime?, string?) cx)
    {
        Content = null;
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

    /// Rebuilds the Dev servers list only when it changed; hidden when there are none.
    void UpdateServers()
    {
        var servers = DevServers.Scan();
        var key = string.Join(",", servers.Select(s => $"{s.Pid}:{s.Port}"));
        if (key == serversKey) return;
        serversKey = key;

        serversPanel.Children.Clear();
        if (servers.Count == 0) return;
        serversPanel.Children.Add(new Border { Height = 1, Background = Edge, Margin = new Thickness(0, 8, 0, 6) });
        foreach (var server in servers)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            var close = new Button
            {
                Content = "Close", FontSize = 10, Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(8, 0, 0, 0),
                Foreground = Red, Background = Brushes.Transparent, BorderBrush = Red, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, ToolTip = $"Stop {server.Name} (PID {server.Pid}) and everything it started",
            };
            close.Click += (_, _) => ConfirmClose(server);
            DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
            row.Children.Add(close);
            row.Children.Add(Link(server));
            var project = Label(server.Project, 11, Text, HorizontalAlignment.Left, left: 8);
            project.TextTrimming = TextTrimming.CharacterEllipsis;
            project.MaxWidth = 160;
            row.Children.Add(project);
            row.ToolTip =
                $"{server.Project}\n{server.Folder ?? "folder unknown"}\n\n" +
                $"{server.Name} · PID {server.Pid}{(server.LoopbackOnly ? "" : " · reachable from the network")}" +
                (server.CommandLine is { } cmd ? $"\n{cmd}" : "");
            serversPanel.Children.Add(row);
        }
    }

    /// `localhost:port`, opening it in the browser. A Button, so the click doesn't start a window drag.
    UIElement Link(DevServer server)
    {
        var text = new FrameworkElementFactory(typeof(ContentPresenter));
        var link = new Button
        {
            Content = $"● {server.Url}", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Normal,
            Cursor = Cursors.Hand, Template = new ControlTemplate(typeof(Button)) { VisualTree = text },
            VerticalAlignment = VerticalAlignment.Center,
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
        var answer = MessageBox.Show(this,
            $"Are you sure you want to close {server.Project} on {server.Url}?\n\n" +
            $"This stops {server.Name} (PID {server.Pid}) and every process it started. Unsaved work in it is lost.",
            "Close dev server", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
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
        Item("Dock to taskbar", Dock);
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
        const double stroke = 4;
        double r = (size - stroke) / 2, c = size / 2;
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Width = size, Height = size, Stroke = Track, StrokeThickness = stroke });
        if (pct is { } p && p >= 99.9)
            grid.Children.Add(new Ellipse { Width = size, Height = size, Stroke = ColorFor(p, stale), StrokeThickness = stroke });
        else if (pct is { } q && q > 0)
        {
            var a = q / 100 * 2 * Math.PI;
            var fig = new PathFigure { StartPoint = new Point(c, c - r) };
            fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0,
                a > Math.PI, SweepDirection.Clockwise, true));
            grid.Children.Add(new Path
            {
                Data = new PathGeometry([fig]), Stroke = ColorFor(q, stale), StrokeThickness = stroke,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
        }
        grid.Children.Add(Label(Pct(pct), 12, stale ? Theme.StaleText : Text, HorizontalAlignment.Center, bold: true));

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(Label(name, 11, Dim, HorizontalAlignment.Center, top: Gap(4, 1.5)));
        return stack;
    }

    // B: per source, two rows of name · % · bar, with the Reset and the status on the right.
    UIElement Pill()
    {
        var stack = new StackPanel();
        foreach (var src in Sources())
        {
            if (stack.Children.Count > 0)
                stack.Children.Add(new Border { Height = 1, Background = Theme.Divider, Margin = new Thickness(0, Gap(8, 5.5), 0, Gap(8, 2.5)) });
            var grid = new Grid { Background = Brushes.Transparent };
            foreach (var w in new[] { ShowCodex ? Gap(28, 22) : 0, Gap(22, 20), Gap(46, 40), Gap(100, 80), 84 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Gap(22, 18)) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Gap(22, 18)) });
            void Put(UIElement el, int row, int col, int rowSpan = 1)
            {
                Grid.SetRow(el, row); Grid.SetColumn(el, col); Grid.SetRowSpan(el, rowSpan);
                grid.Children.Add(el);
            }
            if (ShowCodex)
            {
                var logo = SourceLogo(src, 16);
                logo.Margin = new Thickness(0, 1, 0, 0);
                logo.HorizontalAlignment = HorizontalAlignment.Left;
                Put(logo, 0, 0, rowSpan: 2);
            }
            void Limit(int r, string name, double? pct)
            {
                Put(Label(name, 12, Dim, HorizontalAlignment.Left), r, 1);
                Put(Label(Pct(pct), 14, src.Stale ? Theme.StaleText : Text, HorizontalAlignment.Right, bold: true, right: Gap(10, 6)), r, 2);
                Put(Bar(pct, Gap(88, 72), 4, horizontal: true, src.Stale), r, 3);
            }
            Limit(0, "5h", src.U?.SessionPct);
            Limit(1, "7d", src.U?.WeeklyPct);
            var reset = ResetIn(src);
            reset.HorizontalAlignment = HorizontalAlignment.Right;
            Put(reset, 0, 4);
            var status = Status(src);
            status.HorizontalAlignment = HorizontalAlignment.Right;
            Put(status, 1, 4);
            stack.Children.Add(Tip(grid, src));
        }
        return stack;
    }

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

    /// Hover a source for its full numbers, and why and since when it is Stale.
    static FrameworkElement Tip(FrameworkElement el, Source src)
    {
        var u = src.U;
        el.ToolTip = $"{src.Name}\n" +
            (u == null ? "No Usage yet" :
                $"Session limit {u.SessionPct:0}%, resets in {Countdown(u.SessionReset)}\n" +
                $"Weekly limit {u.WeeklyPct:0}%, resets {u.WeeklyReset?.ToLocalTime().ToString("ddd HH:mm") ?? "—"}") +
            (src.Err == null ? "" : $"\n\n{src.Err}." +
                (u != null ? $" Stale: these are the numbers from {Time(src.Ok)}." : ""));
        return el;
    }

    static bool NeedsAction(string err) => err.Contains("sign in", StringComparison.OrdinalIgnoreCase);

    /// The part before a colon is the short reason; the rest is in the tooltip.
    static string Short(string err)
    {
        if (NeedsAction(err)) return "Sign in";
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
