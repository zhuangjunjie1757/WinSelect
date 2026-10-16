using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace WinSelect
{
    // A compact WPF flyout avoids the legacy WinForms menu chrome, image gutter,
    // thick border and tiny system menu items. Positioning uses physical pixels.
    public sealed class TrayMenuWindow : Window
    {
        const double LogicalWidth = 272;
        readonly double logicalHeight;
        readonly MonitorArea monitor;
        readonly PointL anchor;
        bool closing;
        public TrayMenuWindow(MonitorArea screen, PointL point, BitmapSource icon, string hotkey, Action show, Action settings, Action quit)
        {
            monitor = screen; anchor = point;
            Title = "WinSelect 托盘菜单"; Width = LogicalWidth;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
            Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true;
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"); UseLayoutRounding = true;
            Border surface = new Border { Margin = new Thickness(8), Padding = new Thickness(7), Background = Theme.Brush("#FBFCFE"), BorderBrush = Theme.Brush("#E1E6ED"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = .17, Color = Color.FromRgb(18, 30, 48) } };
            StackPanel content = new StackPanel();
            DockPanel heading = new DockPanel { Margin = new Thickness(9, 7, 8, 11), Height = 32 };
            Image brand = new Image { Source = icon, Width = 29, Height = 29, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }; heading.Children.Add(brand);
            StackPanel name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(Theme.Label("WinSelect", 13, Theme.Text)); name.Children.Add(Theme.Label("屏幕切换", 10.5, Theme.Muted)); heading.Children.Add(name); content.Children.Add(heading);
            content.Children.Add(Item("显示面板", "\uE7F4", hotkey, false, show));
            content.Children.Add(Item("设置", "\uE713", "", false, settings));
            content.Children.Add(new Border { Height = 1, Background = Theme.Brush("#E8ECF2"), Margin = new Thickness(10, 7, 10, 7) });
            content.Children.Add(Item("退出", "\uE7E8", "", true, quit));
            surface.Child = content; Content = surface;
            // Measure actual content instead of reserving a fixed-height footer.
            surface.Measure(new Size(LogicalWidth, Double.PositiveInfinity));
            logicalHeight = Math.Ceiling(surface.DesiredSize.Height); Height = logicalHeight;
            Deactivated += delegate { Dismiss(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; } };
            SourceInitialized += delegate { Position(); }; Loaded += delegate { Position(); };
        }
        Button Item(string label, string glyph, string shortcut, bool destructive, Action action)
        {
            Button button = new Button { Height = 37, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(10, 0, 10, 0), Cursor = Cursors.Hand, Foreground = destructive ? Theme.Brush("#BE4247") : Theme.Text };
            AutomationProperties.SetName(button, label);
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bg = new FrameworkElementFactory(typeof(Border), "Hover"); bg.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); bg.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0)); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); bg.AppendChild(presenter); template.VisualTree = bg;
            Trigger hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Brush(destructive ? "#FCECEE" : "#EAF1FE"), "Hover")); template.Triggers.Add(hover);
            Trigger focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Brush(destructive ? "#FCECEE" : "#EAF1FE"), "Hover")); template.Triggers.Add(focus);
            button.Template = template;
            Grid row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock symbol = Theme.Label(glyph, 16, destructive ? Theme.Brush("#BE4247") : Theme.Muted); symbol.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"); row.Children.Add(symbol);
            TextBlock title = Theme.Label(label, 12, button.Foreground); Grid.SetColumn(title, 1); row.Children.Add(title);
            TextBlock key = Theme.Label(shortcut, 10.5, Theme.Muted); key.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(key, 2); row.Children.Add(key); button.Content = row;
            button.Click += delegate { Dismiss(); Dispatcher.BeginInvoke(action); };
            return button;
        }
        public void Position()
        {
            Width = LogicalWidth; Height = logicalHeight; double scale = monitor.Scale;
            int w = (int)Math.Round(LogicalWidth * scale), h = (int)Math.Round(logicalHeight * scale), margin = (int)(4 * scale);
            RectI r = monitor.Info.Work;
            int x = Math.Max(r.Left + margin, Math.Min(anchor.X - (int)(12 * scale), r.Right - w - margin));
            int y = Math.Max(r.Top + margin, Math.Min(anchor.Y - h - margin, r.Bottom - h - margin));
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), x, y, w, h, 0);
        }
        public void Dismiss() { if (!closing) { closing = true; Close(); } }
    }
}
