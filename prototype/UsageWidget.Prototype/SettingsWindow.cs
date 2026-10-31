// PROTOTYPE: throwaway. Every change applies at once and is saved; there is no OK button.
// Dark and borderless like the widget: its own title bar, segmented choices, switches and sliders (styles in Theme).
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace UsageWidget.Prototype;

class SettingsWindow : Window
{
    const double LabelWidth = 140, ControlWidth = 228;

    readonly Settings s;
    readonly Action apply;
    readonly StackPanel panel = new() { Margin = new Thickness(20, 4, 20, 14) };
    /// The parts of the current look and set, rebuilt when either changes.
    readonly StackPanel options = new();
    // Where Row, Switch and Line add to: the panel, or the options while they are built.
    StackPanel target;

    public SettingsWindow(Settings settings, Action apply, Action resetPosition)
    {
        s = settings;
        this.apply = apply;
        target = panel;
        Title = "Coding Agent Usage Meter settings";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var title = new DockPanel { Margin = new Thickness(20, 10, 8, 6), Background = Brushes.Transparent };
        var close = new Button { Style = Style("WindowClose"), ToolTip = "Close" };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        title.Children.Add(close);
        title.Children.Add(new TextBlock { Text = "Settings", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.MouseLeftButtonDown += (_, _) => DragMove();

        var body = new StackPanel();
        body.Children.Add(title);
        body.Children.Add(panel);
        var card = new Border
        {
            Margin = new Thickness(16), Background = Theme.CardBg, BorderBrush = Theme.Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Child = body, Effect = (System.Windows.Media.Effects.Effect)Application.Current.Resources["Shadow"],
        };
        TextElement.SetFontFamily(card, Theme.Font);
        TextElement.SetForeground(card, Theme.Primary);
        Content = card;

        Row("Look", Segments(PrototypeWindow.LookNames, s.Look, i => { s.Look = i; BuildOptions(); }, columns: 3));
        Switch("Compact", s.Compact, v => { s.Compact = v; BuildOptions(); });
        panel.Children.Add(options);
        BuildOptions();
        Line();
        Row("Tray icon", Segments(["Bars", "Rings"], s.TrayStyle, i => s.TrayStyle = i));
        Row("Size", Percent(s.Scale, 50, 300, v => s.Scale = v));
        Row("Opacity when idle", Percent(s.IdleOpacity, 30, 100, v => s.IdleOpacity = v));
        int[] minutes = [1, 2, 5, 10, 15, 30];
        Row("Update every", Segments(minutes.Select(m => $"{m}m").ToArray(), Math.Max(0, Array.IndexOf(minutes, s.UpdateMinutes)),
            i => s.UpdateMinutes = minutes[i]));

        Line();
        Switch("Always on top", s.Topmost, v => s.Topmost = v);
        if (CodexUsageClient.Installed) Switch("Show Codex", s.ShowCodex, v => s.ShowCodex = v, icon: "codex");
        Switch("Start with Windows", StartWithWindows.IsOn, StartWithWindows.Set, save: false);

        Line();
        var reset = new Button { Content = "Reset position", Style = Style("Link"), ToolTip = "Put the widget back above the clock", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 0, 0, 0) };
        reset.Click += (_, _) => resetPosition();
        var file = new Button { Content = "Open settings file", Style = Style("Link"), ToolTip = Settings.FilePath, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 2, 0, 0) };
        file.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Settings.FilePath) { UseShellExecute = true }); } catch { }
        };
        panel.Children.Add(reset);
        panel.Children.Add(file);
        panel.Children.Add(new TextBlock
        {
            Text = "Ctrl + mouse wheel on the widget also resizes it.", Margin = new Thickness(0, 6, 0, 0),
            FontSize = 12, Foreground = Theme.Tertiary,
        });
    }

    /// QA only (--qa): what the window shows, on a copy of the settings.
    internal static FrameworkElement QaContent()
    {
        var w = new SettingsWindow(Settings.Load(), () => { }, () => { });
        var content = (FrameworkElement)w.Content;
        w.Content = null;
        return content;
    }

    /// What the current look shows in its Full or Compact set: only the parts that apply to that look.
    void BuildOptions()
    {
        options.Children.Clear();
        target = options;
        var look = Math.Clamp(s.Look, 0, LookOptions.Names.Length - 1);
        var o = s.Options(look);
        var head = new DockPanel { Margin = new Thickness(0, 10, 0, 2) };
        var back = new Button { Content = "Back to suggested", Style = Style("Link"), ToolTip = "The parts this set shows at first", Margin = new Thickness(0, 0, -8, 0) };
        back.Click += (_, _) => Changed(() => { s.ResetOptions(look); BuildOptions(); });
        DockPanel.SetDock(back, Dock.Right);
        head.Children.Add(back);
        head.Children.Add(new TextBlock
        {
            Text = $"{LookOptions.Names[look]} · {(s.Compact ? "Compact" : "Full")}", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Secondary, VerticalAlignment = VerticalAlignment.Center,
        });
        options.Children.Add(head);
        foreach (var part in LookOptions.Shown(look))
            switch (part)
            {
                case "Limits": Row("Limits", Segments(["5h and 7d", "5h only", "Higher one"], (int)o.Limits, i => o.Limits = (Limits)i)); break;
                case "Arrange": Row("Agents", Segments(["Stacked", "Side by side"], (int)o.Arrange, i => o.Arrange = (Arrangement)i)); break;
                case "Logo": Switch("Logo", o.Logo, v => o.Logo = v); break;
                case "Pct": Switch("Percent", o.Pct, v => o.Pct = v); break;
                case "Labels": Switch("5h / 7d labels", o.Labels, v => o.Labels = v); break;
                case "BarStyle": Row("Bar style", Segments(["Smooth", "Blocks"], (int)o.BarStyle, i => o.BarStyle = (BarStyle)i)); break;
                case "BarLength": Row("Bar length", Segments(["Short", "Long"], (int)o.BarLength, i => o.BarLength = (BarLength)i)); break;
                case "Tick": Switch("Pace tick", o.Tick, v => o.Tick = v); break;
                case "PaceWords": Switch("\"fast\" / \"on pace\"", o.PaceWords, v => o.PaceWords = v); break;
                case "Hairline": Switch("Hairline bar", o.Hairline, v => o.Hairline = v); break;
                case "Reset": Row("Reset", Segments(["Off", "Time left", "Clock time"], (int)o.Reset, i => o.Reset = (ResetShow)i)); break;
                case "Rotate": Switch("Rotate reset time", o.Rotate, v => o.Rotate = v); break;
                case "Updated": Switch("\"Updated\" time", o.Updated, v => o.Updated = v); break;
                case "Stale": Row("When stale", Segments(["Reason", "Icon only"], (int)o.Stale, i => o.Stale = (StaleShow)i)); break;
            }
        target = panel;
    }

    static new Style Style(string key) => (Style)Application.Current.Resources[key];

    void Changed(Action set)
    {
        set();
        s.Save();
        apply();
    }

    void Row(string label, FrameworkElement control)
    {
        // A control taller than one row (the Look grid) gets its label at the top, in line with its first row.
        var tall = control.Tag is "tall";
        var row = new DockPanel { MinHeight = 40, Margin = new Thickness(0, tall ? 6 : 0, 0, tall ? 6 : 0) };
        row.Children.Add(new TextBlock
        {
            Text = label, Width = LabelWidth, FontSize = 13, Margin = new Thickness(0, tall ? 7 : 0, 0, 0),
            VerticalAlignment = tall ? VerticalAlignment.Top : VerticalAlignment.Center,
        });
        control.Width = ControlWidth;
        control.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(control);
        target.Children.Add(row);
    }

    void Line() => target.Children.Add(new Border { Height = 1, Background = Theme.Divider, Margin = new Thickness(0, 8, 0, 8) });

    /// One choice of several, as segments on a faint track; more than `columns` wrap onto further rows.
    FrameworkElement Segments(string[] names, int selected, Action<int> set, int columns = 0)
    {
        if (columns <= 0) columns = names.Length;
        var grid = new Grid();
        var group = Guid.NewGuid().ToString();
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var r = 0; r < (names.Length + columns - 1) / columns; r++) grid.RowDefinitions.Add(new RowDefinition());
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var b = new RadioButton { Content = names[i], GroupName = group, IsChecked = i == selected, Style = Style("Segment") };
            b.Checked += (_, _) => Changed(() => set(index));
            Grid.SetColumn(b, i % columns);
            Grid.SetRow(b, i / columns);
            grid.Children.Add(b);
        }
        return new Border
        {
            Background = Theme.Divider, CornerRadius = new CornerRadius(6), Padding = new Thickness(2), Child = grid,
            Tag = names.Length > columns ? "tall" : null,
        };
    }

    FrameworkElement Percent(double value, int min, int max, Action<double> set)
    {
        var text = new TextBlock { Width = 44, FontSize = 12, Foreground = Theme.Secondary, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value * 100, TickFrequency = 10, IsSnapToTickEnabled = true, Style = Style("Fluent") };
        text.Text = $"{slider.Value:0}%";
        slider.ValueChanged += (_, e) =>
        {
            text.Text = $"{e.NewValue:0}%";
            Changed(() => set(e.NewValue / 100));
        };
        var row = new DockPanel();
        DockPanel.SetDock(text, Dock.Right);
        row.Children.Add(text);
        row.Children.Add(slider);
        return row;
    }

    /// `icon`: a logo from Icons/ in front of the label, like the widget marks that source.
    void Switch(string label, bool value, Action<bool> set, bool save = true, string? icon = null)
    {
        object content = label;
        if (icon != null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var logo = PrototypeWindow.Logo(icon, 16);
            logo.Margin = new Thickness(0, 1, 8, 0);
            row.Children.Add(logo);
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            content = row;
        }
        var box = new CheckBox { Content = content, IsChecked = value, Style = Style("Switch") };
        box.Click += (_, _) =>
        {
            if (save) Changed(() => set(box.IsChecked == true));
            else set(box.IsChecked == true);
        };
        target.Children.Add(box);
    }
}
