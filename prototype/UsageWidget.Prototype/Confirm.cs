// PROTOTYPE: throwaway. A yes/no question in the widget's style, instead of the Windows message box.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UsageWidget.Prototype;

static class Confirm
{
    /// Opens just above `owner`, right edges lined up. "Keep it" is the default, for Enter and Esc alike.
    public static bool Ask(Window owner, string title, string text, string yes)
    {
        var window = Make(owner, title, text, yes);
        window.Loaded += (_, _) =>
        {
            // The margin around the card is shadow, so the card itself lines up with the widget.
            window.Left = Math.Max(SystemParameters.VirtualScreenLeft, owner.Left + owner.ActualWidth - window.ActualWidth + 10);
            window.Top = owner.Top - window.ActualHeight + 6;
            if (window.Top < SystemParameters.VirtualScreenTop) window.Top = owner.Top + owner.ActualHeight;
        };
        return window.ShowDialog() == true;
    }

    /// The window, not shown yet; QA draws its card.
    internal static Window Make(Window? owner, string title, string text, string yes)
    {
        var keep = new Button { Content = "Keep it", IsDefault = true, IsCancel = true };
        var go = new Button { Content = yes, Background = Theme.Red, Foreground = Brushes.White, Margin = new Thickness(8, 0, 0, 0) };
        // Close's own red Background wins over the style's grey.
        foreach (var b in new[] { keep, go }) b.Style = (Style)Application.Current.FindResource("DialogButton");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(keep);
        buttons.Children.Add(go);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Theme.Primary });
        body.Children.Add(new TextBlock
        {
            Text = text, FontSize = 12, Foreground = Theme.Secondary, TextWrapping = TextWrapping.Wrap,
            LineHeight = 17, Margin = new Thickness(0, 4, 0, 0),
        });
        body.Children.Add(buttons);

        var window = new Window
        {
            Owner = owner, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, Topmost = true,
            Title = title, FontFamily = Theme.Font,
            Content = new Border
            {
                Width = 300, Margin = new Thickness(10, 6, 10, 14), Padding = new Thickness(14, 12, 14, 12),
                Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = body,
                Effect = (System.Windows.Media.Effects.Effect)Application.Current.FindResource("Shadow"),
            },
        };
        window.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) window.DragMove(); };
        go.Click += (_, _) => window.DialogResult = true;
        return window;
    }
}
