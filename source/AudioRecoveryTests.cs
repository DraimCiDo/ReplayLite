using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace ReplayLite
{
    static class AudioRecoveryTests
    {
        sealed class Sources
        {
            internal volatile bool SystemPresent, MicPresent, FailSystemStart, FailMicRead;
            internal int Created, Disposed, SystemOpened;
            internal IAudioSource Open(bool system)
            {
                if (!(system ? SystemPresent : MicPresent)) throw new NotSupportedException("Endpoint unavailable");
                Interlocked.Increment(ref Created); if (system) Interlocked.Increment(ref SystemOpened);
                return new Source(this, system);
            }
        }
        sealed class Source : IAudioSource
        {
            readonly Sources owner; readonly bool system; long bytes;
            internal Source(Sources owner, bool system) { this.owner = owner; this.system = system; }
            public string Failure { get { return !(system ? owner.SystemPresent : owner.MicPresent) ? "Audio device invalidated" : null; } }
            public long CapturedBytes { get { return Interlocked.Read(ref bytes); } }
            public void Start() { if (system && owner.FailSystemStart) throw new NotSupportedException("Audio initialization failed"); }
            public int Read(float[] values)
            {
                if (!system && owner.FailMicRead) throw new NotSupportedException("Device disconnected while reading");
                for (int i = 0; i < values.Length; i++) values[i] = system ? .1f : .05f;
                Interlocked.Add(ref bytes, values.Length * 4); return values.Length;
            }
            public void Dispose() { Interlocked.Increment(ref owner.Disposed); }
        }
        static async Task WaitFor(Func<bool> condition, string name)
        {
            var timeout = Stopwatch.StartNew();
            while (!condition() && timeout.ElapsedMilliseconds < 7000) await Task.Delay(25);
            if (!condition()) throw new Exception(name);
        }
        internal static async Task<int> Run(string root)
        {
            root = Path.GetFullPath(root); Directory.CreateDirectory(root);
            string session = Path.Combine(root, "audio-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(session);
            var report = new StringBuilder(); int exit = 1; var sources = new Sources();
            var settings = new Settings { Audio = "both", Seconds = 30, Folder = root };
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            using (var recorder = new Recorder(settings))
            {
                try
                {
                    var changes = new AudioDeviceChanges(true, false);
                    var system = new AudioPipe(true, sources.Open, changes, 100);
                    var mic = new AudioPipe(false, sources.Open, null, 100);
                    var pipes = (List<AudioPipe>)typeof(Recorder).GetField("audio", flags).GetValue(recorder);
                    pipes.Add(system); pipes.Add(mic); system.Start(); mic.Start();
                    string ffmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe");
                    var args = new List<string> { "-hide_banner", "-v", "error", "-y" };
                    foreach (var pipe in pipes) args.AddRange(new[] { "-thread_queue_size", "32", "-probesize", "32", "-analyzeduration", "0", "-f", "f32le", "-ar", "48000", "-ac", "2", "-i", pipe.InputPath });
                    args.AddRange(new[] { "-re", "-f", "lavfi", "-i", "testsrc2=size=64x64:rate=5", "-filter_complex", "[0:a][1:a]amix=inputs=2:duration=longest:dropout_transition=0:normalize=0[a]", "-map", "2:v", "-map", "[a]", "-c:v", "libx264", "-preset", "ultrafast", "-g", "10", "-bf", "0", "-c:a", "aac", "-threads", "1", "-f", "segment", "-segment_format", "mpegts", "-segment_time", "2", "-segment_list", "segments.csv", "-segment_list_type", "csv", "-reset_timestamps", "1", "s%09d.ts" });
                    var process = Recorder.Launch(ffmpeg, args, session, null);
                    typeof(Recorder).GetField("session", flags).SetValue(recorder, session);
                    typeof(Recorder).GetField("process", flags).SetValue(recorder, process);
                    var maintain = typeof(Recorder).GetMethod("Maintain", flags);
                    typeof(Recorder).GetField("cleaner", flags).SetValue(recorder, new System.Threading.Timer(delegate { maintain.Invoke(recorder, null); }, null, 250, 250));
                    await WaitFor(() => recorder.BufferedSeconds > 0, "No video buffer without audio endpoints");
                    if (!recorder.AudioDegraded || recorder.AudioSummary != "Без звука") throw new Exception("Unavailable endpoints were not replaced with silence");
                    int pid = recorder.ProcessId; double before = recorder.BufferedSeconds;
                    report.AppendLine("START_WITHOUT_AUDIO_ENDPOINTS: PASS");
                    sources.SystemPresent = true; sources.FailSystemStart = true;
                    await Task.Delay(250);
                    if (system.Available || recorder.Failure != null || !recorder.Running) throw new Exception("Audio startup failure stopped video");
                    report.AppendLine("AUDIO_START_FAILURE_KEEPS_VIDEO: PASS");
                    sources.FailSystemStart = false;
                    await WaitFor(() => system.Available, "System audio did not reconnect");
                    if (mic.Available || recorder.AudioSummary != "Только звук компьютера") throw new Exception("System-only fallback incorrect");
                    report.AppendLine("MIC_MISSING_FALLBACK_TO_SYSTEM: PASS");
                    sources.MicPresent = true;
                    await WaitFor(() => mic.Available && mic.CapturedBytes > 0, "Microphone did not reconnect");
                    if (recorder.AudioSummary != "Компьютер и микрофон") throw new Exception("Both sources not restored");
                    report.AppendLine("MIC_RECONNECT_RESTORES_BOTH: PASS");
                    sources.FailMicRead = true;
                    await WaitFor(() => !mic.Available, "Microphone read failure not handled");
                    if (!system.Available || mic.Failure != null || recorder.Failure != null) throw new Exception("Microphone read failure affected healthy source");
                    report.AppendLine("MIC_READ_FAILURE_ISOLATED: PASS");
                    sources.SystemPresent = false;
                    await WaitFor(() => !system.Available, "System audio invalidation not handled");
                    if (recorder.AudioSummary != "Без звука" || !recorder.Running) throw new Exception("Disconnecting both devices stopped video");
                    report.AppendLine("DISCONNECT_BOTH_KEEPS_VIDEO: PASS");
                    sources.FailMicRead = false;
                    await WaitFor(() => mic.Available, "Microphone-only fallback unavailable");
                    if (recorder.AudioSummary != "Только микрофон") throw new Exception("Microphone-only fallback incorrect");
                    sources.SystemPresent = true;
                    await WaitFor(() => system.Available, "System audio did not return");
                    int opened = sources.SystemOpened;
                    changes.OnDefaultDeviceChanged(DataFlow.Render, Role.Multimedia, "replacement-device");
                    await WaitFor(() => sources.SystemOpened > opened && system.Available, "Default endpoint change not rebound");
                    if (recorder.AudioDegraded || recorder.AudioSummary != "Компьютер и микрофон" || settings.Audio != "both") throw new Exception("Preferred audio setting changed");
                    await WaitFor(() => recorder.BufferedSeconds > before, "Buffer stopped growing during audio recovery");
                    if (!recorder.Running || recorder.ProcessId != pid || recorder.Failure != null) throw new Exception("Audio recovery restarted or failed capture");
                    report.AppendLine("DEFAULT_DEVICE_CHANGE_REBINDS_WITHOUT_RESTART_OR_BUFFER_RESET: PASS");
                    string video = await recorder.SaveAsync();
                    var decodeErrors = new StringBuilder();
                    using (var decoder = Recorder.Launch(ffmpeg, new[] { "-v", "error", "-i", video, "-f", "null", "-" }, root, line => { lock (decodeErrors) decodeErrors.AppendLine(line); }))
                    {
                        await Task.Run(delegate { if (!decoder.WaitForExit(10000)) { decoder.Kill(); throw new TimeoutException("Decode timed out"); } decoder.WaitForExit(); });
                        if (decoder.ExitCode != 0 || decodeErrors.Length != 0) throw new Exception("Recovered replay failed decode: " + decodeErrors);
                    }
                    report.AppendLine("RECOVERED_REPLAY_EXPORT_AND_DECODE: PASS"); exit = 0;
                }
                catch (Exception error) { report.AppendLine("FAIL: " + error); }
                await recorder.StopAsync();
                if (sources.Created != sources.Disposed) { report.AppendLine("FAIL: Audio source leak"); exit = 1; }
                else report.AppendLine("ALL_AUDIO_SOURCES_DISPOSED: PASS");
            }
            File.WriteAllText(Path.Combine(root, "audio-test.txt"), report.ToString(), new UTF8Encoding(false)); return exit;
        }
    }
}
