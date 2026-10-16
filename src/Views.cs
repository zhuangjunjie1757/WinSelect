using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WinSelect
{
    public static class Theme
    {
        public static readonly Brush Text = Brush("#172337"), Muted = Brush("#68768B"), Blue = Brush("#2467E8"), Line = Brush("#E0E6EE");
        public static SolidColorBrush Brush(string color) { SolidColorBrush b = (SolidColorBrush)new BrushConverter().ConvertFromString(color); b.Freeze(); return b; }
        public static TextBlock Label(string text, double size, Brush color) { return new TextBlock { Text = text, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center }; }
        public static Button Button(string text) { return new Button { Content = text, Padding = new Thickness(16, 8, 16, 8), MinHeight = 34, Background = Brushes.White, Foreground = Text, BorderBrush = Line, BorderThickness = new Thickness(1), Cursor = Cursors.Hand }; }
    }
    public static class Captures
    {
        public static Dictionary<string, BitmapSource> Take(List<DisplayInfo> displays)
        {
            Dictionary<string, BitmapSource> result = new Dictionary<string, BitmapSource>();
            foreach (DisplayInfo d in displays.Where(d => d.Active && d.Width > 0 && d.Height > 0))
            {
                try
                {
                    using (Drawing.Bitmap full = new Drawing.Bitmap(d.Width, d.Height, Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (Drawing.Graphics g = Drawing.Graphics.FromImage(full)) g.CopyFromScreen(d.X, d.Y, 0, 0, new Drawing.Size(d.Width, d.Height), Drawing.CopyPixelOperation.SourceCopy);
                        int w = Math.Min(d.Width, 760), h = Math.Max(1, (int)Math.Round(d.Height * w / (double)d.Width));
                        using (Drawing.Bitmap thumb = new Drawing.Bitmap(w, h))
                        {
                            using (Drawing.Graphics g = Drawing.Graphics.FromImage(thumb)) { g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(full, 0, 0, w, h); }
                            IntPtr bitmap = thumb.GetHbitmap();
                            try { BitmapSource image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); image.Freeze(); result[d.Identity] = image; }
                            finally { Native.DeleteObject(bitmap); }
                        }
                    }
                }
                catch (Exception error) { Storage.Log(error); }
            }
            return result;
        }
    }
    public sealed class PanelWindow : Window
    {
        public readonly MonitorArea Screen;
        public readonly bool Bottom;
        public readonly double DesiredWidth;
        public double DesiredHeight { get; private set; }
        readonly double normalHeight;
        readonly PointL? trayAnchor;
        readonly Controller controller;
        readonly Dictionary<string, ToggleButton> toggles = new Dictionary<string, ToggleButton>();
        readonly TextBlock status = Theme.Label("", 12, Theme.Muted);
        public PanelWindow(Controller owner, MonitorArea screen, bool bottom, List<DisplayInfo> displays, Dictionary<string, BitmapSource> captures, PointL? anchor = null)
        {
            controller = owner; Screen = screen; Bottom = bottom; trayAnchor = anchor;
            Title = "WinSelect · 屏幕切换"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true;
            ShowActivated = false; FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            // Compare centres: differently sized screens aligned in Windows should still
            // form one row, rather than becoming a diagonal because their top edges differ.
            List<int> xs = Bands(displays.Select(d => d.LayoutX + d.Width / 2), Math.Max(1, displays.Max(d => d.Width) / 2));
            List<int> ys = Bands(displays.Select(d => d.LayoutY + d.Height / 2), Math.Max(1, displays.Max(d => d.Height) / 2));
            int columns = xs.Count, rows = ys.Count;
            double availableWidth = Math.Max(80, screen.Info.Work.Width / screen.Scale - 74);
            double availableHeight = Math.Max(80, screen.Info.Work.Height / screen.Scale - 64);
            double cardWidth = Math.Min(207, (availableWidth - 14 * (columns - 1)) / columns);
            // The window can fit vertically stacked display arrangements too.
            double imageHeight = Math.Max(24, Math.Min(cardWidth * 9 / 16, (availableHeight - 14 * (rows - 1)) / rows - 103));
            double cardHeight = imageHeight + 103;
            double panelWidth = columns * cardWidth + (columns - 1) * 14;
            double panelHeight = rows * cardHeight + (rows - 1) * 14;
            Canvas canvas = new Canvas { Width = panelWidth, Height = panelHeight };
            HashSet<string> cells = new HashSet<string>();
            for (int i = 0; i < displays.Count; i++)
            {
                DisplayInfo d = displays[i]; int x = BandIndex(xs, d.LayoutX + d.Width / 2), y = BandIndex(ys, d.LayoutY + d.Height / 2);
                // Mirrored outputs can share desktop coordinates: retain separate controls.
                while (cells.Contains(x + ":" + y)) x++;
                cells.Add(x + ":" + y);
                Border card = Card(d, i + 1, cardWidth, imageHeight, captures);
                Canvas.SetLeft(card, x * (cardWidth + 14)); Canvas.SetTop(card, y * (cardHeight + 14)); canvas.Children.Add(card);
                canvas.Width = Math.Max(canvas.Width, (x + 1) * cardWidth + x * 14);
            }
            // Uniform scaling is only needed for unusually small work areas or mirrored cards.
            Viewbox fit = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = availableWidth, MaxHeight = availableHeight };
            fit.Measure(new Size(availableWidth, availableHeight));
            StackPanel stack = new StackPanel(); stack.Children.Add(fit);
            status.HorizontalAlignment = HorizontalAlignment.Center; status.TextAlignment = TextAlignment.Center;
            status.Margin = new Thickness(0, 6, 0, 0); status.MaxWidth = fit.DesiredSize.Width;
            status.Visibility = Visibility.Collapsed; status.TextWrapping = TextWrapping.Wrap; stack.Children.Add(status);
            Border surface = new Border { Padding = new Thickness(14, 13, 14, 5), CornerRadius = new CornerRadius(14), Background = Theme.Brush("#FCFDFF"), BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Child = stack, Effect = new DropShadowEffect { Color = Color.FromRgb(12, 30, 60), BlurRadius = 18, ShadowDepth = 4, Opacity = .16 } };
            Content = new Border { Padding = new Thickness(10), Background = Brushes.Transparent, Child = surface };
            Width = fit.DesiredSize.Width + 50;
            Height = fit.DesiredSize.Height + 40;
            DesiredWidth = Width; DesiredHeight = Height;
            normalHeight = DesiredHeight;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { controller.HidePanels(); e.Handled = true; } };
            Deactivated += delegate { controller.QueueDismiss(); };
            SourceInitialized += delegate { Position(); };
            Loaded += delegate { Position(); };
        }
        static List<int> Bands(IEnumerable<int> values, int tolerance)
        {
            List<int> result = new List<int>(); foreach (int n in values.OrderBy(v => v)) if (!result.Any(v => Math.Abs((long)n - v) < tolerance)) result.Add(n); return result;
        }
        static int BandIndex(List<int> values, int n) { return values.Select((v, i) => new { Distance = Math.Abs((long)n - v), Index = i }).OrderBy(v => v.Distance).First().Index; }
        Border Card(DisplayInfo display, int number, double width, double imageHeight, Dictionary<string, BitmapSource> captures)
        {
            Border card = new Border { Width = width, Background = Brushes.Transparent };
            StackPanel body = new StackPanel();
            DockPanel head = new DockPanel { Height = 24, LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
            TextBlock digit = Theme.Label(number.ToString(), 11, Theme.Muted);
            Border badge = new Border { Child = digit, Width = 19, Height = 19, CornerRadius = new CornerRadius(5), BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Background = Theme.Brush("#F7F9FC"), Margin = new Thickness(0, 0, 6, 0) }; digit.TextAlignment = TextAlignment.Center;
            head.Children.Add(badge); TextBlock name = Theme.Label(display.Name, 12, Theme.Text); name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = display.Name; head.Children.Add(name); body.Children.Add(head);
            Border screenshot = new Border { Height = imageHeight, Background = Theme.Brush("#F0F3F8"), CornerRadius = new CornerRadius(7), ClipToBounds = true };
            screenshot.Clip = new RectangleGeometry(new Rect(0, 0, width, imageHeight), 7, 7);
            BitmapSource bitmap;
            if (display.Active && captures.TryGetValue(display.Identity, out bitmap)) screenshot.Child = new Image { Source = bitmap, Stretch = Stretch.Uniform };
            else
            {
                StackPanel placeholder = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                TextBlock glyph = Theme.Label("▱", 32, Theme.Muted); glyph.HorizontalAlignment = HorizontalAlignment.Center; placeholder.Children.Add(glyph);
                TextBlock hint = Theme.Label(display.Active ? "暂时无法截取画面" : "输出已关闭", 12, Theme.Muted); hint.Margin = new Thickness(0, 6, 0, 0); placeholder.Children.Add(hint); screenshot.Child = placeholder;
            }
            body.Children.Add(screenshot);
            TextBlock details = Theme.Label(display.Details, 11, Theme.Muted); details.Height = 31; details.Padding = new Thickness(0, 7, 0, 0); details.TextAlignment = TextAlignment.Center; details.TextTrimming = TextTrimming.CharacterEllipsis; details.ToolTip = display.Details; body.Children.Add(details);
            ToggleButton toggle = new ToggleButton { IsChecked = display.Active, Width = 44, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.Hand, ToolTip = display.Active ? "关闭此屏幕输出" : "开启此屏幕输出" };
            AutomationProperties.SetName(toggle, display.Name + " 屏幕输出");
            ControlTemplate template = new ControlTemplate(typeof(ToggleButton));
            FrameworkElementFactory hit = new FrameworkElementFactory(typeof(Border)); hit.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            FrameworkElementFactory dot = new FrameworkElementFactory(typeof(Border), "Dot"); dot.SetValue(Border.WidthProperty, 20.0); dot.SetValue(Border.HeightProperty, 20.0); dot.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); dot.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Center); dot.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center); dot.SetValue(Border.BorderBrushProperty, Theme.Muted); dot.SetValue(Border.BorderThicknessProperty, new Thickness(1.5)); dot.SetValue(Border.BackgroundProperty, Brushes.White);
            FrameworkElementFactory core = new FrameworkElementFactory(typeof(Border), "Core"); core.SetValue(Border.WidthProperty, 10.0); core.SetValue(Border.HeightProperty, 10.0); core.SetValue(Border.CornerRadiusProperty, new CornerRadius(5)); core.SetValue(Border.BackgroundProperty, Theme.Blue); core.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Center); core.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center); core.SetValue(UIElement.VisibilityProperty, Visibility.Hidden); dot.AppendChild(core);
            hit.AppendChild(dot); template.VisualTree = hit;
            Trigger on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; on.Setters.Add(new Setter(Border.BorderBrushProperty, Theme.Blue, "Dot")); on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Core")); template.Triggers.Add(on);
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(UIElement.OpacityProperty, .72, "Dot")); template.Triggers.Add(hover);
            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4, "Dot")); template.Triggers.Add(disabled);
            toggle.Template = template;
            toggle.Click += async delegate { bool requested = toggle.IsChecked == true; toggle.IsChecked = display.Active; await controller.Toggle(display.Identity, requested); };
            toggles[display.Identity] = toggle; body.Children.Add(toggle); card.Child = body; return card;
        }
        public void Busy(bool busy) { foreach (ToggleButton t in toggles.Values) t.IsEnabled = !busy; }
        public void Error(string message) { status.Text = message; status.Foreground = Theme.Brush("#B43E3E"); status.Visibility = Visibility.Visible; status.Measure(new Size(Math.Max(80, DesiredWidth - 50), Double.PositiveInfinity)); DesiredHeight = normalHeight + status.DesiredSize.Height; Position(); }
        public void Position()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle; RectI work = Screen.Info.Work;
            // WPF starts on the primary monitor; place once to let WM_DPICHANGED select
            // the destination monitor, then position using the destination scale.
            // WM_DPICHANGED may alter Window.Width/Height during the first move away
            // from the primary screen. Keep the intended logical dimensions immutable.
            Width = DesiredWidth; Height = DesiredHeight;
            double scale = Screen.Scale; int w = (int)Math.Round(DesiredWidth * scale), h = (int)Math.Round(DesiredHeight * scale);
            int x, y;
            if (Bottom) { x = work.Left + (work.Width - w) / 2; y = work.Bottom - h - (int)Math.Round(12 * scale); }
            else
            {
                PointL point = trayAnchor ?? new PointL(work.Right, work.Bottom);
                int margin = (int)Math.Round(4 * scale);
                x = Math.Max(work.Left + margin, Math.Min(point.X - w - margin, work.Right - w - margin));
                y = Math.Max(work.Top + margin, Math.Min(point.Y - h - margin, work.Bottom - h - margin));
            }
            Native.SetWindowPos(hwnd, new IntPtr(-1), x, y, w, h, 0x10);
        }
    }
    public sealed class SettingsWindow : Window
    {
        public const double LogicalWidth = 392;
        public double LogicalHeight { get; private set; }
        public readonly MonitorArea Screen;
        readonly Controller controller;
        readonly Button shortcut;
        readonly TextBlock result = Theme.Label("", 12, Theme.Muted);
        bool recording;
        public SettingsWindow(Controller owner, MonitorArea screen = null)
        {
            controller = owner; Screen = screen ?? owner.MouseMonitor(); Title = "WinSelect 设置";
            Width = LogicalWidth; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true; WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"); UseLayoutRounding = true; SnapsToDevicePixels = true;
            StackPanel stack = new StackPanel();
            DockPanel header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 15), Background = Brushes.Transparent };
            Button close = RoundedButton("\uE8BB", true); close.Width = 28; close.Height = 28; close.FontSize = 12; close.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"); close.ToolTip = "关闭"; close.Click += delegate { Close(); }; AutomationProperties.SetName(close, "关闭设置"); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            Image mark = new Image { Source = owner.AppIcon, Width = 24, Height = 24, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }; header.Children.Add(mark);
            StackPanel heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; heading.Children.Add(Theme.Label("设置", 14, Theme.Text)); heading.Children.Add(Theme.Label("WinSelect", 10.5, Theme.Muted)); header.Children.Add(heading);
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); };
            stack.Children.Add(header);
            DockPanel hotkeyRow = new DockPanel { Height = 62, LastChildFill = true };
            shortcut = RoundedButton(owner.ShortcutText, false); shortcut.Width = 145; shortcut.Height = 33; DockPanel.SetDock(shortcut, Dock.Right); hotkeyRow.Children.Add(shortcut); hotkeyRow.Children.Add(RowLabel("呼出快捷键", "点击右侧按键框修改")); stack.Children.Add(hotkeyRow);
            shortcut.Click += delegate { recording = true; shortcut.Content = "按下组合键…"; Feedback("按 Ctrl / Alt / Shift 组合键，Esc 取消"); };
            stack.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 2, 0, 2) });
            DockPanel startupRow = new DockPanel { Height = 66, LastChildFill = true };
            ToggleButton startup = StartupSwitch(); startup.IsChecked = owner.StartupEnabled; DockPanel.SetDock(startup, Dock.Right); startupRow.Children.Add(startup); startupRow.Children.Add(RowLabel("开机启动", "登录 Windows 后常驻托盘")); stack.Children.Add(startupRow);
            startup.Click += delegate { try { owner.SetStartup(startup.IsChecked == true); Feedback("已保存"); } catch (Exception error) { startup.IsChecked = owner.StartupEnabled; Feedback("保存失败：" + error.Message, true); } };
            result.Visibility = Visibility.Collapsed; result.Margin = new Thickness(0, 3, 0, 8); result.FontSize = 11; result.TextWrapping = TextWrapping.Wrap; stack.Children.Add(result);
            Border note = new Border { Background = Theme.Brush("#F1F5FA"), CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(0, 6, 0, 0) };
            TextBlock noteText = Theme.Label("呼出时截图一次 · 关闭后常驻托盘", 11, Theme.Muted); noteText.TextWrapping = TextWrapping.Wrap; note.Child = noteText; stack.Children.Add(note);
            Border surface = new Border { Padding = new Thickness(18, 16, 18, 16), CornerRadius = new CornerRadius(14), Background = Theme.Brush("#FCFDFF"), BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Child = stack, Effect = new DropShadowEffect { Color = Color.FromRgb(12, 30, 60), BlurRadius = 18, ShadowDepth = 4, Opacity = .16 } };
            Content = new Border { Padding = new Thickness(10), Child = surface, Background = Brushes.Transparent };
            MeasureContent();
            PreviewKeyDown += RecordKey;
            SourceInitialized += delegate { PositionSettings(); }; Loaded += delegate { PositionSettings(); };
        }
        static StackPanel RowLabel(string title, string hint)
        {
            StackPanel label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(Theme.Label(title, 12, Theme.Text)); TextBlock sub = Theme.Label(hint, 11, Theme.Muted); sub.Margin = new Thickness(0, 4, 0, 0); label.Children.Add(sub); return label;
        }
        static Button RoundedButton(string text, bool quiet)
        {
            Button button = new Button { Content = text, FontSize = 12, Foreground = Theme.Text, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory frame = new FrameworkElementFactory(typeof(Border), "Frame"); frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); frame.SetValue(Border.BorderThicknessProperty, new Thickness(quiet ? 0 : 1)); frame.SetValue(Border.BorderBrushProperty, Theme.Line); frame.SetValue(Border.BackgroundProperty, quiet ? Brushes.Transparent : Theme.Brush("#F5F7FB"));
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); frame.AppendChild(presenter); template.VisualTree = frame;
            Trigger hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Brush("#EAF0F8"), "Frame")); template.Triggers.Add(hover);
            button.Template = template; return button;
        }
        static ToggleButton StartupSwitch()
        {
            ToggleButton toggle = new ToggleButton { Width = 44, Height = 34, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(toggle, "开机启动");
            ControlTemplate template = new ControlTemplate(typeof(ToggleButton));
            FrameworkElementFactory hit = new FrameworkElementFactory(typeof(Border)); hit.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            FrameworkElementFactory track = new FrameworkElementFactory(typeof(Border), "Track"); track.SetValue(Border.WidthProperty, 34.0); track.SetValue(Border.HeightProperty, 20.0); track.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); track.SetValue(Border.BackgroundProperty, Theme.Brush("#CED7E3")); track.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Right); track.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
            FrameworkElementFactory thumb = new FrameworkElementFactory(typeof(Border), "Thumb"); thumb.SetValue(Border.WidthProperty, 14.0); thumb.SetValue(Border.HeightProperty, 14.0); thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(7)); thumb.SetValue(Border.MarginProperty, new Thickness(3, 0, 3, 0)); thumb.SetValue(Border.BackgroundProperty, Brushes.White); thumb.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left); thumb.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center); track.AppendChild(thumb); hit.AppendChild(track); template.VisualTree = hit;
            Trigger on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; on.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Blue, "Track")); on.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "Thumb")); template.Triggers.Add(on); toggle.Template = template; return toggle;
        }
        void MeasureContent()
        {
            FrameworkElement content = (FrameworkElement)Content; content.Measure(new Size(LogicalWidth, Double.PositiveInfinity)); LogicalHeight = Math.Ceiling(content.DesiredSize.Height); Width = LogicalWidth; Height = LogicalHeight;
        }
        void PositionSettings()
        {
            if (Screen == null) return;
            Width = LogicalWidth; Height = LogicalHeight;
            RectI r = Screen.Info.Work; int w = (int)Math.Round(LogicalWidth * Screen.Scale), h = (int)Math.Round(LogicalHeight * Screen.Scale);
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, r.Left + (r.Width - w) / 2, r.Top + (r.Height - h) / 2, w, h, 0x4);
        }
        void Feedback(string text, bool error = false)
        {
            result.Text = text; result.Foreground = error ? Theme.Brush("#B43E3E") : Theme.Muted; result.Visibility = Visibility.Visible; MeasureContent(); PositionSettings();
        }
        void RecordKey(object sender, KeyEventArgs e)
        {
            if (!recording) { if (e.Key == Key.Escape) Close(); return; }
            e.Handled = true; Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { recording = false; shortcut.Content = controller.ShortcutText; Feedback("已取消修改"); return; }
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return;
            ModifierKeys m = Keyboard.Modifiers;
            if ((m & ModifierKeys.Windows) != 0 || (m & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) == 0 || key == Key.F12) { Feedback("请使用 Ctrl / Alt / Shift 组合键，避开 Win 键和 F12。", true); return; }
            uint mods = ((m & ModifierKeys.Alt) != 0 ? 1u : 0) | ((m & ModifierKeys.Control) != 0 ? 2u : 0) | ((m & ModifierKeys.Shift) != 0 ? 4u : 0);
            string error; if (controller.ChangeShortcut(mods, (uint)KeyInterop.VirtualKeyFromKey(key), out error)) { recording = false; shortcut.Content = controller.ShortcutText; Feedback("快捷键已保存"); } else Feedback(error, true);
        }
    }
}
