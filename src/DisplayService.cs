using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace WinSelect
{
    public class DisplayInfo
    {
        public string Identity, Name, GdiName, Connection;
        public DisplayPath Path;
        public bool Active;
        public int X, Y, Width, Height, LayoutX, LayoutY;
        public double Refresh;
        public DisplayMemory Memory;
        public string Details { get { return (Width > 0 ? Width + " × " + Height : "分辨率未知") + (Refresh > 0 ? "  ·  " + Refresh.ToString("0.##") + " Hz" : "") + "  ·  " + Connection; } }
    }
    public class Snapshot { public DisplayPath[] Paths; public ModeInfo[] Modes; }
    public class DisplayPlan { public DisplayPath[] Paths; public ModeInfo[] Modes; public bool AllowModeChanges; }
    public sealed class DisplayService
    {
        readonly Preferences preferences;
        public DisplayService(Preferences p) { preferences = p; }
        public static Snapshot Query(uint flags)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                uint pc, mc; int error = Native.GetDisplayConfigBufferSizes(flags, out pc, out mc);
                if (error != 0) throw new Win32Exception(error, "读取显示器配置失败");
                DisplayPath[] paths = new DisplayPath[pc]; ModeInfo[] modes = new ModeInfo[mc];
                error = Native.QueryDisplayConfig(flags, ref pc, paths, ref mc, modes, IntPtr.Zero);
                if (error == 122) continue;
                if (error != 0) throw new Win32Exception(error, "读取显示器路径失败");
                Array.Resize(ref paths, (int)pc); Array.Resize(ref modes, (int)mc);
                return new Snapshot { Paths = paths, Modes = modes };
            }
            throw new InvalidOperationException("显示器正在连接或断开，请稍后重试。");
        }
        static string Technology(uint value)
        {
            switch (value) { case 0: return "VGA"; case 4: return "DVI"; case 5: return "HDMI"; case 6: return "内置屏幕"; case 10: return "DisplayPort"; case 11: return "内置 DisplayPort"; case 15: return "Miracast"; case 16: return "间接连接"; case 17: return "无线显示"; case 0x80000000: return "内置屏幕"; default: return "显示接口 " + value; }
        }
        public List<DisplayInfo> Read(bool remember)
        {
            Snapshot s = Query(1);
            List<DisplayInfo> result = new List<DisplayInfo>();
            foreach (IGrouping<string, DisplayPath> group in s.Paths.Where(p => p.Target.Available != 0).GroupBy(Native.TargetKey))
            {
                DisplayPath path = group.OrderByDescending(p => (p.Flags & 1) != 0).First();
                TargetName name = new TargetName { Header = Native.Header(2, Marshal.SizeOf(typeof(TargetName)), path.Target.Adapter, path.Target.Id) };
                Native.GetTargetName(ref name);
                string identity = !String.IsNullOrEmpty(name.DevicePath) ? name.DevicePath : Native.TargetKey(path);
                SourceName sourceName = new SourceName { Header = Native.Header(1, Marshal.SizeOf(typeof(SourceName)), path.Source.Adapter, path.Source.Id) };
                Native.GetSourceName(ref sourceName);
                DisplayMemory memory = preferences.Displays.Find(m => String.Equals(m.Identity, identity, StringComparison.OrdinalIgnoreCase));
                bool active = (path.Flags & 1) != 0;
                SourceMode source = new SourceMode(); VideoSignal target = new VideoSignal();
                bool hasSource = active && path.Source.ModeIndex < s.Modes.Length && s.Modes[path.Source.ModeIndex].Type == 1;
                if (hasSource) source = s.Modes[path.Source.ModeIndex].Source;
                else if (memory != null) try { source = Native.FromBytes<ModeInfo>(memory.SourceBytes).Source; } catch { }
                if (active && path.Target.ModeIndex < s.Modes.Length && s.Modes[path.Target.ModeIndex].Type == 2) target = s.Modes[path.Target.ModeIndex].Target;
                else if (memory != null) try { target = Native.FromBytes<ModeInfo>(memory.TargetBytes).Target; } catch { }
                if (source.Width == 0)
                {
                    PreferredMode preferred = new PreferredMode { Header = Native.Header(3, Marshal.SizeOf(typeof(PreferredMode)), path.Target.Adapter, path.Target.Id) };
                    if (Native.GetPreferredMode(ref preferred) == 0) { target = preferred.Signal; source.Width = preferred.Width; source.Height = preferred.Height; source.PixelFormat = 4; }
                }
                Rational rate = target.VSync.Den != 0 ? target.VSync : path.Target.Refresh;
                result.Add(new DisplayInfo {
                    Identity = identity, Name = String.IsNullOrEmpty(name.FriendlyName) ? "显示器 " + (result.Count + 1) : name.FriendlyName,
                    GdiName = sourceName.GdiName, Path = path, Active = active, Memory = memory,
                    X = source.Position.X, Y = source.Position.Y, Width = (int)source.Width, Height = (int)source.Height,
                    LayoutX = memory != null ? memory.X : source.Position.X, LayoutY = memory != null ? memory.Y : source.Position.Y,
                    Refresh = rate.Den > 0 ? rate.Num / (double)rate.Den : 0, Connection = Technology(path.Target.Technology)
                });
            }
            // Full configurations are the layout reference. Partial configurations must not
            // overwrite it when Windows moves the surviving display back to (0,0).
            bool complete = result.Count > 0 && result.All(d => d.Active);
            bool changed = false;
            foreach (DisplayInfo d in result)
            {
                if (d.Active && (complete || d.Memory == null) && remember)
                {
                    if (d.Memory == null) { d.Memory = new DisplayMemory { Identity = d.Identity }; preferences.Displays.Add(d.Memory); }
                    d.Memory.X = d.X; d.Memory.Y = d.Y; d.Memory.Primary = d.X == 0 && d.Y == 0; d.Memory.SourceId = d.Path.Source.Id;
                    d.Memory.Rotation = d.Path.Target.Rotation; d.Memory.Scaling = d.Path.Target.Scaling; d.Memory.Scanline = d.Path.Target.Scanline;
                    if (d.Path.Source.ModeIndex < s.Modes.Length) d.Memory.SourceBytes = Native.Bytes(s.Modes[d.Path.Source.ModeIndex]);
                    if (d.Path.Target.ModeIndex < s.Modes.Length) d.Memory.TargetBytes = Native.Bytes(s.Modes[d.Path.Target.ModeIndex]);
                    d.LayoutX = d.X; d.LayoutY = d.Y; changed = true;
                }
            }
            int right = result.Where(d => d.Active).Select(d => d.LayoutX + d.Width).DefaultIfEmpty(0).Max();
            foreach (DisplayInfo d in result.Where(d => !d.Active && d.Memory == null)) { d.LayoutX = right; right += Math.Max(d.Width, 1920); }
            if (changed) Storage.Save(preferences);
            return result.OrderBy(d => d.LayoutX).ThenBy(d => d.LayoutY).ToList();
        }
        public DisplayPlan BuildPlan(string identity, bool enabled)
        {
            List<DisplayInfo> displays = Read(true);
            return BuildPlanFromSnapshot(displays, Query(1), identity, enabled);
        }
        internal DisplayPlan BuildPlanFromSnapshot(List<DisplayInfo> displays, Snapshot s, string identity, bool enabled)
        {
            DisplayInfo selected = displays.Find(d => String.Equals(d.Identity, identity, StringComparison.OrdinalIgnoreCase));
            if (selected == null) throw new InvalidOperationException("该显示器已断开，请重新呼出面板。");
            if (selected.Active == enabled) return null;
            List<DisplayInfo> desired = displays.Where(d => d.Active || d == selected && enabled).Where(d => d != selected || enabled).ToList();
            if (desired.Count == 0) throw new InvalidOperationException("至少保留一块屏幕开启。");
            List<DisplayPath> paths = new List<DisplayPath>(); List<ModeInfo> modes = new List<ModeInfo>();
            Dictionary<string, uint> usedSources = new Dictionary<string, uint>();
            HashSet<string> occupied = new HashSet<string>(desired.Where(d => d.Active).Select(d => Native.SourceKey(d.Path)));
            DisplayInfo anchor = desired.FirstOrDefault(d => d.Memory != null && d.Memory.Primary) ?? desired.FirstOrDefault(d => d.Active && d.X == 0 && d.Y == 0) ?? desired[0];
            foreach (DisplayInfo d in desired.OrderByDescending(d => d.Active))
            {
                DisplayPath p;
                List<DisplayPath> candidates = s.Paths.Where(v => Native.TargetKey(v) == Native.TargetKey(d.Path) && v.Target.Available != 0).ToList();
                if (d.Active) p = candidates.First(v => (v.Flags & 1) != 0);
                else
                {
                    List<DisplayPath> free = candidates.Where(v => !occupied.Contains(Native.SourceKey(v))).OrderByDescending(v => d.Memory != null && v.Source.Id == d.Memory.SourceId).ToList();
                    if (free.Count == 0) throw new InvalidOperationException("显卡没有可用的独立输出路径，无法启用该显示器。");
                    p = free[0]; occupied.Add(Native.SourceKey(p));
                }
                uint sourceIndex;
                string sourceKey = Native.SourceKey(p);
                if (!usedSources.TryGetValue(sourceKey, out sourceIndex))
                {
                    ModeInfo mode;
                    if (d.Active && p.Source.ModeIndex < s.Modes.Length) mode = s.Modes[p.Source.ModeIndex];
                    else if (d.Memory != null && d.Memory.SourceBytes != null) mode = Native.FromBytes<ModeInfo>(d.Memory.SourceBytes);
                    else mode = new ModeInfo { Source = new SourceMode { Width = (uint)Math.Max(1, d.Width), Height = (uint)Math.Max(1, d.Height), PixelFormat = 4 } };
                    mode.Type = 1; mode.Id = p.Source.Id; mode.Adapter = p.Source.Adapter;
                    SourceMode sm = mode.Source; sm.Position = new PointL(d.LayoutX - anchor.LayoutX, d.LayoutY - anchor.LayoutY); mode.Source = sm;
                    sourceIndex = (uint)modes.Count; modes.Add(mode); usedSources.Add(sourceKey, sourceIndex);
                }
                uint targetIndex = Native.Invalid;
                if (d.Active && p.Target.ModeIndex < s.Modes.Length)
                {
                    targetIndex = (uint)modes.Count; modes.Add(s.Modes[p.Target.ModeIndex]);
                }
                else if (d.Memory != null && d.Memory.TargetBytes != null)
                {
                    ModeInfo tm = Native.FromBytes<ModeInfo>(d.Memory.TargetBytes); tm.Type = 2; tm.Id = p.Target.Id; tm.Adapter = p.Target.Adapter;
                    targetIndex = (uint)modes.Count; modes.Add(tm);
                }
                if (d.Memory != null && d.Memory.TargetBytes != null)
                {
                    VideoSignal saved = Native.FromBytes<ModeInfo>(d.Memory.TargetBytes).Target;
                    if (saved.VSync.Den > 0) p.Target.Refresh = saved.VSync;
                }
                if (!d.Active && d.Memory != null)
                {
                    if (d.Memory.Rotation != 0) p.Target.Rotation = d.Memory.Rotation;
                    if (d.Memory.Scaling != 0) p.Target.Scaling = d.Memory.Scaling;
                    p.Target.Scanline = d.Memory.Scanline;
                }
                p.Source.ModeIndex = sourceIndex; p.Target.ModeIndex = targetIndex; p.Flags = 1;
                paths.Add(p);
            }
            DisplayPlan plan = new DisplayPlan { Paths = paths.ToArray(), Modes = modes.ToArray() };
            int error = Native.SetDisplayConfig((uint)plan.Paths.Length, plan.Paths, (uint)plan.Modes.Length, plan.Modes, Native.Validate | Native.Supplied);
            if (error != 0 && enabled)
            {
                // Only the newly enabled target loses its exact timing on fallback.
                // Existing outputs keep their complete source/target modes.
                for (int i = 0; i < plan.Paths.Length; i++)
                {
                    DisplayPath p = plan.Paths[i];
                    if (Native.TargetKey(p) == Native.TargetKey(selected.Path)) { p.Target.ModeIndex = Native.Invalid; p.Target.Refresh = new Rational(); p.Target.Scanline = 0; plan.Paths[i] = p; }
                }
                plan.AllowModeChanges = true;
                error = Native.SetDisplayConfig((uint)plan.Paths.Length, plan.Paths, (uint)plan.Modes.Length, plan.Modes, Native.Validate | Native.Supplied | Native.AllowChanges);
            }
            if (error != 0) throw new Win32Exception(error, "Windows 无法应用这组显示配置，当前输出未主动更改");
            return plan;
        }
        public void SetEnabled(string identity, bool enabled)
        {
            DisplayPlan plan = BuildPlan(identity, enabled); if (plan == null) return;
            uint flags = Native.Apply | Native.Supplied | Native.SaveDatabase | (plan.AllowModeChanges ? Native.AllowChanges : 0);
            int error = Native.SetDisplayConfig((uint)plan.Paths.Length, plan.Paths, (uint)plan.Modes.Length, plan.Modes, flags);
            if (error != 0) throw new Win32Exception(error, "切换失败，Windows 返回错误 " + error);
            List<DisplayInfo> actual = Read(false);
            DisplayInfo target = actual.Find(d => String.Equals(d.Identity, identity, StringComparison.OrdinalIgnoreCase));
            if (target == null || target.Active != enabled) throw new InvalidOperationException("Windows 未确认目标屏幕的输出状态，请检查显示器连接后重试。");
        }
    }
}
