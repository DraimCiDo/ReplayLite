using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReplayLite
{
    static class SelfTest
    {
        public static void JobExitTest(string marker)
        {
            var ffmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe");
            var child = Recorder.Launch(ffmpeg, new[] { "-v", "error", "-re", "-f", "lavfi", "-i", "color=s=64x64:r=1", "-t", "30", "-f", "null", "-" }, AppDomain.CurrentDomain.BaseDirectory, null);
            File.WriteAllText(marker, child.Id.ToString(CultureInfo.InvariantCulture));
            System.Threading.Thread.Sleep(500);
            // Deliberately skip Dispose and normal Stop. OS handle closure must terminate the child.
            Environment.Exit(0);
        }
        public static async Task<int> Run(string[] args)
        {
            string root = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test");
            Directory.CreateDirectory(root);
            var report = new StringBuilder(); int result = 1;
            var settings = new Settings { Seconds = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 10, Fps = 30, Mbps = 8, Audio = args.Length > 2 ? args[2] : "both", Folder = root };
            if (args.Length > 4) settings.Display = int.Parse(args[4], CultureInfo.InvariantCulture);
            if (args.Length > 5) settings.DisplayAdapter = int.Parse(args[5], CultureInfo.InvariantCulture);
            var monitor = MonitorCatalog.Enumerate().Find(m => m.Adapter == settings.DisplayAdapter && m.Output == settings.Display);
            if (monitor != null) settings.Rotation = monitor.Rotation;
            using (var recorder = new Recorder(settings))
            {
                try
                {
                    await recorder.StartAsync(); report.AppendLine("START: PASS");
                    var app = Process.GetCurrentProcess(); double cpuStart = app.TotalProcessorTime.TotalSeconds + recorder.CpuSeconds;
                    var clock = Stopwatch.StartNew(); long peakMemory = 0, peakDisk = 0;
                    // Verify that silent loopback never stalls video. Tests must not play audible tones.
                    await Task.Delay(3000);
                    string first = await recorder.SaveAsync();
                    report.AppendLine("FIRST_SAVE: " + Path.GetFileName(first));
                    if (recorder.BufferedSeconds > 2.1) throw new Exception("Saved footage still fills the buffer.");
                    report.AppendLine("BUFFER_RESET_AFTER_FIRST_SAVE: PASS");
                    for (int i = 0; i < settings.Seconds + 4; i++)
                    {
                        await Task.Delay(1000); app.Refresh();
                        if (recorder.Failure != null || !recorder.Running) throw new Exception(recorder.Failure ?? recorder.ErrorLog);
                        peakMemory = Math.Max(peakMemory, app.WorkingSet64 + recorder.Memory); peakDisk = Math.Max(peakDisk, recorder.DiskBytes);
                    }
                    if (recorder.SegmentCount > settings.Seconds / 2 + 6) throw new Exception("Ring retention exceeded limit.");
                    var saving = recorder.SaveAsync();
                    bool rejected = false;
                    try { await recorder.SaveAsync(); } catch (InvalidOperationException) { rejected = true; }
                    if (!rejected) throw new Exception("Concurrent saves were not rejected.");
                    string second = await saving;
                    report.AppendLine("CONCURRENT_SAVE_GUARD: PASS");
                    report.AppendLine("SECOND_SAVE: " + Path.GetFileName(second));
                    double cpu = (app.TotalProcessorTime.TotalSeconds + recorder.CpuSeconds - cpuStart) / clock.Elapsed.TotalSeconds / Environment.ProcessorCount * 100;
                    report.AppendLine("CPU_AVERAGE_PERCENT_ALL_LOGICAL_CORES: " + cpu.ToString("F2", CultureInfo.InvariantCulture));
                    report.AppendLine("RAM_PEAK_MIB_APP_PLUS_FFMPEG: " + (peakMemory / 1048576.0).ToString("F1", CultureInfo.InvariantCulture));
                    report.AppendLine("RING_DISK_PEAK_MIB: " + (peakDisk / 1048576.0).ToString("F1", CultureInfo.InvariantCulture));
                    report.AppendLine("BUFFER_SECONDS: " + recorder.BufferedSeconds.ToString("F2", CultureInfo.InvariantCulture));
                    report.AppendLine("RING_SEGMENT_COUNT: " + recorder.SegmentCount);
                    if (recorder.SegmentCount > settings.Seconds / 2 + 6) throw new Exception("Ring retention exceeded limit.");
                    var audioBytes = recorder.AudioBytes;
                    report.AppendLine("AUDIO_CAPTURE_BYTES_SYSTEM_THEN_MIC: " + string.Join(",", audioBytes.Select(v => v.ToString()).ToArray()));
                    if (settings.Audio == "both" && audioBytes.Length > 1 && audioBytes[1] == 0) throw new Exception("Microphone produced no samples.");
                    string ffmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe");
                    foreach (var video in new[] { first, second })
                    {
                        var decodeLog = new StringBuilder();
                        using (var decoder = Recorder.Launch(ffmpeg, new[] { "-v", "error", "-i", video, "-f", "null", "-" }, root, s => { lock (decodeLog) decodeLog.AppendLine(s); }))
                        {
                            await Task.Run(() => { if (!decoder.WaitForExit(30000)) { decoder.Kill(); throw new TimeoutException("Decode timed out"); } decoder.WaitForExit(); });
                            if (decoder.ExitCode != 0 || decodeLog.Length != 0) throw new Exception("Decode failed: " + decodeLog);
                        }
                    }
                    report.AppendLine("DECODE_BOTH_MP4: PASS");
                    // Stop and save may overlap without deleting pinned segments or corrupting export.
                    var refill = Stopwatch.StartNew(); while (recorder.BufferedSeconds < 2 && refill.ElapsedMilliseconds < 6000 && recorder.Running) await Task.Delay(100);
                    if (recorder.BufferedSeconds < 2) throw new Exception("Buffer did not refill after the second save.");
                    var finalSave = recorder.SaveAsync(); await recorder.StopAsync(); string last = await finalSave;
                    if (!File.Exists(last)) throw new Exception("Save during stop failed.");
                    report.AppendLine("SAVE_DURING_STOP: PASS");
                    result = 0;
                }
                catch (Exception e) { report.AppendLine("FAIL: " + e); report.AppendLine(recorder.ErrorLog); }
                await recorder.StopAsync(); report.AppendLine("STOP: " + (!recorder.Running ? "PASS" : "FAIL"));
            }
            File.WriteAllText(Path.Combine(root, "self-test.txt"), report.ToString(), new UTF8Encoding(false));
            return result;
        }
        public static async Task<int> BufferResetTest(string root)
        {
            root = Path.GetFullPath(root); Directory.CreateDirectory(root);
            string session = Path.Combine(root, "buffer-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(session);
            var settings = new Settings { Seconds = 30, Audio = "none", Folder = root };
            var report = new StringBuilder(); int exit = 1;
            using (var recorder = new Recorder(settings))
            {
                try
                {
                    string ffmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe");
                    // Tiny synthetic source tests the real export on CI without NVIDIA or desktop capture.
                    var capture = Recorder.Launch(ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi", "-i", "testsrc2=size=64x64:rate=5", "-c:v", "libx264", "-preset", "ultrafast", "-g", "10", "-bf", "0", "-f", "segment", "-segment_format", "mpegts", "-segment_time", "2", "-segment_list", "segments.csv", "-segment_list_type", "csv", "-segment_list_size", "20", "-reset_timestamps", "1", "s%09d.ts" }, session, null);
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    typeof(Recorder).GetField("session", flags).SetValue(recorder, session); typeof(Recorder).GetField("process", flags).SetValue(recorder, capture);
                    var deadline = Stopwatch.StartNew(); while (recorder.BufferedSeconds < 4 && deadline.ElapsedMilliseconds < 10000 && recorder.Running) await Task.Delay(100);
                    if (recorder.BufferedSeconds < 4) throw new Exception("Synthetic buffer did not fill.");
                    int pid = recorder.ProcessId; double beforeFailure = recorder.BufferedSeconds;
                    string invalidFolder = Path.Combine(root, "not-a-folder"); File.WriteAllText(invalidFolder, "sentinel"); settings.Folder = invalidFolder;
                    bool rejected = false; try { await recorder.SaveAsync(); } catch (IOException) { rejected = true; }
                    settings.Folder = root;
                    if (!rejected || recorder.BufferedSeconds < beforeFailure) throw new Exception("Failed export consumed the buffer.");
                    report.AppendLine("FAILED_SAVE_RETAINS_BUFFER: PASS");
                    string first = await recorder.SaveAsync();
                    string savedLast = (string)typeof(Recorder).GetField("savedLastSegment", flags).GetValue(recorder);
                    if (recorder.BufferedSeconds > 2.1 || Directory.GetFiles(session, "s*.ts").Any(p => string.CompareOrdinal(Path.GetFileName(p), savedLast) <= 0)) throw new Exception("Successful export did not clear consumed segments.");
                    report.AppendLine("SUCCESS_CLEARS_CONSUMED_SEGMENTS: PASS");
                    string second = await recorder.SaveAsync();
                    if (recorder.ProcessId != pid || !recorder.Running) throw new Exception("Buffer reset restarted capture.");
                    report.AppendLine("CAPTURE_PROCESS_CONTINUES: PASS");
                    var firstHashes = await PacketHashes(first); var secondHashes = await PacketHashes(second);
                    if (firstHashes.Length == 0 || secondHashes.Length == 0 || firstHashes.Intersect(secondHashes).Any()) throw new Exception("Consecutive clips share encoded frames.");
                    report.AppendLine("CONSECUTIVE_CLIPS_NO_SHARED_FRAMES: PASS");
                    deadline.Restart(); while (recorder.BufferedSeconds < 2 && deadline.ElapsedMilliseconds < 6000 && recorder.Running) await Task.Delay(100);
                    if (recorder.BufferedSeconds < 2) throw new Exception("Buffer did not refill after reset.");
                    report.AppendLine("BUFFER_REFILLS_AFTER_RESET: PASS");
                    var finalSave = recorder.SaveAsync(); await recorder.StopAsync(); string last = await finalSave;
                    foreach (string video in new[] { first, second, last })
                    {
                        var log = new StringBuilder(); using (var decoder = Recorder.Launch(ffmpeg, new[] { "-v", "error", "-i", video, "-f", "null", "-" }, root, line => { lock (log) log.AppendLine(line); }))
                        { await Task.Run(delegate { if (!decoder.WaitForExit(10000)) { decoder.Kill(); throw new TimeoutException("Decode timed out"); } decoder.WaitForExit(); }); if (decoder.ExitCode != 0 || log.Length != 0) throw new Exception("Saved clip failed to decode: " + log); }
                    }
                    report.AppendLine("DECODE_THREE_CLIPS_AND_SAVE_DURING_STOP: PASS"); exit = 0;
                }
                catch (Exception e) { report.AppendLine("FAIL: " + e); }
                await recorder.StopAsync();
            }
            File.WriteAllText(Path.Combine(root, "buffer-test.txt"), report.ToString(), new UTF8Encoding(false)); return exit;
        }
        static async Task<string[]> PacketHashes(string video)
        {
            using (var probe = new Process { StartInfo = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffprobe.exe"), "-v error -select_streams v:0 -show_packets -show_data_hash sha256 -show_entries packet=data_hash -of csv=p=0 " + Recorder.Quote(video)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } })
            {
                probe.Start(); Native.ProtectChild(probe); var output = probe.StandardOutput.ReadToEndAsync(); var error = probe.StandardError.ReadToEndAsync();
                await Task.Run(delegate { if (!probe.WaitForExit(10000)) { probe.Kill(); throw new TimeoutException("Probe timed out"); } probe.WaitForExit(); });
                if (probe.ExitCode != 0) throw new IOException(await error);
                return (await output).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(line => line.Contains("SHA256:")).ToArray();
            }
        }
    }
}
