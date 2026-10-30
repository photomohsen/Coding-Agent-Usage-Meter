// PROTOTYPE: throwaway. Every change applies at once and is saved; there is no OK button.
using System.Windows;
using System.Windows.Controls;

namespace UsageWidget.Prototype;

class SettingsWindow : Window
{
    readonly Settings s;
    readonly Action apply;
    readonly StackPanel panel = new() { Margin = new Thickness(16) };

    public SettingsWindow(Settings settings, Action apply, Action resetPosition)
    {
        s = settings;
        this.apply = apply;
        Title = "Claude Gauge settings";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Content = panel;

        var look = new ComboBox { ItemsSource = new[] { "A · Rings", "B · Pill", "C · Strip" }, SelectedIndex = s.Look };
        look.SelectionChanged += (_, _) => Changed(() => s.Look = look.SelectedIndex);
        Row("Look", look);

        Row("Size", Percent(s.Scale, 50, 300, v => s.Scale = v));
        Row("See-through when idle", Percent(s.IdleOpacity, 30, 100, v => s.IdleOpacity = v));

        var minutes = new[] { 1, 2, 5, 10, 15, 30 };
        var every = new ComboBox
        {
            ItemsSource = minutes.Select(m => $"{m} min"),
            SelectedIndex = Math.Max(0, Array.IndexOf(minutes, s.UpdateMinutes)),
        };
        every.SelectionChanged += (_, _) => Changed(() => s.UpdateMinutes = minutes[every.SelectedIndex]);
        Row("Update every", every);

        Check("Always on top", s.Topmost, v => s.Topmost = v);
        Check("Docked in the taskbar", s.Docked, v => s.Docked = v);
        if (CodexUsageClient.Installed) Check("Show Codex", s.ShowCodex, v => s.ShowCodex = v);
        Check("Start with Windows", StartWithWindows.IsOn, StartWithWindows.Set, save: false);

        var reset = new Button { Content = "Put it back above the clock", Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
        reset.Click += (_, _) => resetPosition();
        panel.Children.Add(reset);

        panel.Children.Add(new TextBlock
        {
            Text = $"Saved in {Settings.FilePath}\nTip: Ctrl + mouse wheel on the widget also resizes it.",
            Margin = new Thickness(0, 12, 0, 0), FontSize = 11, Foreground = System.Windows.Media.Brushes.Gray,
        });
    }

    void Changed(Action set)
    {
        set();
        s.Save();
        apply();
    }

    void Row(string label, FrameworkElement control)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center });
        control.Width = 180;
        row.Children.Add(control);
        panel.Children.Add(row);
    }

    FrameworkElement Percent(double value, int min, int max, Action<double> set)
    {
        var text = new TextBlock { Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value * 100, TickFrequency = 10, IsSnapToTickEnabled = true, Width = 140 };
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

    void Check(string label, bool value, Action<bool> set, bool save = true)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
        box.Click += (_, _) =>
        {
            if (save) Changed(() => set(box.IsChecked == true));
            else set(box.IsChecked == true);
        };
        panel.Children.Add(box);
    }
}
