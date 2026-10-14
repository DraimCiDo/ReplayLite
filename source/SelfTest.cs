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
                    for (int i = 0; i < settings.Seconds + 4; i++)
                    {
                        await Task.Delay(1000); app.Refresh();
                        if (recorder.Failure != null || !recorder.Running) throw new Exception(recorder.Failure ?? recorder.ErrorLog);
                        peakMemory = Math.Max(peakMemory, app.WorkingSet64 + recorder.Memory); peakDisk = Math.Max(peakDisk, recorder.DiskBytes);
                    }
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
    }
}
