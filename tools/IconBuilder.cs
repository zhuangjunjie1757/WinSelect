using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Native vector artwork, rasterised independently at every Windows icon size.
// Restrained system-style strokes on transparent pixels, with no coloured tile.
static class IconBuilder
{
    static GraphicsPath Round(float x, float y, float w, float h, float r)
    {
        GraphicsPath p = new GraphicsPath(); float d = 2 * r;
        p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    static Bitmap Draw(int size)
    {
        const int sample = 4;
        using (Bitmap big = new Bitmap(size * sample, size * sample, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(big))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.ScaleTransform(size * sample / 32f, size * sample / 32f);
                using (Pen stroke = new Pen(Color.FromArgb(105, 124, 150), size <= 20 ? 2.15f : 1.8f))
                {
                    stroke.StartCap = LineCap.Round; stroke.EndCap = LineCap.Round; stroke.LineJoin = LineJoin.Round;
                    // Leave a clean gap around the front screen instead of stacking
                    // several dark strokes in the overlapping region at tray sizes.
                    using (Region exposed = new Region(new RectangleF(0, 0, 32, 32)))
                    using (GraphicsPath gap = Round(10, 10, 21, 16, 3))
                    using (GraphicsPath back = Round(3, 5, 17, 12, 2))
                    {
                        exposed.Exclude(gap); g.SetClip(exposed, CombineMode.Replace); g.DrawPath(stroke, back); g.ResetClip();
                    }
                    using (GraphicsPath front = Round(12, 12, 17, 12, 2)) g.DrawPath(stroke, front);
                    g.DrawLine(stroke, 20.5f, 24, 20.5f, 27);
                    g.DrawLine(stroke, 16, 27, 25, 27);
                }
            }
            Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(big, new Rectangle(0, 0, size, size));
            }
            return result;
        }
    }
    static void Main(string[] args)
    {
        string root = args[0]; Directory.CreateDirectory(Path.Combine(root, "assets"));
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 }; List<byte[]> frames = new List<byte[]>();
        foreach (int size in sizes) using (Bitmap image = Draw(size)) using (MemoryStream bytes = new MemoryStream())
        {
            image.Save(bytes, ImageFormat.Png); frames.Add(bytes.ToArray());
            if (size == 256 || size == 32 || size == 16) image.Save(Path.Combine(root, "assets", "icon-" + size + ".png"), ImageFormat.Png);
        }
        using (BinaryWriter writer = new BinaryWriter(File.Create(Path.Combine(root, "WinSelect.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length); int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
            }
            foreach (byte[] bytes in frames) writer.Write(bytes);
        }
    }
}
