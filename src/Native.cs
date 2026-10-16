using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WinSelect
{
    [StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; public override string ToString() { return High.ToString("X8") + Low.ToString("X8"); } }
    [StructLayout(LayoutKind.Sequential)] public struct PointL { public int X, Y; public PointL(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] public struct Rational { public uint Num, Den; }
    [StructLayout(LayoutKind.Sequential)] public struct SizeL { public uint Width, Height; }
    [StructLayout(LayoutKind.Sequential)] public struct SourceMode { public uint Width, Height, PixelFormat; public PointL Position; }
    [StructLayout(LayoutKind.Sequential)] public struct VideoSignal { public ulong PixelRate; public Rational HSync, VSync; public SizeL Active, Total; public uint VideoStandard, Scanline; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] public struct ModeInfo
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid Adapter;
        [FieldOffset(16)] public SourceMode Source;
        [FieldOffset(16)] public VideoSignal Target;
    }
    [StructLayout(LayoutKind.Sequential)] public struct PathSource { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] public struct PathTarget
    {
        public Luid Adapter;
        public uint Id, ModeIndex, Technology, Rotation, Scaling;
        public Rational Refresh;
        public uint Scanline;
        public int Available;
        public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] public struct DisplayPath { public PathSource Source; public PathTarget Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct DeviceHeader { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct TargetName
    {
        public DeviceHeader Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Instance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct SourceName
    {
        public DeviceHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiName;
    }
    [StructLayout(LayoutKind.Sequential)] public struct PreferredMode { public DeviceHeader Header; public uint Width, Height; public VideoSignal Signal; }
    [StructLayout(LayoutKind.Sequential)] public struct RectI
    {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct MonitorInfo
    {
        public uint Size; public RectI Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    public class MonitorArea { public IntPtr Handle; public MonitorInfo Info; public double Scale; }
    public static class Native
    {
        public const uint Invalid = 0xffffffff;
        public const uint Validate = 0x40, Apply = 0x80, Supplied = 0x20, SaveDatabase = 0x200, AllowChanges = 0x400;
        [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [In, Out] DisplayPath[] paths, ref uint modeCount, [In, Out] ModeInfo[] modes, IntPtr topology);
        [DllImport("user32.dll")] public static extern int SetDisplayConfig(uint pathCount, [In] DisplayPath[] paths, uint modeCount, [In] ModeInfo[] modes, uint flags);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int GetTargetName(ref TargetName name);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int GetSourceName(ref SourceName name);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int GetPreferredMode(ref PreferredMode mode);
        public delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, ref RectI rect, IntPtr data);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(PointL pt, uint flags);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out PointL pt);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RectI rect);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(PointL point);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetWindowText(IntPtr hwnd, string text);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string className, string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr handle);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle);
        public static DeviceHeader Header(uint type, int size, Luid adapter, uint id) { return new DeviceHeader { Type = type, Size = (uint)size, Adapter = adapter, Id = id }; }
        public static string TargetKey(DisplayPath p) { return p.Target.Adapter + ":" + p.Target.Id; }
        public static string SourceKey(DisplayPath p) { return p.Source.Adapter + ":" + p.Source.Id; }
        public static List<MonitorArea> Monitors()
        {
            List<MonitorArea> result = new List<MonitorArea>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr h, IntPtr dc, ref RectI r, IntPtr data) {
                MonitorInfo info = new MonitorInfo { Size = (uint)Marshal.SizeOf(typeof(MonitorInfo)) };
                if (GetMonitorInfo(h, ref info)) { uint x = 96, y = 96; try { GetDpiForMonitor(h, 0, out x, out y); } catch (EntryPointNotFoundException) { } result.Add(new MonitorArea { Handle = h, Info = info, Scale = Math.Max(1, x) / 96.0 }); }
                return true;
            }, IntPtr.Zero);
            return result;
        }
        public static byte[] Bytes<T>(T value) where T : struct
        {
            int n = Marshal.SizeOf(typeof(T)); IntPtr p = Marshal.AllocHGlobal(n);
            try { Marshal.StructureToPtr(value, p, false); byte[] bytes = new byte[n]; Marshal.Copy(p, bytes, 0, n); return bytes; } finally { Marshal.FreeHGlobal(p); }
        }
        public static T FromBytes<T>(byte[] bytes) where T : struct
        {
            int n = Marshal.SizeOf(typeof(T)); if (bytes == null || bytes.Length != n) throw new ArgumentException("Invalid native structure size");
            IntPtr p = Marshal.AllocHGlobal(n); try { Marshal.Copy(bytes, 0, p, n); return (T)Marshal.PtrToStructure(p, typeof(T)); } finally { Marshal.FreeHGlobal(p); }
        }
    }
}
