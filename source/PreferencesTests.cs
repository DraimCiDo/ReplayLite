using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ReplayLite
{
    static class PreferencesTests
    {
        internal static async Task UnsupportedCodec(StringBuilder report)
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".deps-cache", "unsupported-" + Guid.NewGuid().ToString("N"));
            var config = new Settings { Folder = root, BufferFolder = root, Audio = "none", Codec = "av1" };
            using (var recorder = new Recorder(config, (args, session, log) =>
            {
                log("[av1_nvenc] Codec not supported");
                return Recorder.Launch(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe"), new[] { "-v", "quiet", "-replaylite-unsupported-codec-fixture" }, session, log);
            }))
            {
                bool rejected = false;
                try { await recorder.StartAsync(); } catch (IOException error) { rejected = !CaptureRecovery.ShouldRetry(error) && error.Message.Contains("AV1"); }
                if (!rejected || recorder.Running || recorder.NeedsRecovery) throw new Exception("Unsupported NVENC codec incorrectly enters recovery loop.");
            }
            if (CaptureRecovery.UnsupportedEncoder("[av1_nvenc] Could not open encoder before EOF") || CaptureRecovery.UnsupportedEncoder("[Parsed_ddagrab] Desktop duplication access denied\n[av1_nvenc] Codec not supported")) throw new Exception("Upstream desktop failure mistaken for unsupported codec.");
            report.AppendLine("UNSUPPORTED_NVENC_CODEC_FAILS_WITHOUT_RETRY_OR_CPU_FALLBACK: PASS");
        }
        internal static void Configuration(StringBuilder report, string root)
        {
            Directory.CreateDirectory(root); string file = Path.Combine(root, "preferences.txt");
            var settings = new Settings { Folder = root, Container = "mkv", Codec = "av1", RateControl = "vbr", Resolution = "720", Palette = "violet", FilenameTemplate = "Clip-{monitor}-{date}-{time}", CheckUpdatesOnStartup = false, CloseToTray = false, StartMinimized = true, FeedbackVolume = 17, FeedbackTone = 2, Audio = "microphone" };
            settings.SaveToFile(file); var loaded = Settings.LoadFromFile(file);
            if (string.Join("\n", settings.PreferenceLines()) != string.Join("\n", loaded.PreferenceLines()) || loaded.Audio != "microphone") throw new Exception("New preferences failed round-trip.");
            var now = new DateTime(2026, 10, 29, 13, 14, 15); string first = settings.ClipPath(now); File.WriteAllText(first, "keep"); string second = settings.ClipPath(now);
            if (second == first || !second.EndsWith("-2.mkv") || File.ReadAllText(first) != "keep") throw new Exception("Filename collision overwrites output.");
            foreach (string invalid in new[] { "", "../clip", "C:\\clip", "CON", "NUL.mp4", "{unknown}", "a\nclip", "clip.", "clip " }) if (Settings.ValidTemplate(invalid)) throw new Exception("Unsafe template accepted: " + invalid);
            byte[] original = File.ReadAllBytes(file); settings.FilenameTemplate = "../clip";
            bool rejected = false; try { settings.SaveToFile(file); } catch (ArgumentException) { rejected = true; }
            if (!rejected || Convert.ToBase64String(original) != Convert.ToBase64String(File.ReadAllBytes(file))) throw new Exception("Invalid template replaced previous preferences.");
            File.WriteAllText(file, "Codec=unknown\nContainer=exe\nResolution=-1\nPalette=bad\nFilenameTemplate=../clip\nFeedbackVolume=500\nFeedbackTone=-5\nCheckUpdatesOnStartup=bad\n"); loaded = Settings.LoadFromFile(file);
            if (loaded.Codec != "h264" || loaded.Container != "mp4" || loaded.Resolution != "native" || loaded.Palette != "mint" || !Settings.ValidTemplate(loaded.FilenameTemplate) || loaded.FeedbackVolume != 100 || loaded.FeedbackTone != 0 || !loaded.CheckUpdatesOnStartup) throw new Exception("Invalid preferences have unsafe fallback.");
            byte[] silent = SaveFeedback.Wave(0, 0), loud = SaveFeedback.Wave(0, 100); bool nonzero = false;
            for (int i = 44; i < silent.Length; i += 2) { if (BitConverter.ToInt16(silent, i) != 0) throw new Exception("Silent feedback is audible."); if (BitConverter.ToInt16(loud, i) != 0) nonzero = true; }
            if (!nonzero || silent.Length > 12000) throw new Exception("Feedback amplitude or bounded sample size failed.");
            report.AppendLine("PREFERENCES_ROUNDTRIP_BAD_VALUES_TEMPLATE_COLLISION_ATOMIC_REJECTION_FEEDBACK_VOLUME: PASS");
        }
        internal static async Task<int> Hardware(string root)
        {
            Directory.CreateDirectory(root); var report = new StringBuilder();
            try
            {
                Configuration(report, Path.Combine(root, "config"));
                string[][] cases = { new[] { "h264", "mp4", "native", "cbr", "none" }, new[] { "hevc", "mkv", "native", "vbr", "none" }, new[] { "av1", "mp4", "native", "cbr", "none" }, new[] { "av1", "mkv", "720", "vbr", "microphone" }, new[] { "h264", "mp4", "1080", "vbr", "system" } };
                foreach (var test in cases)
                {
                    var config = new Settings { Seconds = 10, Folder = root, BufferFolder = Path.Combine(root, "buffer"), Codec = test[0], Container = test[1], Resolution = test[2], RateControl = test[3], Audio = test[4], FilenameTemplate = "Test-" + string.Join("-", test) + "-{time}" };
                    if (config.Codec == "hevc") config.Fps = 60;
                    var monitor = MonitorCatalog.Enumerate().Find(m => m.CanCapture); if (monitor == null) throw new Exception("No NVIDIA monitor available."); config.Display = monitor.Output; config.DisplayAdapter = monitor.Adapter; config.MonitorId = monitor.Id; config.Rotation = monitor.Rotation;
                    using (var recorder = new Recorder(config))
                    {
                        await recorder.StartAsync(); await Task.Delay(2200);
                        if (config.Codec == "av1" && config.Resolution == "native")
                        {
                            using (var child = Process.GetProcessById(recorder.ProcessId)) { child.Kill(); child.WaitForExit(); }
                            await recorder.RecoverAsync(System.Threading.CancellationToken.None);
                            if (!recorder.Running || recorder.BufferResetOnRecovery || recorder.BufferedSeconds <= 0) throw new Exception("AV1 recovery lost compatible segments.");
                            report.AppendLine("AV1_MATROSKA_SEGMENTS_PROCESS_LOSS_RECOVERY_RETAINS_BUFFER: PASS");
                        }
                        string validTemplate = config.FilenameTemplate; config.FilenameTemplate = "../bad"; bool rejected = false;
                        try { await recorder.SaveAsync(); } catch (ArgumentException) { rejected = true; }
                        if (!rejected || recorder.BufferedSeconds <= 0) throw new Exception("Bad export lost buffered footage."); config.FilenameTemplate = validTemplate;
                        string clip = await recorder.SaveAsync(); if (!File.Exists(clip) || !clip.EndsWith("." + config.Container)) throw new Exception("Output missing.");
                        string info = await Probe(clip); string codec = config.Codec == "hevc" ? "hevc" : config.Codec == "av1" ? "av1" : "h264";
                        if (!info.Contains("codec_name=" + codec)) throw new Exception("Wrong output codec: " + info);
                        if (config.Resolution != "native")
                        {
                            int width = 0, height = 0; foreach (string line in info.Split('\n')) { if (line.StartsWith("width=")) int.TryParse(line.Substring(6).Trim(), out width); if (line.StartsWith("height=")) int.TryParse(line.Substring(7).Trim(), out height); }
                            if (width <= 0 || height <= 0 || height > int.Parse(config.Resolution, CultureInfo.InvariantCulture) || width > (config.Resolution == "720" ? 1280 : 1920)) throw new Exception("Scaled resolution ignored: " + info);
                        }
                        var log = new StringBuilder(); using (var decoder = Recorder.Launch(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe"), new[] { "-v", "error", "-i", clip, "-f", "null", "-" }, root, line => { lock (log) log.AppendLine(line); }))
                        { await Task.Run(() => { if (!decoder.WaitForExit(30000)) { decoder.Kill(); throw new TimeoutException("Decode timeout."); } decoder.WaitForExit(); }); if (decoder.ExitCode != 0 || log.Length != 0) throw new Exception("Decode failed: " + log); }
                        await recorder.StopAsync(); if (Directory.GetDirectories(config.BufferFolder, "buffer-*").Length != 0) throw new Exception("Session not cleaned.");
                        report.AppendLine(string.Join("/", test) + ": RECORD_EXPORT_CODEC_DIMENSIONS_DECODE_FAILED_SAVE_RETENTION_STOP PASS " + info.Replace('\n', ' ').Trim());
                    }
                    File.WriteAllText(Path.Combine(root, "preferences-hardware.txt"), report.ToString(), new UTF8Encoding(false));
                }
                return 0;
            }
            catch (Exception error) { report.AppendLine(error.ToString()); return 1; }
            finally { File.WriteAllText(Path.Combine(root, "preferences-hardware.txt"), report.ToString(), new UTF8Encoding(false)); }
        }
        static async Task<string> Probe(string clip)
        {
            var output = new StringBuilder();
            using (var process = new Process { StartInfo = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffprobe.exe"), "-v error -select_streams v:0 -show_entries stream=codec_name,width,height -of default=nw=1 " + Recorder.Quote(clip)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } })
            { process.Start(); var read = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); await Task.Run(() => { if (!process.WaitForExit(15000)) { process.Kill(); throw new TimeoutException("Probe timeout."); } }); if (process.ExitCode != 0) throw new IOException(await error); output.Append(await read); }
            return output.ToString();
        }
    }
}
