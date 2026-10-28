using System;
using System.IO;
using System.IO.Pipes;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace ReplayLite
{
    static class AdvancedSettingsTests
    {
        sealed class ConstantAudio : IAudioSource
        {
            public string Failure { get { return null; } } public long CapturedBytes { get { return 1; } }
            public void Start() { } public void Dispose() { }
            public int Read(float[] values) { for (int i = 0; i < values.Length; i++) values[i] = .2f; return values.Length; }
        }
        internal static async Task VerifyBehaviorAsync(StringBuilder report)
        {
            foreach (int volume in new[] { 0, 50, 100 })
                using (var pipe = new AudioPipe(true, s => new ConstantAudio(), null, 100, volume))
                using (var reader = new NamedPipeClientStream(".", pipe.Name, PipeDirection.In, PipeOptions.Asynchronous))
                {
                    pipe.Start(); reader.Connect(3000); byte[] bytes = new byte[4]; var read = reader.ReadAsync(bytes, 0, 4);
                    if (await Task.WhenAny(read, Task.Delay(3000)) != read || await read != 4) throw new Exception("Audio gain pipe read timed out.");
                    if (Math.Abs(BitConverter.ToSingle(bytes, 0) - .2f * volume / 100) > .00001f) throw new Exception("PCM pipe ignored configured volume.");
                }
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".deps-cache", "advanced-disk-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var config = new Settings { Audio = "none", CaptureCursor = false, FreeSpaceMb = 512, BufferFolder = Path.Combine(root, "Буфер с пробелами"), Folder = root, Seconds = 10 };
            long free = 0; bool launched = false;
            Func<List<string>, string, Action<string>, Process> launch = (args, session, log) =>
            {
                launched = true;
                string graph = args[args.IndexOf("-filter_complex") + 1];
                if (!graph.Contains("draw_mouse=0") || Path.GetDirectoryName(session) != config.BufferFolder) throw new Exception("Configured cursor or buffer folder ignored.");
                return Recorder.Launch(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe"), new[] { "-v", "error", "-re", "-f", "lavfi", "-i", "testsrc2=size=64x64:rate=5", "-c:v", "libx264", "-preset", "ultrafast", "-g", "10", "-bf", "0", "-f", "segment", "-segment_format", "mpegts", "-segment_time", "2", "-segment_list", "segments.csv", "-segment_list_type", "csv", "-reset_timestamps", "1", "s%09d.ts" }, session, log);
            };
            using (var recorder = new Recorder(config, launch, path => free))
            {
                bool rejected = false; try { await recorder.StartAsync(); } catch (IOException) { rejected = true; }
                if (!rejected || launched || recorder.Running || recorder.NeedsRecovery) throw new Exception("Low space did not reject capture before launch.");
            }
            free = 20L * 1024 * 1048576;
            using (var recorder = new Recorder(config, launch, path => free))
            {
                Exception failure = null;
                try
                {
                    await recorder.StartAsync(); free = 256L * 1048576;
                    var clock = Stopwatch.StartNew(); while (recorder.Failure == null && clock.ElapsedMilliseconds < 3000) await Task.Delay(100);
                    if (recorder.Failure == null || recorder.NeedsRecovery) throw new Exception("Custom disk reserve did not fail without capture retries.");
                }
                catch (Exception error) { failure = error; }
                await recorder.StopAsync(); if (failure != null) throw failure;
            }
            if (Directory.GetDirectories(config.BufferFolder, "buffer-*").Length != 0) throw new Exception("Custom session not cleaned after stop.");
            string sentinel = Path.Combine(root, "not-a-folder"); File.WriteAllText(sentinel, "keep"); config.BufferFolder = sentinel;
            using (var recorder = new Recorder(config, launch))
            {
                bool rejected = false; try { await recorder.StartAsync(); } catch (IOException) { rejected = true; }
                if (!rejected || File.ReadAllText(sentinel) != "keep") throw new Exception("Invalid buffer folder damaged sentinel.");
            }
            report.AppendLine("PCM_PIPE_VOLUME_0_50_100: PASS");
            report.AppendLine("CUSTOM_BUFFER_START_RUNTIME_LOW_SPACE_AND_INVALID_PATH: PASS");
        }
        internal static void Run(StringBuilder report)
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".deps-cache", "advanced-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); string file = Path.Combine(root, "settings.txt");
            var config = new Settings { SystemDevice = "render=id", MicrophoneDevice = "capture=id", SystemVolume = 55, MicrophoneVolume = 0, FreeSpaceMb = 512, StopWhenLowSpace = false, BufferFolder = Path.Combine(root, "Буфер с пробелами"), Animations = false, SaveNotification = false, SaveSound = true, CaptureCursor = false };
            config.SaveToFile(file); var loaded = Settings.LoadFromFile(file);
            if (loaded.SystemDevice != config.SystemDevice || loaded.MicrophoneDevice != config.MicrophoneDevice || loaded.SystemVolume != 55 || loaded.MicrophoneVolume != 0 || loaded.BufferHome != config.BufferFolder || loaded.FreeSpaceMb != 512 || loaded.StopWhenLowSpace || loaded.Animations || loaded.SaveNotification || !loaded.SaveSound || loaded.CaptureCursor) throw new Exception("Advanced settings round-trip failed.");
            File.WriteAllText(file, "Seconds=120\nMbps=8\nAudio=both\n", Encoding.UTF8); loaded = Settings.LoadFromFile(file);
            if (loaded.SystemVolume != 100 || loaded.MicrophoneVolume != 100 || !loaded.Animations || !loaded.SaveNotification || loaded.SaveSound || !loaded.CaptureCursor || loaded.BufferHome != Settings.Home) throw new Exception("Legacy settings defaults changed.");
            File.WriteAllText(file, "SystemVolume=-5\nMicrophoneVolume=200\nFreeSpaceMb=-1\nAnimations=garbage\nBufferFolder=relative\n", Encoding.UTF8); loaded = Settings.LoadFromFile(file);
            if (loaded.SystemVolume != 0 || loaded.MicrophoneVolume != 100 || loaded.FreeSpaceMb != 128 || !loaded.Animations || loaded.BufferFolder != "") throw new Exception("Invalid advanced values were not bounded.");
            byte[] before = File.ReadAllBytes(file); config.BufferFolder = "relative"; bool rejected = false;
            try { config.SaveToFile(file); } catch (ArgumentException) { rejected = true; }
            if (!rejected || Convert.ToBase64String(before) != Convert.ToBase64String(File.ReadAllBytes(file))) throw new Exception("Invalid folder overwrote settings.");
            if (Settings.ValidAbsolutePath(@"C:relative") || Settings.ValidAbsolutePath(@"\relative") || Settings.ValidAbsolutePath("bad\npath")) throw new Exception("Drive-relative buffer path accepted.");
            config.BufferFolder = ""; string blocker = Path.Combine(root, "blocked"); File.WriteAllText(blocker, "sentinel"); rejected = false;
            try { config.SaveToFile(Path.Combine(blocker, "settings.txt")); } catch (IOException) { rejected = true; }
            if (!rejected || File.ReadAllText(blocker) != "sentinel") throw new Exception("Failed save modified unrelated file.");
            using (var recorder = new Recorder(config))
            {
                config.StopWhenLowSpace = true; config.FreeSpaceMb = 512; if (recorder.MinimumFreeBytes != 512L * 1048576) throw new Exception("Custom disk reserve ignored.");
                config.StopWhenLowSpace = false; if (recorder.MinimumFreeBytes != 128L * 1048576) throw new Exception("Hard disk safety floor disabled.");
            }
            var values = new[] { -.8f, .4f, 0f }; AudioPipe.ApplyGain(values, .5f);
            if (Math.Abs(values[0] + .4f) > .00001f || Math.Abs(values[1] - .2f) > .00001f) throw new Exception("Audio gain incorrect.");
            AudioPipe.ApplyGain(values, 0); if (Array.Exists(values, v => v != 0)) throw new Exception("Mute gain incorrect.");
            AudioPipe.ApplyGain(values, 1); if (Array.Exists(values, v => v != 0)) throw new Exception("Unity gain changed silence.");
            var choices = AudioChoice.Enumerate(false, "missing-test-endpoint");
            if (!choices.Exists(c => c.Id == "missing-test-endpoint") || choices[0].Id != "") throw new Exception("Missing device preference lost.");
            using (var changes = new AudioDeviceChanges(true, false, "selected"))
            {
                changes.OnDefaultDeviceChanged(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia, "other"); changes.OnDeviceRemoved("other");
                if (changes.Version != 0) throw new Exception("Explicit audio endpoint reacts to unrelated defaults.");
                changes.OnDeviceRemoved("selected"); if (changes.Version != 1) throw new Exception("Explicit audio endpoint removal ignored.");
            }
            report.AppendLine("ADVANCED_SETTINGS_ROUNDTRIP_LEGACY_INVALID_AND_FAILED_SAVE: PASS");
            report.AppendLine("AUDIO_GAIN_MUTE_MISSING_DEVICE_AND_DISK_RESERVE: PASS");
        }
    }
}
