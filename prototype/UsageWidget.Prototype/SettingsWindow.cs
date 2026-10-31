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

    public SettingsWindow(Settings settings, Action apply, Action resetPosition)
    {
        s = settings;
        this.apply = apply;
        Title = "Claude Gauge settings";
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

        Row("Look", Segments(["Rings", "Pill", "Strip"], s.Look, i => s.Look = i));
        Row("Size", Percent(s.Scale, 50, 300, v => s.Scale = v));
        Row("Opacity when idle", Percent(s.IdleOpacity, 30, 100, v => s.IdleOpacity = v));
        int[] minutes = [1, 2, 5, 10, 15, 30];
        Row("Update every", Segments(minutes.Select(m => $"{m}m").ToArray(), Math.Max(0, Array.IndexOf(minutes, s.UpdateMinutes)),
            i => s.UpdateMinutes = minutes[i]));

        Line();
        Switch("Always on top", s.Topmost, v => s.Topmost = v);
        Switch("Docked in the taskbar", s.Docked, v => s.Docked = v);
        if (CodexUsageClient.Installed) Switch("Show Codex", s.ShowCodex, v => s.ShowCodex = v);
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

    static new Style Style(string key) => (Style)Application.Current.Resources[key];

    void Changed(Action set)
    {
        set();
        s.Save();
        apply();
    }

    void Row(string label, FrameworkElement control)
    {
        var row = new DockPanel { Height = 40 };
        row.Children.Add(new TextBlock { Text = label, Width = LabelWidth, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        control.Width = ControlWidth;
        control.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(control);
        panel.Children.Add(row);
    }

    void Line() => panel.Children.Add(new Border { Height = 1, Background = Theme.Divider, Margin = new Thickness(0, 8, 0, 8) });

    /// One choice of several, as a row of segments on a faint track.
    FrameworkElement Segments(string[] names, int selected, Action<int> set)
    {
        var grid = new Grid();
        var group = Guid.NewGuid().ToString();
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var b = new RadioButton { Content = names[i], GroupName = group, IsChecked = i == selected, Style = Style("Segment") };
            b.Checked += (_, _) => Changed(() => set(index));
            Grid.SetColumn(b, i);
            grid.Children.Add(b);
        }
        return new Border
        {
            Background = Theme.Divider, CornerRadius = new CornerRadius(6), Padding = new Thickness(2), Child = grid,
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

    void Switch(string label, bool value, Action<bool> set, bool save = true)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Style = Style("Switch") };
        box.Click += (_, _) =>
        {
            if (save) Changed(() => set(box.IsChecked == true));
            else set(box.IsChecked == true);
        };
        panel.Children.Add(box);
    }
}
