using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReplayLite
{
    public sealed class Settings
    {
        public int Seconds = 120, Fps = 30, Mbps = 8, Display = 0;
        public int DisplayAdapter, Rotation;
        public string MonitorId = "";
        public KeyBinding Hotkey = KeyBinding.Default;
        public string Audio = "both";
        public string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ReplayLite");
        public static readonly bool Portable = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "portable.flag"));
        public static readonly string Home = Portable ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReplayLite");
        public static Settings Load()
        {
            var s = new Settings();
            if (Portable) s.Folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Replays");
            try
            {
                foreach (var line in File.ReadAllLines(Path.Combine(Home, "settings.txt"), Encoding.UTF8))
                {
                    var p = line.Split(new[] { '=' }, 2); if (p.Length != 2) continue;
                    int v;
                    switch (p[0])
                    {
                        case "Seconds": if (int.TryParse(p[1], out v)) s.Seconds = Math.Max(10, Math.Min(600, v)); break;
                        case "Fps": if (int.TryParse(p[1], out v)) s.Fps = v == 60 ? 60 : 30; break;
                        case "Mbps": if (int.TryParse(p[1], out v)) s.Mbps = Math.Max(2, Math.Min(30, v)); break;
                        case "Display": if (int.TryParse(p[1], out v)) s.Display = Math.Max(0, Math.Min(63, v)); break;
                        case "DisplayAdapter": if (int.TryParse(p[1], out v)) s.DisplayAdapter = Math.Max(0, Math.Min(31, v)); break;
                        case "MonitorId": s.MonitorId = p[1]; break;
                        case "HotkeyModifiers": if (int.TryParse(p[1], out v)) s.Hotkey.Modifiers = (uint)v; break;
                        case "HotkeyKey": if (int.TryParse(p[1], out v)) s.Hotkey.Key = (Keys)v; break;
                        case "Audio": if (new[] { "both", "system", "none" }.Contains(p[1])) s.Audio = p[1]; break;
                        case "Folder": if (Path.IsPathRooted(p[1])) s.Folder = p[1]; else if (Portable) s.Folder = Deployment.Child(AppDomain.CurrentDomain.BaseDirectory, p[1]); break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (!s.Hotkey.Valid) s.Hotkey = KeyBinding.Default;
            return s;
        }
        public void Save()
        {
            Directory.CreateDirectory(Home);
            string target = Path.Combine(Home, "settings.txt"), pending = target + ".new";
            string storedFolder = Folder, appPrefix = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\') + "\\";
            if (Portable && Path.GetFullPath(Folder).StartsWith(appPrefix, StringComparison.OrdinalIgnoreCase)) storedFolder = Path.GetFullPath(Folder).Substring(appPrefix.Length);
            File.WriteAllLines(pending, new[] { "Seconds=" + Seconds, "Fps=" + Fps, "Mbps=" + Mbps, "Display=" + Display, "DisplayAdapter=" + DisplayAdapter, "MonitorId=" + MonitorId, "HotkeyModifiers=" + Hotkey.Modifiers, "HotkeyKey=" + (int)Hotkey.Key, "Audio=" + Audio, "Folder=" + storedFolder }, Encoding.UTF8);
            if (File.Exists(target)) File.Replace(pending, target, null); else File.Move(pending, target);
        }
    }

    // Raw audio alone is transported through small pipes. Video never passes through managed RAM.
    public sealed class AudioPipe : IDisposable
    {
        public readonly string Name = "ReplayLite-" + Guid.NewGuid().ToString("N");
        public string InputPath { get { return @"\\.\pipe\" + Name; } }
        readonly NamedPipeServerStream pipe;
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        readonly Func<bool, IAudioSource> openSource;
        readonly AudioDeviceChanges changes;
        readonly int retryMilliseconds;
        volatile IAudioSource source;
        Task pump;
        volatile bool disposed;
        long capturedBytes;
        public readonly bool SystemAudio;
        public volatile bool Available;
        public volatile string Warning, Failure;
        public long CapturedBytes { get { var current = source; return Interlocked.Read(ref capturedBytes) + (current == null ? 0 : current.CapturedBytes); } }

        public AudioPipe(bool system) : this(system, s => new WasapiAudioSource(s), AudioDeviceChanges.TryCreate(system), 5000) { }
        internal AudioPipe(bool system, Func<bool, IAudioSource> factory, AudioDeviceChanges notifications = null, int retryMs = 5000)
        {
            SystemAudio = system; openSource = factory; changes = notifications; retryMilliseconds = retryMs;
            try { pipe = new NamedPipeServerStream(Name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 16384, 16384); }
            catch { if (changes != null) changes.Dispose(); stop.Dispose(); throw; }
        }
        void DropSource()
        {
            Available = false; var previous = source; source = null;
            if (previous == null) return;
            Interlocked.Add(ref capturedBytes, previous.CapturedBytes);
            try { previous.Dispose(); } catch (Exception) { }
        }
        public void Start()
        {
            // Capture starts only after FFmpeg opens this input, avoiding stale startup audio.
            pump = Task.Run(delegate
            {
                try
                {
                    pipe.WaitForConnection();
                    if (stop.WaitOne(0)) return;
                    var values = new float[1920]; var bytes = new byte[values.Length * 4];
                    var clock = Stopwatch.StartNew(); long blocks = 0, retryAt = 0; int deviceVersion = changes == null ? 0 : changes.Version;
                    while (!stop.WaitOne(0))
                    {
                        long now = clock.ElapsedMilliseconds;
                        if (changes != null && deviceVersion != changes.Version)
                        { deviceVersion = changes.Version; DropSource(); retryAt = now; }
                        if (source != null && source.Failure != null)
                        { Warning = source.Failure; DropSource(); retryAt = now; }
                        if (source == null && now >= retryAt)
                        {
                            retryAt = now + retryMilliseconds;
                            try { source = openSource(SystemAudio); source.Start(); Warning = null; Available = true; }
                            catch (Exception error) { Warning = error.Message; DropSource(); }
                        }
                        Array.Clear(values, 0, values.Length);
                        if (source != null)
                        {
                            try { int read = source.Read(values); if (read < values.Length) Array.Clear(values, read, values.Length - read); }
                            catch (Exception error) { Warning = error.Message; DropSource(); Array.Clear(values, 0, values.Length); retryAt = now + retryMilliseconds; }
                        }
                        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                        pipe.Write(bytes, 0, bytes.Length);
                        blocks++;
                        int delay = (int)(blocks * 20 - clock.ElapsedMilliseconds);
                        if (delay > 0) stop.WaitOne(delay);
                        // FFmpeg queue stays bounded; a blocked pipe applies backpressure.
                    }
                }
                catch (Exception e) { if (!disposed) Failure = e.Message; }
                finally { DropSource(); }
            });
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; stop.Set();
            try { pipe.Dispose(); } catch { }
            if (pump != null) try { pump.Wait(2000); } catch { }
            if (changes != null) changes.Dispose();
            if (pump == null || pump.IsCompleted) stop.Dispose();
            else pump.ContinueWith(delegate { stop.Dispose(); }, TaskScheduler.Default);
        }
    }

    public sealed class Segment
    {
        public string Name; public double Start, End;
    }
    public sealed class Recorder : IDisposable
    {
        public readonly Settings Config;
        readonly string ffmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe");
        readonly object gate = new object(), logGate = new object();
        readonly Queue<string> errors = new Queue<string>();
        readonly List<AudioPipe> audio = new List<AudioPipe>();
        readonly SemaphoreSlim saver = new SemaphoreSlim(1, 1);
        Process process;
        System.Threading.Timer cleaner;
        string session;
        string savedLastSegment;
        readonly List<Segment> retainedSegments = new List<Segment>();
        readonly Func<List<string>, string, Action<string>, Process> captureLauncher;
        int nextSegmentNumber;
        double timestampOffset;
        volatile bool recovering;
        string retainedVideoFormat;
        int retainedRotation;
        public bool BufferResetOnRecovery { get; private set; }
        volatile bool stopping;
        public string Failure;
        public int ProcessId { get { return process == null ? 0 : process.Id; } }
        public bool Running { get { return process != null && !process.HasExited && !stopping; } }
        public double CpuSeconds { get { try { return process.TotalProcessorTime.TotalSeconds; } catch { return 0; } } }
        public long Memory { get { try { process.Refresh(); return process.WorkingSet64; } catch { return 0; } } }
        public long DiskBytes { get { lock (gate) { return session == null || !Directory.Exists(session) ? 0 : Directory.GetFiles(session, "*.ts").Sum(p => new FileInfo(p).Length); } } }
        public int SegmentCount { get { lock (gate) { return session == null || !Directory.Exists(session) ? 0 : Directory.GetFiles(session, "s*.ts").Length; } } }
        public long[] AudioBytes { get { return audio.Select(a => a.CapturedBytes).ToArray(); } }
        public bool AudioDegraded { get { return audio.Any(a => !a.Available); } }
        public string AudioSummary
        {
            get
            {
                bool system = audio.Any(a => a.SystemAudio && a.Available), mic = audio.Any(a => !a.SystemAudio && a.Available);
                return system && mic ? "Компьютер и микрофон" : system ? "Только звук компьютера" : mic ? "Только микрофон" : "Без звука";
            }
        }
        public double BufferedSeconds { get { lock (gate) { var a = ReadSegments(); return a.Count == 0 ? 0 : Math.Min(Config.Seconds, a.Last().End - a.First().Start); } } }
        public Recorder(Settings settings) { Config = settings; }
        internal Recorder(Settings settings, Func<List<string>, string, Action<string>, Process> launcher) { Config = settings; captureLauncher = launcher; }

        public static string Quote(string value)
        {
            // CommandLineToArgvW quoting, including trailing backslashes and embedded quotes.
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"') result.Append('\\', slashes * 2 + 1).Append('"');
                else result.Append('\\', slashes).Append(ch);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        public static Process Launch(string file, IEnumerable<string> args, string cwd, Action<string> log)
        {
            var p = new Process { StartInfo = new ProcessStartInfo(file, string.Join(" ", args.Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = cwd } };
            p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null && log != null) log(e.Data); };
            p.OutputDataReceived += delegate { };
            Native.ThrowIfSessionEnding(); p.Start();
            try { Native.ProtectChild(p); }
            catch { try { p.Kill(); } catch { } p.Dispose(); throw; }
            p.BeginErrorReadLine(); p.BeginOutputReadLine();
            return p;
        }
        void Log(string line) { lock (logGate) { errors.Enqueue(line); while (errors.Count > 25) errors.Dequeue(); } }
        public string ErrorLog { get { lock (logGate) return string.Join(Environment.NewLine, errors.ToArray()); } }
        public async Task StartAsync()
        {
            if (!File.Exists(ffmpeg)) throw new FileNotFoundException("Не найден bin\\ffmpeg.exe. Распакуй всю папку приложения.");
            Directory.CreateDirectory(Config.Folder);
            Directory.CreateDirectory(Settings.Home);
            session = Path.Combine(Settings.Home, "buffer-" + Guid.NewGuid().ToString("N"));
            savedLastSegment = null;
            Directory.CreateDirectory(session);
            recovering = true;
            Exception startupError = null;
            try { await StartPipelineAsync(CancellationToken.None); recovering = false; }
            catch (Exception error) { startupError = error; }
            if (startupError == null) return;
            if (!(startupError is OperationCanceledException) && !CaptureRecovery.IsTransient(startupError.ToString() + ErrorLog) && !(startupError is TimeoutException)) await StopAsync();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(startupError).Throw();
        }
        async Task StartPipelineAsync(CancellationToken token)
        {
            Exception startupError = null;
            try
            {
                token.ThrowIfCancellationRequested(); Native.ThrowIfSessionEnding();
                Failure = null; lock (logGate) errors.Clear(); CheckSpace();
                if (Config.Audio != "none") audio.Add(new AudioPipe(true));
                if (Config.Audio == "both") audio.Add(new AudioPipe(false));
                foreach (var a in audio) a.Start();
                var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-y", "-filter_threads", "1", "-filter_complex_threads", "1", "-init_hw_device", "d3d11va=capture:" + Config.DisplayAdapter, "-filter_hw_device", "capture" };
                foreach (var a in audio) args.AddRange(new[] { "-thread_queue_size", "32", "-probesize", "32", "-analyzeduration", "0", "-f", "f32le", "-ar", "48000", "-ac", "2", "-i", a.InputPath });
                string graph = "ddagrab=output_idx=" + Config.Display + ":framerate=" + Config.Fps + ":draw_mouse=1[v]";
                if (audio.Count == 2) graph += ";[0:a][1:a]amix=inputs=2:duration=longest:dropout_transition=0:normalize=0,alimiter=limit=0.95:latency=1[a]";
                args.AddRange(new[] { "-filter_complex", graph, "-map", "[v]", "-c:v", "h264_nvenc", "-preset", "p1", "-tune", "ll", "-rc", "cbr", "-b:v", Config.Mbps + "M", "-maxrate", Config.Mbps + "M", "-bufsize", (Config.Mbps * 2) + "M", "-g", (Config.Fps * 2).ToString(), "-bf", "0", "-rc-lookahead", "0", "-surfaces", "4", "-fps_mode", "cfr" });
                if (audio.Count == 2) args.AddRange(new[] { "-map", "[a]" });
                else if (audio.Count == 1) args.AddRange(new[] { "-map", "0:a" });
                if (audio.Count > 0) args.AddRange(new[] { "-c:a", "aac", "-b:a", "160k", "-threads:a", "1" });
                args.AddRange(new[] { "-f", "segment", "-segment_format", "mpegts", "-segment_time", "2", "-segment_start_number", nextSegmentNumber.ToString(CultureInfo.InvariantCulture), "-segment_list", "segments.csv", "-segment_list_type", "csv", "-segment_list_size", ((Config.Seconds / 2) + 4).ToString(), "-segment_list_flags", "+live", "-reset_timestamps", "1", "s%09d.ts" });
                token.ThrowIfCancellationRequested(); Native.ThrowIfSessionEnding();
                process = captureLauncher == null ? Launch(ffmpeg, args, session, Log) : captureLauncher(args, session, Log);
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                if (cleaner == null) cleaner = new System.Threading.Timer(delegate { Maintain(); }, null, 1000, 1000);
                for (int i = 0; i < 160; i++)
                {
                    await Task.Delay(100, token);
                    Native.ThrowIfSessionEnding();
                    if (Failure != null) throw new Exception(Failure);
                    if (process.HasExited) throw new Exception("Захват не запущен. Проверь экран и драйвер NVIDIA.");
                    lock (gate) { if (ReadCurrentSegments().Count > 0) return; }
                }
                throw new TimeoutException("За 16 секунд не появился видеобуфер.");
            }
            catch (Exception e) { startupError = e; }
            await StopPipelineAsync();
            if (!(startupError is OperationCanceledException))
            {
                string diagnostic = startupError.Message + "\n" + ErrorLog;
                if (startupError is TimeoutException) throw new TimeoutException(diagnostic, startupError);
                throw new IOException(diagnostic, startupError);
            }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(startupError).Throw();
        }
        List<Segment> ReadSegments()
        {
            var result = retainedSegments.Where(s => (savedLastSegment == null || string.CompareOrdinal(s.Name, savedLastSegment) > 0) && session != null && File.Exists(Path.Combine(session, s.Name))).ToList();
            result.AddRange(ReadCurrentSegments()); return result;
        }
        List<Segment> ReadCurrentSegments()
        {
            var result = new List<Segment>();
            if (session == null) return result;
            try
            {
                using (var stream = new FileStream(Path.Combine(session, "segments.csv"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var p = line.Split(','); double start, end;
                        if (p.Length != 3 || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out start) || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out end)) continue;
                        var name = p[0].Trim('"');
                        if (end > start && (savedLastSegment == null || string.CompareOrdinal(name, savedLastSegment) > 0) && Path.GetFileName(name) == name && name.StartsWith("s") && name.EndsWith(".ts") && File.Exists(Path.Combine(session, name))) result.Add(new Segment { Name = name, Start = start + timestampOffset, End = end + timestampOffset });
                    }
                }
            }
            catch (IOException) { }
            return result;
        }
        void CheckSpace()
        {
            var drive = new DriveInfo(Path.GetPathRoot(session));
            long required = (long)((Config.Mbps * 1000000.0 / 8 + 24000) * (Config.Seconds + 12) * 1.2) + 128 * 1024 * 1024;
            if (drive.AvailableFreeSpace < required) throw new IOException("Недостаточно места для буфера: нужно примерно " + (required / 1048576) + " МБ.");
        }
        void Maintain()
        {
            if (stopping || recovering) return;
            try
            {
                lock (gate)
                {
                    if (stopping || recovering || process == null) return;
                    if (process.HasExited) { Failure = "FFmpeg остановился.\n" + ErrorLog; return; }
                    foreach (var a in audio) if (a.Failure != null) { Failure = "Ошибка аудиоустройства: " + a.Failure; return; }
                    var all = Directory.GetFiles(session, "s*.ts").OrderBy(p => p, StringComparer.Ordinal).ToArray();
                    int keep = Config.Seconds / 2 + 5;
                    retainedSegments.RemoveAll(s => (savedLastSegment != null && string.CompareOrdinal(s.Name, savedLastSegment) <= 0) || !File.Exists(Path.Combine(session, s.Name)));
                    for (int i = 0; i < all.Length; i++)
                    {
                        bool consumed = savedLastSegment != null && string.CompareOrdinal(Path.GetFileName(all[i]), savedLastSegment) <= 0;
                        if (consumed) TryDeleteConsumed(all[i]);
                        else if (i < all.Length - keep) File.Delete(all[i]);
                    }
                    if (new DriveInfo(Path.GetPathRoot(session)).AvailableFreeSpace < 128 * 1024 * 1024) Failure = "На диске осталось менее 128 МБ; запись остановлена.";
                }
            }
            catch (Exception e) { if (!stopping && !recovering) Failure = "Ошибка буфера: " + e.Message; }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);
        void TryDeleteConsumed(string path)
        {
            // A successfully exported clip must stay successful even if another process locks an old segment.
            // Maintain retries these files; the open segment and newer footage are never deleted.
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        public async Task<string> SaveAsync()
        {
            if (!await saver.WaitAsync(0)) throw new InvalidOperationException("Предыдущий повтор ещё сохраняется.");
            string snapshot = null, output = null, partial = null;
            string exportedLast = null;
            try
            {
                if (!Running) throw new InvalidOperationException("Сначала включи буфер.");
                double previous;
                lock (gate) { var a = ReadSegments(); previous = a.Count == 0 ? -1 : a.Last().End; }
                // Include the moment the hotkey was pressed by waiting for the current GOP to close.
                for (int i = 0; i < 40 && Running; i++)
                {
                    await Task.Delay(100);
                    lock (gate) { var a = ReadSegments(); if (a.Count > 0 && a.Last().End > previous) break; }
                }
                lock (gate)
                {
                    var a = ReadSegments();
                    if (a.Count == 0) throw new InvalidOperationException("Буфер пока пуст.");
                    double end = a.Last().End;
                    var selected = a.Where(s => s.End > end - Config.Seconds + 0.05).ToArray();
                    exportedLast = selected.Last().Name;
                    snapshot = Path.Combine(session, "save-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(snapshot);
                    var list = new List<string> { "ffconcat version 1.0" };
                    foreach (var segment in selected)
                    {
                        string target = Path.Combine(snapshot, segment.Name), source = Path.Combine(session, segment.Name);
                        if (!CreateHardLink(target, source, IntPtr.Zero)) File.Copy(source, target);
                        list.Add("file '" + segment.Name + "'");
                        list.Add("duration " + (segment.End - segment.Start).ToString("0.000000", CultureInfo.InvariantCulture));
                    }
                    File.WriteAllLines(Path.Combine(snapshot, "list.ffconcat"), list, new UTF8Encoding(false));
                }
                output = Path.Combine(Config.Folder, "Replay-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".mp4");
                partial = output + ".partial.mp4";
                var log = new StringBuilder();
                using (var remux = Launch(ffmpeg, new[] { "-hide_banner", "-loglevel", "warning", "-y", "-f", "concat", "-safe", "1", "-display_rotation:v:0", Config.Rotation.ToString(CultureInfo.InvariantCulture), "-i", "list.ffconcat", "-map", "0", "-c", "copy", "-bsf:a", "aac_adtstoasc", "-movflags", "+faststart", partial }, snapshot, line => { lock (log) { if (log.Length < 12000) log.AppendLine(line); } }))
                {
                    await Task.Run(delegate { if (!remux.WaitForExit(60000)) { remux.Kill(); remux.WaitForExit(); throw new TimeoutException("Сохранение заняло больше минуты."); } remux.WaitForExit(); });
                    if (remux.ExitCode != 0) throw new IOException("Не удалось сохранить повтор.\n" + log);
                }
                File.Move(partial, output);
                lock (gate)
                {
                    savedLastSegment = exportedLast;
                    // Commit the boundary only after the MP4 has its final name. Keep anything captured during export.
                    try
                    {
                        if (session != null) foreach (var path in Directory.GetFiles(session, "s*.ts"))
                            if (string.CompareOrdinal(Path.GetFileName(path), savedLastSegment) <= 0) TryDeleteConsumed(path);
                    }
                    catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                return output;
            }
            finally
            {
                if (partial != null && File.Exists(partial)) try { File.Delete(partial); } catch { }
                if (snapshot != null && Directory.Exists(snapshot)) try { Directory.Delete(snapshot, true); } catch { }
                saver.Release();
            }
        }
        internal async Task RecoverAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Native.ThrowIfSessionEnding();
            if (stopping || session == null) throw new InvalidOperationException("Буфер остановлен.");
            await saver.WaitAsync(token);
            try
            {
                lock (gate) { if (!recovering) BufferResetOnRecovery = false; recovering = true; }
                await StopPipelineAsync(); token.ThrowIfCancellationRequested(); Native.ThrowIfSessionEnding();
                lock (gate)
                {
                    var completed = ReadSegments();
                    retainedSegments.Clear(); retainedSegments.AddRange(completed);
                    timestampOffset = completed.Count == 0 ? 0 : completed.Last().End;
                    foreach (var path in Directory.GetFiles(session, "s*.ts"))
                    {
                        string name = Path.GetFileNameWithoutExtension(path); int number;
                        if (int.TryParse(name.Substring(1), out number)) nextSegmentNumber = Math.Max(nextSegmentNumber, number + 1);
                        if (!completed.Any(s => s.Name == Path.GetFileName(path))) TryDeleteConsumed(path);
                    }
                    string csv = Path.Combine(session, "segments.csv"); if (File.Exists(csv)) File.Delete(csv);
                }
                if (retainedSegments.Count > 0 && retainedVideoFormat == null)
                {
                    retainedRotation = Config.Rotation;
                    try { retainedVideoFormat = await ProbeVideoFormatAsync(Path.Combine(session, retainedSegments[0].Name), token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { DiscardRetainedSegments(); }
                }
                if (captureLauncher == null)
                {
                    var monitor = MonitorCatalog.Enumerate().FirstOrDefault(m => m.CanCapture && (!string.IsNullOrEmpty(Config.MonitorId) ? string.Equals(m.Id, Config.MonitorId, StringComparison.OrdinalIgnoreCase) : m.Adapter == Config.DisplayAdapter && m.Output == Config.Display));
                    if (monitor == null) throw new CaptureUnavailableException("Выбранный монитор временно недоступен.");
                    Config.DisplayAdapter = monitor.Adapter; Config.Display = monitor.Output; Config.Rotation = monitor.Rotation;
                }
                await StartPipelineAsync(token);
                if (retainedSegments.Count > 0)
                {
                    string currentFormat = null;
                    try { string first; lock (gate) first = ReadCurrentSegments()[0].Name; currentFormat = await ProbeVideoFormatAsync(Path.Combine(session, first), token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { }
                    if (currentFormat != retainedVideoFormat || retainedRotation != Config.Rotation) DiscardRetainedSegments();
                }
                retainedVideoFormat = null; Failure = null; recovering = false;
            }
            finally { saver.Release(); }
        }
        void DiscardRetainedSegments()
        {
            lock (gate)
            {
                foreach (var segment in retainedSegments) TryDeleteConsumed(Path.Combine(session, segment.Name));
                retainedSegments.Clear(); retainedVideoFormat = null; BufferResetOnRecovery = true;
            }
        }
        async Task<string> ProbeVideoFormatAsync(string path, CancellationToken token)
        {
            using (var probe = new Process { StartInfo = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffprobe.exe"), "-v error -select_streams v:0 -show_entries stream=codec_name,width,height,pix_fmt -of csv=p=0 " + Quote(path)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } })
            {
                token.ThrowIfCancellationRequested(); Native.ThrowIfSessionEnding(); probe.Start();
                try
                {
                    Native.ProtectChild(probe); var output = probe.StandardOutput.ReadToEndAsync(); var error = probe.StandardError.ReadToEndAsync();
                    var deadline = Stopwatch.StartNew();
                    while (!probe.HasExited && deadline.ElapsedMilliseconds < 5000)
                    { await Task.Delay(100, token); Native.ThrowIfSessionEnding(); }
                    token.ThrowIfCancellationRequested(); Native.ThrowIfSessionEnding();
                    if (!probe.HasExited) throw new TimeoutException("Не удалось проверить формат видеобуфера.");
                    probe.WaitForExit(); string result = (await output).Trim(); string diagnostic = await error;
                    if (probe.ExitCode != 0 || result.Length == 0) throw new IOException("Не удалось прочитать видеобуфер. " + diagnostic);
                    return result;
                }
                finally { try { if (!probe.HasExited) { probe.Kill(); probe.WaitForExit(1000); } } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
            }
        }
        async Task StopPipelineAsync()
        {
            if (process != null && !process.HasExited)
            {
                try { await process.StandardInput.WriteLineAsync("q"); } catch { }
                bool exited = await Task.Run(() => process.WaitForExit(2500));
                if (!exited) { try { process.Kill(); } catch { } await Task.Run(() => process.WaitForExit(2500)); }
            }
            foreach (var a in audio) a.Dispose(); audio.Clear();
            if (process != null) { if (process.HasExited) await Task.Run(() => process.WaitForExit()); process.Dispose(); process = null; }
        }
        public async Task StopAsync()
        {
            stopping = true;
            if (cleaner != null) { cleaner.Dispose(); cleaner = null; }
            await StopPipelineAsync();
            // Await snapshots before deleting this session. Other instances' sessions are never touched.
            await saver.WaitAsync();
            try { lock (gate) { if (session != null && Directory.Exists(session)) Directory.Delete(session, true); session = null; retainedSegments.Clear(); } }
            catch (IOException) { }
            finally { saver.Release(); }
        }
        public void Dispose() { if (process != null) process.Dispose(); }
    }

    static class Native
    {
        static volatile bool sessionEnding;
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        internal static bool SessionEnding { get { return sessionEnding || GetSystemMetrics(0x2000) != 0; } }
        internal static void SetSessionEnding(bool ending) { sessionEnding = ending; }
        internal static void ThrowIfSessionEnding()
        { if (SessionEnding) throw new OperationCanceledException("Windows завершает сеанс."); }
        internal static void StopChildrenForShutdown()
        { lock (jobGate) { if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; } } }
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr security, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JobLimits info, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [StructLayout(LayoutKind.Sequential)] struct BasicLimits
        {
            public long ProcessTime, JobTime; public uint Flags;
            public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcesses;
            public UIntPtr Affinity; public uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] struct IoCounters
        { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] struct JobLimits
        { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        static readonly object jobGate = new object();
        static IntPtr job;
        public static void ProtectChild(Process child)
        {
            // Windows closes this handle even if ReplayLite crashes. No orphan recorder can fill the disk.
            lock (jobGate)
            {
                if (job == IntPtr.Zero)
                {
                    var created = CreateJobObject(IntPtr.Zero, null);
                    if (created == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                    var limits = new JobLimits(); limits.Basic.Flags = 0x2000;
                    if (!SetInformationJobObject(created, 9, ref limits, (uint)Marshal.SizeOf(typeof(JobLimits))))
                    { int error = Marshal.GetLastWin32Error(); CloseHandle(created); throw new System.ComponentModel.Win32Exception(error); }
                    job = created;
                }
                if (!AssignProcessToJobObject(job, child.Handle)) throw new System.ComponentModel.Win32Exception();
            }
        }
    }
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--self-test") return SelfTest.Run(args).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--buffer-test") return SelfTest.BufferResetTest(args[1]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--audio-test") return AudioRecoveryTests.Run(args[1]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--capture-recovery-test") return CaptureRecoveryTests.Run(args[1]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--job-test") { SelfTest.JobExitTest(args[1]); return 0; }
            if (args.Length == 2 && (args[0] == "--startup-test" || args[0] == "--capture-recovery-live-test"))
            {
                Directory.CreateDirectory(args[1]);
                using (var form = new MainForm(true, true))
                {
                    int exitCode = 1; var handle = form.Handle;
                    form.BeginInvoke(new Action(async delegate
                    {
                        try { await form.VerifyBackgroundStartupAsync(args[1], args[0] == "--capture-recovery-live-test"); exitCode = 0; }
                        catch (Exception error) { File.WriteAllText(Path.Combine(args[1], "startup-test.txt"), error.ToString()); }
                        finally { Application.ExitThread(); }
                    }));
                    Application.Run(); return exitCode;
                }
            }
            if (args.Length == 2 && (args[0] == "--ui-test" || args[0] == "--ui-live-test"))
            {
                using (var form = new MainForm(false, true))
                {
                    int exitCode = 1;
                    form.Shown += async delegate { try { if (args[0] == "--ui-live-test") await form.VerifyLiveUiAsync(args[1]); else await form.VerifyUiAsync(args[1]); exitCode = 0; } catch (Exception error) { File.WriteAllText(args[0] == "--ui-live-test" ? Path.Combine(args[1], "ui-live-test.txt") : args[1], error.ToString()); } finally { Application.ExitThread(); } };
                    Application.Run(form); return exitCode;
                }
            }
            if (args.Length == 2 && (args[0] == "--screenshot" || args[0] == "--settings-screenshot"))
            {
                using (var form = new MainForm(false, true))
                {
                    if (args[0] == "--settings-screenshot") form.PreviewSettingsForDocs(); else form.PreviewForDocs(); form.Show(); form.Hide(); form.Show(); form.Activate(); Application.DoEvents(); form.VerifyControls(); Thread.Sleep(200);
                    using (var image = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(args[1]); }
                }
                return 0;
            }
            using (var singleton = new Mutex(true, "Local\\ReplayLite.Main", out SingletonCreated))
            {
                bool launchInTray = Array.IndexOf(args, "--startup") >= 0;
                if (!SingletonCreated) { if (!launchInTray) MessageBox.Show("ReplayLite уже запущен. Открой его из трея."); return 0; }
                using (var form = new MainForm(launchInTray))
                {
                    if (!launchInTray) Application.Run(form);
                    else
                    {
                        form.FormClosed += delegate { Application.ExitThread(); };
                        var handle = form.Handle;
                        form.BeginInvoke(new Action(async delegate { await form.StartInBackgroundAsync(); }));
                        Application.Run();
                    }
                }
                return 0;
            }
        }
        static bool SingletonCreated;
    }
}
