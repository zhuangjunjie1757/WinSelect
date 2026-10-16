using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace WinSelect
{
    public class DisplayMemory
    {
        public string Identity;
        public int X, Y;
        public bool Primary;
        public byte[] SourceBytes, TargetBytes;
        public uint SourceId;
        public uint Rotation, Scaling, Scanline;
    }
    public class Preferences
    {
        public uint Modifiers = 3;
        public uint Key = 0x44;
        public List<DisplayMemory> Displays = new List<DisplayMemory>();
    }
    public static class Storage
    {
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinSelect");
        public static readonly string ConfigPath = Path.Combine(Folder, "settings.xml");
        public static Preferences Load()
        {
            try { if (File.Exists(ConfigPath)) using (FileStream f = File.OpenRead(ConfigPath)) { Preferences p = (Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(f); if (p.Displays == null) p.Displays = new List<DisplayMemory>(); if (p.Key == 0) { p.Key = 0x44; p.Modifiers = 3; } return p; } }
            catch (Exception e) { Log(e); }
            return new Preferences();
        }
        public static void Save(Preferences p)
        {
            Directory.CreateDirectory(Folder); string tmp = ConfigPath + ".tmp";
            using (FileStream f = File.Create(tmp)) new XmlSerializer(typeof(Preferences)).Serialize(f, p);
            if (File.Exists(ConfigPath)) File.Replace(tmp, ConfigPath, null); else File.Move(tmp, ConfigPath);
        }
        public static void Log(Exception error)
        {
            try { Directory.CreateDirectory(Folder); string file = Path.Combine(Folder, "WinSelect.log"); if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) File.WriteAllText(file, ""); File.AppendAllText(file, DateTime.Now.ToString("s") + " " + error + Environment.NewLine); } catch { }
        }
    }
}
