using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReplayLite
{
    public sealed partial class Settings
    {
        public string Container = "mp4", FilenameTemplate = "Replay-{date}_{time}", Codec = "h264", RateControl = "cbr", Resolution = "native", Palette = "mint";
        public bool CheckUpdatesOnStartup = true, CloseToTray = true, StartMinimized;
        public int FeedbackVolume = 60, FeedbackTone;
        internal void LoadPreference(string key, string value)
        {
            bool flag; int number;
            switch (key)
            {
                case "Container": if (new[] { "mp4", "mkv" }.Contains(value)) Container = value; break;
                case "FilenameTemplate": if (ValidTemplate(value)) FilenameTemplate = value; break;
                case "Codec": if (new[] { "h264", "hevc", "av1" }.Contains(value)) Codec = value; break;
                case "RateControl": if (new[] { "cbr", "vbr" }.Contains(value)) RateControl = value; break;
                case "Resolution": if (new[] { "native", "1080", "720" }.Contains(value)) Resolution = value; break;
                case "Palette": if (new[] { "mint", "ocean", "violet", "light" }.Contains(value)) Palette = value; break;
                case "CheckUpdatesOnStartup": if (bool.TryParse(value, out flag)) CheckUpdatesOnStartup = flag; break;
                case "CloseToTray": if (bool.TryParse(value, out flag)) CloseToTray = flag; break;
                case "StartMinimized": if (bool.TryParse(value, out flag)) StartMinimized = flag; break;
                case "FeedbackVolume": if (int.TryParse(value, out number)) FeedbackVolume = Math.Max(0, Math.Min(100, number)); break;
                case "FeedbackTone": if (int.TryParse(value, out number)) FeedbackTone = Math.Max(0, Math.Min(2, number)); break;
            }
        }
        internal string[] PreferenceLines()
        {
            return new[] { "Container=" + Container, "FilenameTemplate=" + FilenameTemplate, "Codec=" + Codec, "RateControl=" + RateControl, "Resolution=" + Resolution, "Palette=" + Palette,
                "CheckUpdatesOnStartup=" + CheckUpdatesOnStartup, "CloseToTray=" + CloseToTray, "StartMinimized=" + StartMinimized, "FeedbackVolume=" + FeedbackVolume, "FeedbackTone=" + FeedbackTone };
        }
        internal void ValidatePreferences()
        {
            if (!ValidTemplate(FilenameTemplate)) throw new ArgumentException("Имя: используйте {date}, {time}, {monitor}; без расширения и символов \\ / : * ? \" < > |.");
            if (!new[] { "mp4", "mkv" }.Contains(Container) || !new[] { "h264", "hevc", "av1" }.Contains(Codec) || !new[] { "cbr", "vbr" }.Contains(RateControl) || !new[] { "native", "1080", "720" }.Contains(Resolution)) throw new ArgumentException("Недопустимые параметры видео.");
        }
        internal static bool ValidTemplate(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 100 || text.EndsWith(".") || text.EndsWith(" ")) return false;
            string stripped = text.Replace("{date}", "2026-01-01").Replace("{time}", "12-30-00-000").Replace("{monitor}", "1").Replace("{YYYY-MM-DD_HH-MM-SS}", "2026-01-01_12-30-00");
            if (stripped.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || stripped.IndexOfAny(new[] { '{', '}' }) >= 0) return false;
            string stem = stripped.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            return !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem) && stripped != "." && stripped != "..";
        }
        internal string ClipStem(DateTime now)
        {
            ValidatePreferences();
            return FilenameTemplate.Replace("{date}", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Replace("{time}", now.ToString("HH-mm-ss-fff", CultureInfo.InvariantCulture)).Replace("{monitor}", (Display + 1).ToString(CultureInfo.InvariantCulture)).Replace("{YYYY-MM-DD_HH-MM-SS}", now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
        }
        internal string ClipPath(DateTime now)
        {
            string stem = ClipStem(now), path = Path.Combine(Folder, stem + "." + Container);
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(Folder, stem + "-" + i.ToString(CultureInfo.InvariantCulture) + "." + Container);
            return path;
        }
        internal string ScaleFilter
        {
            get { return Resolution == "native" ? "" : ",hwdownload,format=bgra,scale=w='min(iw," + (Resolution == "720" ? "1280" : "1920") + ")':h='min(ih," + Resolution + ")':force_original_aspect_ratio=decrease:force_divisible_by=2,format=nv12"; }
        }
    }
    static class SaveFeedback
    {
        internal static byte[] Wave(int tone, int volume)
        {
            const int rate = 22050, count = 5512;
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                for (int i = 0; i < count; i++) { double t = (double)i / rate, frequency = tone == 1 ? (i < count / 2 ? 660 : 880) : tone == 2 ? 520 : 880; double envelope = Math.Min(1, t / .01) * Math.Pow(1 - (double)i / count, 2); writer.Write((short)(Math.Sin(t * frequency * 2 * Math.PI) * envelope * 12000 * Math.Max(0, Math.Min(100, volume)) / 100)); }
                writer.Flush(); return memory.ToArray();
            }
        }
        internal static Task PlayAsync(int tone, int volume)
        {
            // One short playback per explicit action; no permanent output device or timer.
            return Task.Run(delegate { try { using (var stream = new MemoryStream(Wave(tone, volume))) using (var player = new System.Media.SoundPlayer(stream)) player.PlaySync(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        }
    }
}
