using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
namespace ReplayLite
{
    static class DocArt
    {
        static void Text(Graphics g, string text, float x, float y, float size, Color color, bool bold)
        { using (var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel)) using (var brush = new SolidBrush(color)) g.DrawString(text, font, brush, x, y); }
        static void Pill(Graphics g, string text, float x, float y, float width)
        { using (var shape = Theme.Round(new RectangleF(x, y, width, 44), 12)) using (var brush = new SolidBrush(Theme.Input)) g.FillPath(brush, shape); Text(g, text, x + 17, y + 9, 20, Theme.Text, false); }
        static int Main(string[] args)
        {
            var dir = args[0]; Directory.CreateDirectory(dir);
            using (var image = new Bitmap(1440, 440)) using (var g = Graphics.FromImage(image))
            {
                g.Clear(Theme.Background); g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new LinearGradientBrush(new Rectangle(0, 0, 1440, 440), Theme.Background, Color.FromArgb(18, 41, 43), 10f)) g.FillRectangle(brush, 0, 0, 1440, 440);
                Text(g, "ReplayLite", 65, 57, 81, Theme.Text, true);
                Text(g, "Запомни момент. Сохрани после.", 69, 171, 36, Theme.Accent, false);
                Text(g, "Последние минуты экрана — по одной горячей клавише", 70, 228, 24, Theme.Muted, false);
                Pill(g, "Windows 10 / 11", 72, 321, 209); Pill(g, "NVIDIA NVENC", 297, 321, 202); Pill(g, "Ctrl + Shift + F9", 515, 321, 221); Pill(g, "MP4", 752, 321, 90);
                using (var logo = AppIcons.Draw(240, TrayState.Recording)) g.DrawImageUnscaled(logo, 1114, 86);
                image.Save(Path.Combine(dir, "banner.png"), ImageFormat.Png);
            }
            using (var image = new Bitmap(1440, 244)) using (var g = Graphics.FromImage(image))
            {
                g.Clear(Theme.Background); g.SmoothingMode = SmoothingMode.AntiAlias;
                var states = new[] { TrayState.Idle, TrayState.Recording, TrayState.Saving, TrayState.Error };
                var names = new[] { "Выключено", "Идёт запись", "Сохранение", "Ошибка" };
                var notes = new[] { "Буфер не запущен", "Можно сохранить повтор", "Обработка действия", "Нужно проверить настройки" };
                for (int i = 0; i < 4; i++)
                {
                    int x = 20 + i * 355;
                    using (var shape = Theme.Round(new RectangleF(x, 22, 335, 201), 18)) using (var brush = new SolidBrush(Theme.Card)) g.FillPath(brush, shape);
                    using (var icon = AppIcons.Draw(74, states[i])) g.DrawImageUnscaled(icon, x + 131, 41);
                    Text(g, names[i], x + (335 - names[i].Length * 13) / 2, 126, 23, Theme.Text, true);
                    Text(g, notes[i], x + 18, 173, 18, Theme.Muted, false);
                }
                image.Save(Path.Combine(dir, "tray-states.png"), ImageFormat.Png);
            }
            return 0;
        }
    }
}
