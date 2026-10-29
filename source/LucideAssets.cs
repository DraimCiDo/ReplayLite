using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace ReplayLite
{
    static class LucideAssets
    {
        static readonly string[] Names = { "settings", "undo-2", "monitor", "folder", "video", "headphones", "keyboard", "panels-top-left", "external-link", "save", "circle", "log-out", "circle-question-mark", "chevron-down", "chevron-up", "square-play", "pause", "circle-alert", "arrow-up", "check", "download", "x", "minus", "globe", "circle-dot", "copy", "trash" };
        static readonly Bitmap Atlas = Load();
        static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();
        static Bitmap Load() { using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ReplayLite.Lucide.png")) { if (stream == null) throw new InvalidDataException("Missing embedded Lucide atlas."); using (var source = new Bitmap(stream)) return new Bitmap(source); } }
        internal static Bitmap Get(string name, int size, Color color)
        {
            string key = name + ":" + size + ":" + color.ToArgb(); Bitmap image;
            if (Cache.TryGetValue(key, out image)) return image;
            int index = Array.IndexOf(Names, name); if (index < 0) throw new ArgumentException("Unknown Lucide icon: " + name);
            image = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(image)) using (var attributes = new ImageAttributes())
            {
                var matrix = new ColorMatrix(new[] { new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, color.A / 255f, 0 }, new float[] { color.R / 255f, color.G / 255f, color.B / 255f, 0, 1 } });
                attributes.SetColorMatrix(matrix); graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(Atlas, new Rectangle(0, 0, size, size), index * 64, 0, 64, 64, GraphicsUnit.Pixel, attributes);
            }
            Cache.Add(key, image); return image;
        }
        internal static void Draw(Graphics graphics, string name, Rectangle rectangle, Color color) { graphics.DrawImageUnscaled(Get(name, rectangle.Width, color), rectangle.Location); }
        internal static void Clear() { foreach (var image in Cache.Values) image.Dispose(); Cache.Clear(); }
    }
}
