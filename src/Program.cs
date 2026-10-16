using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace WinSelect
{
    public static class Program
    {
        public static readonly uint ShowMessage = Native.RegisterWindowMessage("WinSelect.ShowPanel.1");
        public static readonly uint QuitMessage = Native.RegisterWindowMessage("WinSelect.Exit.1");
        public static IntPtr FindInstanceWindow() { IntPtr window = Native.FindWindow(null, "WinSelect.MessageWindow"); return window != IntPtr.Zero ? window : Native.FindWindowEx(new IntPtr(-3), IntPtr.Zero, null, "WinSelect.MessageWindow"); }
        [STAThread] public static int Main(string[] args)
        {
            if (args.Length > 0 && (args[0] == "--diagnose" || args[0] == "--verify")) return Diagnostics.Run(args);
            if (args.Contains("--quit")) { IntPtr running = FindInstanceWindow(); if (running != IntPtr.Zero) Native.PostMessage(running, QuitMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\WinSelect.Desktop.1", out created))
            {
                if (!created) { IntPtr other = FindInstanceWindow(); if (other != IntPtr.Zero) Native.PostMessage(other, ShowMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Controller controller = null;
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { Storage.Log(e.Exception); if (controller != null) controller.Notify("操作失败", e.Exception.Message); e.Handled = true; };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) { Storage.Log(e.ExceptionObject as Exception ?? new Exception("未知错误")); };
                try
                {
                    controller = new Controller();
                    if (args.Contains("--ui-check")) app.Dispatcher.BeginInvoke(new Action(async delegate { bool passed = await controller.UiCheck(); app.Shutdown(passed ? 0 : 1); }));
                    else if (!args.Contains("--background")) app.Dispatcher.BeginInvoke(new Action(delegate { controller.ShowPanels(false); }));
                    return app.Run();
                }
                catch (Exception error) { Storage.Log(error); MessageBox.Show(error.Message, "WinSelect 启动失败", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
                finally { if (controller != null) controller.Dispose(); }
            }
        }
    }
    public sealed class Controller : IDisposable
    {
        readonly Preferences preferences;
        readonly DisplayService displays;
        readonly HwndSource messages;
        readonly Forms.NotifyIcon tray;
        readonly Drawing.Icon icon;
        readonly BitmapSource brandIcon;
        readonly List<PanelWindow> windows = new List<PanelWindow>();
        readonly DispatcherTimer displayDebounce, dismissTimer;
        SettingsWindow settings;
        TrayMenuWindow trayMenu;
        PointL? panelTrayAnchor;
        string panelTrayDevice;
        bool busy, opening, disposed, allScreens;
        int shortcutId = 101;
        bool hotkeyAvailable;
        Dictionary<string, BitmapSource> captures = new Dictionary<string, BitmapSource>();
        DateTime ignoreDismissUntil;
        public Controller()
        {
            preferences = Storage.Load(); displays = new DisplayService(preferences);
            messages = new HwndSource(new HwndSourceParameters("WinSelect.MessageWindow") { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x80 });
            Native.SetWindowText(messages.Handle, "WinSelect.MessageWindow");
            messages.AddHook(Hook);
            bool registered = Native.RegisterHotKey(messages.Handle, shortcutId, preferences.Modifiers | 0x4000, preferences.Key);
            hotkeyAvailable = registered;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinSelect.Icon"))
            using (Drawing.Icon loaded = new Drawing.Icon(stream, 32, 32)) icon = (Drawing.Icon)loaded.Clone();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinSelect.Icon"))
            using (Drawing.Icon brand = new Drawing.Icon(stream, 64, 64)) brandIcon = Imaging.CreateBitmapSourceFromHIcon(brand.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            brandIcon.Freeze();
            tray = new Forms.NotifyIcon { Icon = icon, Text = "WinSelect · 屏幕切换", Visible = true };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) Application.Current.Dispatcher.BeginInvoke(new Action(delegate { ShowPanels(false); })); };
            tray.MouseUp += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Right) Application.Current.Dispatcher.BeginInvoke(new Action(ShowTrayMenu)); };
            displayDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            displayDebounce.Tick += delegate { displayDebounce.Stop(); if (busy) { displayDebounce.Start(); return; } if (windows.Count > 0) RefreshVisible(); };
            dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            dismissTimer.Tick += delegate {
                dismissTimer.Stop(); if (busy || opening || DateTime.UtcNow < ignoreDismissUntil) return;
                uint pid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                if (pid != (uint)Process.GetCurrentProcess().Id) HidePanels();
            };
            SystemEvents.DisplaySettingsChanged += OnDisplayChange;
            if (!registered) Application.Current.Dispatcher.BeginInvoke(new Action(delegate { Notify("快捷键被占用", "请右击 WinSelect 托盘图标，在设置里更换快捷键。"); }));
        }
        IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x312) { handled = true; if (windows.Count > 0 && allScreens) HidePanels(); else ShowPanels(true); }
            if ((uint)message == Program.ShowMessage) { handled = true; ShowPanels(false); }
            if ((uint)message == Program.QuitMessage) { handled = true; Application.Current.Shutdown(); }
            return IntPtr.Zero;
        }
        void OnDisplayChange(object sender, EventArgs e)
        {
            if (disposed) return;
            Application.Current.Dispatcher.BeginInvoke(new Action(delegate { if (disposed) return; displayDebounce.Stop(); displayDebounce.Start(); }));
        }
        public string ShortcutText { get { return ((preferences.Modifiers & 2) != 0 ? "Ctrl + " : "") + ((preferences.Modifiers & 1) != 0 ? "Alt + " : "") + ((preferences.Modifiers & 4) != 0 ? "Shift + " : "") + ((Forms.Keys)preferences.Key).ToString(); } }
        internal BitmapSource AppIcon { get { return brandIcon; } }
        public bool ChangeShortcut(uint modifiers, uint key, out string error)
        {
            error = ""; if (modifiers == preferences.Modifiers && key == preferences.Key && hotkeyAvailable) return true;
            int nextId = shortcutId == 101 ? 102 : 101;
            // Register the replacement first, so a conflict never removes the working key.
            if (!Native.RegisterHotKey(messages.Handle, nextId, modifiers | 0x4000, key)) { error = "该快捷键已被占用，请换一个组合。"; return false; }
            uint oldModifiers = preferences.Modifiers, oldKey = preferences.Key;
            preferences.Modifiers = modifiers; preferences.Key = key;
            try { Storage.Save(preferences); }
            catch (Exception e) { preferences.Modifiers = oldModifiers; preferences.Key = oldKey; Native.UnregisterHotKey(messages.Handle, nextId); error = "保存失败：" + e.Message; return false; }
            Native.UnregisterHotKey(messages.Handle, shortcutId); shortcutId = nextId; hotkeyAvailable = true; return true;
        }
        public bool StartupEnabled
        {
            get { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key != null && key.GetValue("WinSelect") != null; }
        }
        public void SetStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled) key.SetValue("WinSelect", "\"" + Assembly.GetExecutingAssembly().Location + "\" --background"); else key.DeleteValue("WinSelect", false);
            }
        }
        public MonitorArea MouseMonitor()
        {
            PointL pt; Native.GetCursorPos(out pt); IntPtr monitor = Native.MonitorFromPoint(pt, 2);
            List<MonitorArea> monitors = Native.Monitors(); return monitors.Find(m => m.Handle == monitor) ?? monitors.FirstOrDefault();
        }
        void RememberTrayAnchor(MonitorArea monitor)
        {
            if (monitor == null) return;
            RectI work = monitor.Info.Work;
            panelTrayDevice = monitor.Info.Device; panelTrayAnchor = new PointL(work.Right, work.Bottom);
            // Read the Explorer popup under the pointer rather than assuming its height.
            // When the icon is directly on the taskbar, use the working-area edge.
            PointL pointer; Native.GetCursorPos(out pointer);
            IntPtr root = Native.GetAncestor(Native.WindowFromPoint(pointer), 2);
            RectI rect; uint processId;
            if (root == IntPtr.Zero || !Native.GetWindowRect(root, out rect)) return;
            Native.GetWindowThreadProcessId(root, out processId);
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    if (String.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase) && rect.Width > 0 && rect.Height > 0 && rect.Width < work.Width * .8 && rect.Height < work.Height * .6 && rect.Top >= work.Top)
                        panelTrayAnchor = new PointL(Math.Min(rect.Right, work.Right), Math.Min(rect.Top, work.Bottom));
                }
            }
            catch (ArgumentException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        public void ShowPanels(bool everyScreen)
        {
            if (opening || busy) return;
            opening = true;
            try
            {
                // Resolve the pointer's monitor before creating or moving any windows.
                MonitorArea pointerMonitor = MouseMonitor();
                if (!everyScreen && (trayMenu == null || pointerMonitor == null || panelTrayDevice != pointerMonitor.Info.Device)) RememberTrayAnchor(pointerMonitor);
                CloseTrayMenu();
                if (settings != null) settings.Close();
                HidePanels(); allScreens = everyScreen;
                List<DisplayInfo> list = displays.Read(true);
                if (list.Count == 0) throw new InvalidOperationException("没有发现可控制的显示器。");
                // Flush old windows before taking the one-shot screenshots.
                System.Windows.Media.CompositionTarget.Rendering += CaptureAfterHide;
                pendingDisplays = list; pendingMouse = pointerMonitor;
                // A dispatcher render turn is also available when no WPF window is visible.
                Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(FinishOpen));
            }
            catch (Exception error) { opening = false; Storage.Log(error); Notify("无法打开面板", error.Message); }
        }
        List<DisplayInfo> pendingDisplays; MonitorArea pendingMouse;
        void CaptureAfterHide(object sender, EventArgs e) { FinishOpen(); }
        void FinishOpen()
        {
            if (!opening || pendingDisplays == null) return;
            System.Windows.Media.CompositionTarget.Rendering -= CaptureAfterHide;
            List<DisplayInfo> list = pendingDisplays; MonitorArea mouse = pendingMouse; pendingDisplays = null;
            try { captures = Captures.Take(list); CreatePanels(list, allScreens, mouse); }
            catch (Exception error) { Storage.Log(error); Notify("无法打开面板", error.Message); }
            finally { opening = false; }
        }
        void CreatePanels(List<DisplayInfo> list, bool everyScreen, MonitorArea mouse)
        {
            List<MonitorArea> screens = Native.Monitors();
            if (!everyScreen) { MonitorArea chosen = mouse == null ? null : screens.Find(m => m.Info.Device == mouse.Info.Device); screens = new List<MonitorArea> { chosen ?? screens.FirstOrDefault() }; }
            foreach (MonitorArea monitor in screens.Where(m => m != null))
            {
                PointL? anchor = !everyScreen && monitor.Info.Device == panelTrayDevice ? panelTrayAnchor : null;
                PanelWindow panel = new PanelWindow(this, monitor, everyScreen, list, captures, anchor); windows.Add(panel); panel.Show(); panel.Position();
            }
            ignoreDismissUntil = DateTime.UtcNow.AddMilliseconds(350);
            PanelWindow foreground = windows.FirstOrDefault(w => mouse != null && w.Screen.Info.Device == mouse.Info.Device) ?? windows.FirstOrDefault();
            if (foreground != null) { foreground.Activate(); Native.SetForegroundWindow(new WindowInteropHelper(foreground).Handle); }
        }
        public void HidePanels()
        {
            foreach (PanelWindow window in windows.ToArray()) window.Close(); windows.Clear();
            dismissTimer.Stop();
        }
        public void QueueDismiss() { if (!disposed) { dismissTimer.Stop(); dismissTimer.Start(); } }
        void RefreshVisible()
        {
            if (busy || opening || windows.Count == 0) return;
            try
            {
                MonitorArea old = windows[0].Screen; bool every = allScreens;
                List<DisplayInfo> list = displays.Read(true);
                HidePanels(); CreatePanels(list, every, old);
            }
            catch (Exception error) { Storage.Log(error); Notify("刷新显示器失败", error.Message); }
        }
        public async Task Toggle(string identity, bool enabled)
        {
            if (busy) return; busy = true;
            foreach (PanelWindow window in windows) window.Busy(true);
            try
            {
                await Task.Run(delegate { displays.SetEnabled(identity, enabled); });
                // State is read back from Windows; every visible copy is rebuilt from it.
                // Screen capture is deliberately NOT repeated during switching.
                MonitorArea old = windows.FirstOrDefault() == null ? MouseMonitor() : windows[0].Screen;
                bool every = allScreens; List<DisplayInfo> list = displays.Read(false);
                HidePanels(); CreatePanels(list, every, old);
            }
            catch (Exception error) { Storage.Log(error); foreach (PanelWindow window in windows) window.Error(error.Message); if (windows.Count == 0) Notify("切换失败", error.Message); }
            finally { busy = false; foreach (PanelWindow window in windows) window.Busy(false); }
        }
        void OpenSettings()
        {
            if (busy || opening) return;
            CloseTrayMenu();
            HidePanels();
            if (settings != null) { settings.Activate(); return; }
            settings = new SettingsWindow(this); settings.Closed += delegate { settings = null; }; settings.Show(); settings.Activate();
        }
        void CloseTrayMenu() { if (trayMenu != null) trayMenu.Dismiss(); }
        void ShowTrayMenu()
        {
            if (busy || opening) return;
            HidePanels(); CloseTrayMenu();
            MonitorArea monitor = MouseMonitor(); if (monitor == null) return;
            RememberTrayAnchor(monitor);
            PointL cursor; Native.GetCursorPos(out cursor);
            TrayMenuWindow popup = new TrayMenuWindow(monitor, cursor, brandIcon, ShortcutText, delegate { ShowPanels(false); }, OpenSettings, delegate { Application.Current.Shutdown(); });
            trayMenu = popup; popup.Closed += delegate { if (trayMenu == popup) trayMenu = null; };
            popup.Show(); popup.Position(); popup.Activate(); Native.SetForegroundWindow(new WindowInteropHelper(popup).Handle);
        }
        public void Notify(string title, string message) { if (!disposed) { tray.BalloonTipTitle = title; tray.BalloonTipText = message.Length > 200 ? message.Substring(0, 200) : message; tray.ShowBalloonTip(4000); } }
        internal async Task<bool> UiCheck()
        {
            StringBuilder report = new StringBuilder();
            bool passed = false;
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "work", "diagnostics"); Directory.CreateDirectory(folder);
            try
            {
                ShowPanels(true); await Task.Delay(250);
                if (windows.Count != Native.Monitors().Count) throw new Exception("Hotkey did not create one panel per screen");
                foreach (PanelWindow w in windows)
                {
                    RectI actual; Native.GetWindowRect(new WindowInteropHelper(w).Handle, out actual); RectI area = w.Screen.Info.Work;
                    report.AppendLine("Bottom " + w.Screen.Info.Device + " x=" + actual.Left + " y=" + actual.Top + " size=" + actual.Width + "x" + actual.Height + " dip=" + w.Width + "x" + w.Height);
                    if (Math.Abs((actual.Left + actual.Right) - (area.Left + area.Right)) > 4) throw new Exception("Bottom horizontal centering failed");
                    if (Math.Abs(area.Bottom - actual.Bottom - 12 * w.Screen.Scale) > 4) throw new Exception("Bottom offset failed");
                    if (Math.Abs(actual.Width - w.DesiredWidth * w.Screen.Scale) > 4 || Math.Abs(actual.Height - w.DesiredHeight * w.Screen.Scale) > 4) throw new Exception("Mixed-DPI panel size failed");
                    if (actual.Left < area.Left || actual.Right > area.Right || actual.Top < area.Top || actual.Bottom > area.Bottom) throw new Exception("Panel outside work area");
                }
                report.AppendLine("PASS bottom panels on every screen / mixed DPI");
                if (!hotkeyAvailable) throw new Exception("Configured global hotkey is already in use");
                Native.PostMessage(messages.Handle, 0x312, new IntPtr(shortcutId), IntPtr.Zero); await Task.Delay(80);
                if (windows.Count != 0) throw new Exception("Hotkey dismissal failed");
                Native.PostMessage(messages.Handle, 0x312, new IntPtr(shortcutId), IntPtr.Zero); await Task.Delay(250);
                if (windows.Count != Native.Monitors().Count) throw new Exception("Hotkey message did not open all panels");
                report.AppendLine("PASS global hotkey registration / show-hide routing");
                ShowPanels(false); await Task.Delay(250);
                if (windows.Count != 1) throw new Exception("Tray did not create exactly one panel");
                PanelWindow window = windows[0]; RectI center; Native.GetWindowRect(new WindowInteropHelper(window).Handle, out center); RectI r = window.Screen.Info.Work;
                if (window.Screen.Info.Device != MouseMonitor().Info.Device) throw new Exception("Tray panel on wrong screen");
                PointL point = panelTrayAnchor ?? new PointL(r.Right, r.Bottom); int margin = (int)Math.Round(4 * window.Screen.Scale);
                int expectedX = Math.Max(r.Left + margin, Math.Min(point.X - center.Width - margin, r.Right - center.Width - margin));
                int expectedY = Math.Max(r.Top + margin, Math.Min(point.Y - center.Height - margin, r.Bottom - center.Height - margin));
                if (Math.Abs(center.Left - expectedX) > 2 || Math.Abs(center.Top - expectedY) > 2) throw new Exception("Tray popup anchoring failed");
                if (center.Left < r.Left || center.Right > r.Right || center.Top < r.Top || center.Bottom > r.Bottom) throw new Exception("Tray panel outside work area");
                if (window.DesiredWidth > 480 || window.DesiredHeight > 270) throw new Exception("Two-screen popup is not compact");
                FrameworkElement panelView = (FrameworkElement)window.Content;
                RenderTargetBitmap panelBitmap = new RenderTargetBitmap((int)Math.Ceiling(window.Width), (int)Math.Ceiling(window.Height), 96, 96, System.Windows.Media.PixelFormats.Pbgra32); panelBitmap.Render(panelView);
                PngBitmapEncoder panelEncoder = new PngBitmapEncoder(); panelEncoder.Frames.Add(BitmapFrame.Create(panelBitmap)); using (FileStream f = File.Create(Path.Combine(folder, "compact-panel.png"))) panelEncoder.Save(f);
                report.AppendLine("PASS compact shared surface / tray anchor on pointer screen");
                OpenSettings(); await Task.Delay(100);
                if (settings == null || !settings.IsVisible || windows.Count != 0) throw new Exception("Settings navigation failed");
                FrameworkElement settingsView = (FrameworkElement)settings.Content;
                if (Math.Abs(settings.Height - settingsView.DesiredSize.Height) > 2) throw new Exception("Settings has excess space or clipped content");
                RenderTargetBitmap settingsBitmap = new RenderTargetBitmap((int)Math.Ceiling(settings.Width), (int)Math.Ceiling(settings.Height), 96, 96, System.Windows.Media.PixelFormats.Pbgra32); settingsBitmap.Render(settingsView);
                PngBitmapEncoder settingsEncoder = new PngBitmapEncoder(); settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap)); using (FileStream f = File.Create(Path.Combine(folder, "settings.png"))) settingsEncoder.Save(f);
                double restingHeight = settings.Height;
                System.Windows.Controls.StackPanel settingsRows = (System.Windows.Controls.StackPanel)((System.Windows.Controls.Border)((System.Windows.Controls.Border)settings.Content).Child).Child;
                System.Windows.Controls.Button keyButton = (System.Windows.Controls.Button)((System.Windows.Controls.DockPanel)settingsRows.Children[1]).Children[0];
                keyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Task.Delay(60);
                if (settings.Height <= restingHeight || settingsView.DesiredSize.Height > settings.Height + 2) throw new Exception("Shortcut capture feedback is clipped");
                System.Windows.Input.KeyEventArgs escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(settings), Environment.TickCount, System.Windows.Input.Key.Escape); escape.RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent; settings.RaiseEvent(escape);
                if (settings == null || !settings.IsVisible) throw new Exception("Shortcut capture cancellation closed settings");
                settings.Close();
                foreach (MonitorArea settingsScreen in Native.Monitors())
                {
                    SettingsWindow sample = new SettingsWindow(this, settingsScreen); sample.Show(); await Task.Delay(80);
                    RectI bounds; Native.GetWindowRect(new WindowInteropHelper(sample).Handle, out bounds); RectI area = settingsScreen.Info.Work;
                    if (Math.Abs(bounds.Width - SettingsWindow.LogicalWidth * settingsScreen.Scale) > 3 || Math.Abs(bounds.Height - sample.LogicalHeight * settingsScreen.Scale) > 3) throw new Exception("Settings mixed-DPI size failed");
                    if (Math.Abs(bounds.Left + bounds.Right - area.Left - area.Right) > 3 || Math.Abs(bounds.Top + bounds.Bottom - area.Top - area.Bottom) > 3) throw new Exception("Settings centering failed");
                    sample.Close();
                }
                report.AppendLine("PASS compact settings / shortcut feedback and cancel / mixed-DPI placement");
                IntPtr peer = Program.FindInstanceWindow();
                if (peer != messages.Handle) throw new Exception("Single-instance message window lookup failed");
                report.AppendLine("PASS settings / single-instance lookup");
                ShowTrayMenu(); await Task.Delay(100);
                if (trayMenu == null || !trayMenu.IsVisible) throw new Exception("Tray flyout did not open");
                RectI menuBounds; Native.GetWindowRect(new WindowInteropHelper(trayMenu).Handle, out menuBounds); RectI menuArea = MouseMonitor().Info.Work;
                if (menuBounds.Left < menuArea.Left || menuBounds.Right > menuArea.Right || menuBounds.Top < menuArea.Top || menuBounds.Bottom > menuArea.Bottom) throw new Exception("Tray flyout outside work area");
                FrameworkElement menuView = (FrameworkElement)trayMenu.Content;
                if (Math.Abs(trayMenu.Height - menuView.DesiredSize.Height) > 1) throw new Exception("Tray menu has excess footer space");
                RenderTargetBitmap menuBitmap = new RenderTargetBitmap((int)Math.Ceiling(trayMenu.Width), (int)Math.Ceiling(trayMenu.Height), 96, 96, System.Windows.Media.PixelFormats.Pbgra32); menuBitmap.Render(menuView);
                PngBitmapEncoder menuEncoder = new PngBitmapEncoder(); menuEncoder.Frames.Add(BitmapFrame.Create(menuBitmap)); using (FileStream f = File.Create(Path.Combine(folder, "tray-menu.png"))) menuEncoder.Save(f);
                System.Windows.Controls.StackPanel menuItems = (System.Windows.Controls.StackPanel)((System.Windows.Controls.Border)menuView).Child;
                ((System.Windows.Controls.Button)menuItems.Children[2]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Task.Delay(100);
                if (settings == null || !settings.IsVisible || trayMenu != null) throw new Exception("Tray settings item failed"); settings.Close();
                report.AppendLine("PASS custom tray menu bounds / settings action");
                report.AppendLine("PASS; no display output was changed");
                passed = true;
            }
            catch (Exception error) { Storage.Log(error); report.AppendLine("FAIL " + error); }
            finally { HidePanels(); File.WriteAllText(Path.Combine(folder, "ui-check.txt"), report.ToString(), Encoding.UTF8); }
            return passed;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChange; System.Windows.Media.CompositionTarget.Rendering -= CaptureAfterHide;
            displayDebounce.Stop(); dismissTimer.Stop(); HidePanels(); CloseTrayMenu(); if (settings != null) settings.Close();
            Native.UnregisterHotKey(messages.Handle, shortcutId); messages.Dispose(); tray.Visible = false;
            Forms.ContextMenuStrip menu = tray.ContextMenuStrip; tray.Dispose(); if (menu != null) menu.Dispose(); if (icon != null) icon.Dispose();
        }
    }
    public static class Diagnostics
    {
        public static int Run(string[] args)
        {
            string folder = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "work", "diagnostics");
            Directory.CreateDirectory(folder); StringBuilder report = new StringBuilder();
            try
            {
                report.AppendLine("WinSelect " + DateTime.Now.ToString("s"));
                report.AppendLine("Native sizes: path=" + Marshal.SizeOf(typeof(DisplayPath)) + " mode=" + Marshal.SizeOf(typeof(ModeInfo)) + " targetName=" + Marshal.SizeOf(typeof(TargetName)) + " preferred=" + Marshal.SizeOf(typeof(PreferredMode)));
                if (Marshal.SizeOf(typeof(DisplayPath)) != 72 || Marshal.SizeOf(typeof(ModeInfo)) != 64 || Marshal.SizeOf(typeof(TargetName)) != 420 || Marshal.SizeOf(typeof(PreferredMode)) != 80) throw new Exception("Native structure layout mismatch");
                DisplayService service = new DisplayService(Storage.Load()); List<DisplayInfo> list = service.Read(args[0] == "--verify");
                report.AppendLine("Connected=" + list.Count + " Active=" + list.Count(d => d.Active));
                foreach (DisplayInfo display in list) report.AppendLine(display.Name + " | " + display.Active + " | " + display.GdiName + " | " + display.Details + " | position=" + display.X + "," + display.Y + " | reference=" + display.LayoutX + "," + display.LayoutY + " | " + display.Identity);
                List<MonitorArea> monitors = Native.Monitors();
                foreach (MonitorArea monitor in monitors) report.AppendLine("Monitor " + monitor.Info.Device + " work=" + monitor.Info.Work.Left + "," + monitor.Info.Work.Top + " " + monitor.Info.Work.Width + "x" + monitor.Info.Work.Height + " scale=" + monitor.Scale);
                if (list.Count == 0 || monitors.Count == 0) throw new Exception("No displays enumerated");
                if (args[0] == "--verify")
                {
                    bool[] original = list.Select(d => d.Active).ToArray();
                    try
                    {
                        for (int i = 0; i < list.Count; i++) list[i].Active = i == 0;
                        bool blocked = false; try { service.BuildPlanFromSnapshot(list, DisplayService.Query(1), list[0].Identity, false); } catch (InvalidOperationException) { blocked = true; }
                        if (!blocked) throw new Exception("Last-display guard failed"); report.AppendLine("PASS simulated last-display guard");
                    }
                    finally { for (int i = 0; i < list.Count; i++) list[i].Active = original[i]; }
                    foreach (DisplayInfo display in list)
                    {
                        if (display.Active && list.Count(d => d.Active) == 1)
                        {
                            bool guarded = false; try { service.BuildPlan(display.Identity, false); } catch (InvalidOperationException) { guarded = true; }
                            if (!guarded) throw new Exception("Last-display guard failed"); report.AppendLine("PASS last-display guard");
                        }
                        else { DisplayPlan plan = service.BuildPlan(display.Identity, !display.Active); report.AppendLine("PASS validate-only " + display.Name + " -> " + !display.Active + " paths=" + (plan == null ? 0 : plan.Paths.Length)); }
                        if (display.Active && list.Count(d => d.Active) > 1)
                        {
                            display.Active = false;
                            try { DisplayPlan restore = service.BuildPlanFromSnapshot(list, DisplayService.Query(1), display.Identity, true); report.AppendLine("PASS hypothetical re-enable " + display.Name + " paths=" + restore.Paths.Length); }
                            finally { display.Active = true; }
                        }
                    }
                    Dictionary<string, BitmapSource> images = Captures.Take(list); report.AppendLine("Captured=" + images.Count);
                    PanelWindow panel = new PanelWindow(null, monitors[0], true, list, images);
                    FrameworkElement view = (FrameworkElement)panel.Content; Size size = new Size(panel.Width, panel.Height); view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
                    RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(view);
                    PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (FileStream f = File.Create(Path.Combine(folder, "panel.png"))) encoder.Save(f);
                    panel.Close(); report.AppendLine("PASS offscreen UI render; no SetDisplayConfig(APPLY) called");
                }
                report.AppendLine("PASS"); File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString(), Encoding.UTF8); return 0;
            }
            catch (Exception error) { report.AppendLine(error.ToString()); File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString(), Encoding.UTF8); return 1; }
        }
    }
}
