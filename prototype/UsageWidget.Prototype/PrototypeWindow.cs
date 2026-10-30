// PROTOTYPE: throwaway. No tray or hotkey. Settings (look, size, position...) live in Settings.cs.
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace UsageWidget.Prototype;

class PrototypeWindow : Window
{
    static readonly Brush Normal = Hex("#4FC3F7"), Amber = Hex("#FFB300"), Red = Hex("#EF5350"),
        Stale = Hex("#757575"), Track = Hex("#33FFFFFF"), Text = Hex("#F5F5F5"), Dim = Hex("#B0B0B0"),
        CardBg = Hex("#1E1E1E"), Edge = Hex("#40FFFFFF"), LimitName = Hex("#9FA8DA"), CodexName = Hex("#80CBC4");

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

        Title = "PROTOTYPE Claude usage widget";
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
            Background = CardBg, BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Child = body,
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
        if (v.Key != "B") // Pill shows the Update time itself
        {
            stack.Children.Add(Label($"{(ShowCodex ? "Claude " : "")}Updated {Time(lastOk)}{(error != null ? $" · ⚠ {error}" : "")}",
                9, error != null ? Amber : Dim, HorizontalAlignment.Center, top: 4));
            if (ShowCodex)
                stack.Children.Add(Label($"Codex Updated {Time(codexOk)}{(codexError != null ? $" · ⚠ {codexError}" : "")}",
                    9, codexError != null ? Amber : Dim, HorizontalAlignment.Center));
        }
        gauge.Content = stack;
        if (frame.LayoutTransform is not ScaleTransform s || s.ScaleX != settings.Scale)
            frame.LayoutTransform = new ScaleTransform(settings.Scale, settings.Scale);

        dock?.Render(last, error, ShowCodex ? codexLast : null);
    }

    /// Codex only when it is on this machine and the user hasn't turned it off.
    bool ShowCodex => settings.ShowCodex && CodexUsageClient.Installed;

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
            "Claude Gauge", MessageBoxButton.OK, MessageBoxImage.Information);
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
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            var item = new MenuItem { Header = $"Variant {"ABC"[i]}" };
            item.Click += (_, _) => Switch(index);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var dockItem = new MenuItem { Header = "Dock to taskbar" };
        dockItem.Click += (_, _) => Dock();
        menu.Items.Add(dockItem);
        menu.Items.Add(new Separator());
        var settingsItem = new MenuItem { Header = "Settings…" };
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);
        var help = new MenuItem { Header = "Help…" };
        help.Click += (_, _) => ShowHelp();
        menu.Items.Add(help);
        var update = new MenuItem { Header = "Update now" };
        update.Click += async (_, _) => await UpdateUsage();
        menu.Items.Add(update);
        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quit);
        return menu;
    }

    // A: two ring gauges side by side, Session countdown underneath.
    // With Codex: a second pair of rings beside Claude's, each pair named above.
    UIElement Rings()
    {
        UIElement Pair(string? title, Usage? u, bool stale)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            row.Children.Add(Ring(u?.SessionPct, "5h", stale));
            row.Children.Add(new Border { Width = 10 });
            row.Children.Add(Ring(u?.WeeklyPct, "7d", stale));
            var pair = new StackPanel();
            if (title != null) pair.Children.Add(Label(title, 10, title == "Codex" ? CodexName : LimitName, HorizontalAlignment.Center, bold: true, bottom: 2));
            pair.Children.Add(row);
            pair.Children.Add(Label($"resets {(u == null ? "—" : Countdown(u.SessionReset))}", 10, Dim, HorizontalAlignment.Center, top: 4));
            return pair;
        }
        if (!ShowCodex) return Pair(null, last, error != null);
        var pairs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        pairs.Children.Add(Pair("Claude", last, error != null));
        pairs.Children.Add(new Border { Width = 1, Background = Edge, Margin = new Thickness(10, 0, 10, 0) });
        pairs.Children.Add(Pair("Codex", codexLast, codexError != null));
        return pairs;
    }

    UIElement Ring(double? pct, string name, bool stale, double size = 46)
    {
        const double stroke = 5;
        double r = (size - stroke) / 2, c = size / 2;
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Width = size - stroke, Height = size - stroke, Stroke = Track, StrokeThickness = stroke });
        if (pct is { } p && p >= 99.9)
            grid.Children.Add(new Ellipse { Width = size - stroke, Height = size - stroke, Stroke = ColorFor(p, stale), StrokeThickness = stroke });
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
        var center = Label(Pct(pct), 12, Text, HorizontalAlignment.Center);
        center.VerticalAlignment = VerticalAlignment.Center;
        center.FontWeight = FontWeights.SemiBold;
        grid.Children.Add(center);

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(Label(name, 10, Dim, HorizontalAlignment.Center, top: 2));
        return stack;
    }

    // B: one horizontal line of text and thin bars, countdown at the end.
    // B: the two limits stacked, each name · % · bar; Session countdown and the Update time on the right.
    // With Codex: its two rows under a thin line, the names in teal and "Codex" before its countdown.
    UIElement Pill()
    {
        var grid = new Grid();
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        void Put(UIElement el, int row, int col, int span = 1)
        {
            Grid.SetRow(el, row); Grid.SetColumn(el, col); Grid.SetColumnSpan(el, span); grid.Children.Add(el);
        }
        void Pair(int row, string? title, Brush nameColor, Usage? u, DateTime? ok, string? err)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            void Limit(int r, string name, double? pct)
            {
                Put(Label(name, 12, nameColor, HorizontalAlignment.Left, bold: true, right: 8, top: 2, bottom: 2), r, 0);
                Put(Label(Pct(pct), 13, Text, HorizontalAlignment.Right, bold: true, right: 6), r, 1);
                var bar = (FrameworkElement)Bar(pct, 56, 4, horizontal: true, stale: err != null);
                bar.VerticalAlignment = VerticalAlignment.Center;
                Put(bar, r, 2);
            }
            Limit(row, "5h", u?.SessionPct);
            Limit(row + 1, "7d", u?.WeeklyPct);
            Put(Label($"{(title != null ? title + " " : "")}↻ {(u == null ? "—" : Countdown(u.SessionReset))}",
                12, Text, HorizontalAlignment.Right, left: 14), row, 3);
            Put(Label($"Updated {Time(ok)}{(err != null ? " ⚠" : "")}",
                7.5, err != null ? Amber : Dim, HorizontalAlignment.Right, left: 14), row + 1, 3);
        }
        Pair(0, ShowCodex ? "Claude" : null, LimitName, last, lastOk, error);
        if (ShowCodex)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            Put(new Border { Height = 1, Background = Edge, Margin = new Thickness(0, 4, 0, 4) }, 2, 0, span: 4);
            Pair(3, "Codex", CodexName, codexLast, codexOk, codexError);
        }
        return grid;
    }

    // C: a narrow vertical strip with two filling columns, like batteries. With Codex: two more, named in teal.
    UIElement Strip()
    {
        var cols = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        void Col(string name, double? pct, Brush nameColor, bool stale)
        {
            var col = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
            col.Children.Add(Label(Pct(pct), 10, Text, HorizontalAlignment.Center, bold: true, bottom: 2));
            var bar = Bar(pct, 12, 64, horizontal: false, stale);
            ((FrameworkElement)bar).HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(bar);
            col.Children.Add(Label(name, 10, nameColor, HorizontalAlignment.Center, top: 2));
            cols.Children.Add(col);
        }
        Col("5h", last?.SessionPct, Dim, error != null);
        Col("7d", last?.WeeklyPct, Dim, error != null);
        var countdown = SessionCountdown();
        if (ShowCodex)
        {
            cols.Children.Add(new Border { Width = 1, Background = Edge, Margin = new Thickness(4, 0, 4, 0) });
            Col("5h", codexLast?.SessionPct, CodexName, codexError != null);
            Col("7d", codexLast?.WeeklyPct, CodexName, codexError != null);
            countdown += $" · {(codexLast == null ? "—" : Countdown(codexLast.SessionReset))}";
        }
        var stack = new StackPanel();
        stack.Children.Add(cols);
        stack.Children.Add(Label(countdown, 9, Dim, HorizontalAlignment.Center, top: 4));
        return stack;
    }

    UIElement Bar(double? pct, double w, double h, bool horizontal, bool stale)
    {
        var fill = Math.Clamp(pct ?? 0, 0, 100) / 100;
        var track = new Grid { Width = w, Height = h, Background = Track, ClipToBounds = true };
        track.Children.Add(new Border
        {
            Background = pct is { } p ? ColorFor(p, stale) : Stale,
            Width = horizontal ? w * fill : w,
            Height = horizontal ? h : h * fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Bottom,
        });
        return new Border { CornerRadius = new CornerRadius(2), Child = track };
    }

    static Brush ColorFor(double pct, bool stale) => stale ? Stale : pct >= 90 ? Red : pct >= 75 ? Amber : Normal;

    static string Time(DateTime? at) => at is { } t ? t.ToString("HH:mm") : "never";

    string SessionCountdown() => last == null ? "—" : Countdown(last.SessionReset);

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
        Margin = new Thickness(left, top, right, bottom), FontFamily = new FontFamily("Segoe UI"),
    };

    static SolidColorBrush Hex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
