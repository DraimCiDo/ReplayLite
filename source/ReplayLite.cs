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
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ReplayLite
{
    public sealed class Settings
    {
        public int Seconds = 120, Fps = 30, Mbps = 8, Display = 0;
        public string Audio = "both";
        public string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ReplayLite");
        public static readonly string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReplayLite");
        public static Settings Load()
        {
            var s = new Settings();
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
                        case "Display": if (int.TryParse(p[1], out v)) s.Display = Math.Max(0, Math.Min(7, v)); break;
                        case "Audio": if (new[] { "both", "system", "none" }.Contains(p[1])) s.Audio = p[1]; break;
                        case "Folder": if (Path.IsPathRooted(p[1])) s.Folder = p[1]; break;
                    }
                }
            }
            catch (IOException) { }
            return s;
        }
        public void Save()
        {
            Directory.CreateDirectory(Home);
            File.WriteAllLines(Path.Combine(Home, "settings.txt"), new[] { "Seconds=" + Seconds, "Fps=" + Fps, "Mbps=" + Mbps, "Display=" + Display, "Audio=" + Audio, "Folder=" + Folder }, Encoding.UTF8);
        }
    }

    // Raw audio alone is transported through small pipes. Video never passes through managed RAM.
    public sealed class AudioPipe : IDisposable
    {
        public readonly string Name = "ReplayLite-" + Guid.NewGuid().ToString("N");
        public string InputPath { get { return @"\\.\pipe\" + Name; } }
        readonly WasapiCapture capture;
        readonly BufferedWaveProvider buffer;
        readonly ISampleProvider samples;
        readonly NamedPipeServerStream pipe;
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        readonly ManualResetEvent stopped = new ManualResetEvent(false);
        Task pump;
        volatile bool disposed;
        long capturedBytes;
        public long CapturedBytes { get { return Interlocked.Read(ref capturedBytes); } }
        public string Failure;

        public AudioPipe(bool system)
        {
            try
            {
                capture = system ? new WasapiLoopbackCapture() : new WasapiCapture();
                buffer = new BufferedWaveProvider(capture.WaveFormat) { BufferDuration = TimeSpan.FromMilliseconds(500), DiscardOnBufferOverflow = true, ReadFully = true };
                ISampleProvider source = buffer.ToSampleProvider();
                if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
                if (source.WaveFormat.Channels != 2) throw new NotSupportedException("Аудиоустройство должно быть настроено на стерео или моно (не 5.1/7.1).");
                if (source.WaveFormat.SampleRate != 48000) source = new WdlResamplingSampleProvider(source, 48000);
                samples = source;
                pipe = new NamedPipeServerStream(Name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 16384, 16384);
                capture.DataAvailable += delegate(object sender, WaveInEventArgs e) { if (!disposed) { buffer.AddSamples(e.Buffer, 0, e.BytesRecorded); Interlocked.Add(ref capturedBytes, e.BytesRecorded); } };
                capture.RecordingStopped += delegate(object sender, StoppedEventArgs e) { if (e.Exception != null) Failure = e.Exception.Message; stopped.Set(); };
            }
            catch { if (capture != null) capture.Dispose(); if (pipe != null) pipe.Dispose(); throw; }
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
                    capture.StartRecording();
                    var values = new float[1920]; var bytes = new byte[values.Length * 4];
                    var clock = Stopwatch.StartNew(); long blocks = 0;
                    while (!stop.WaitOne(0))
                    {
                        // ReadFully inserts silence when loopback has no samples.
                        samples.Read(values, 0, values.Length);
                        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                        pipe.Write(bytes, 0, bytes.Length);
                        blocks++;
                        int delay = (int)(blocks * 20 - clock.ElapsedMilliseconds);
                        if (delay > 0) stop.WaitOne(delay);
                        // FFmpeg queue stays bounded; a blocked pipe applies backpressure.
                    }
                }
                catch (Exception e) { if (!disposed) Failure = e.Message; }
            });
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; stop.Set();
            try { capture.StopRecording(); } catch { }
            try { pipe.Dispose(); } catch { }
            if (pump != null) try { pump.Wait(2000); } catch { }
            stopped.WaitOne(500);
            capture.Dispose();
            // Events stay alive until any late audio callback exits.
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
        volatile bool stopping;
        public string Failure;
        public int ProcessId { get { return process == null ? 0 : process.Id; } }
        public bool Running { get { return process != null && !process.HasExited && !stopping; } }
        public double CpuSeconds { get { try { return process.TotalProcessorTime.TotalSeconds; } catch { return 0; } } }
        public long Memory { get { try { process.Refresh(); return process.WorkingSet64; } catch { return 0; } } }
        public long DiskBytes { get { lock (gate) { return session == null || !Directory.Exists(session) ? 0 : Directory.GetFiles(session, "*.ts").Sum(p => new FileInfo(p).Length); } } }
        public int SegmentCount { get { lock (gate) { return session == null || !Directory.Exists(session) ? 0 : Directory.GetFiles(session, "s*.ts").Length; } } }
        public long[] AudioBytes { get { return audio.Select(a => a.CapturedBytes).ToArray(); } }
        public double BufferedSeconds { get { lock (gate) { var a = ReadSegments(); return a.Count == 0 ? 0 : Math.Min(Config.Seconds, a.Last().End - a.First().Start); } } }
        public Recorder(Settings settings) { Config = settings; }

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
            p.Start();
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
            Directory.CreateDirectory(session);
            Exception startupError = null;
            try
            {
                CheckSpace();
                if (Config.Audio != "none") audio.Add(OpenAudio(true));
                if (Config.Audio == "both") audio.Add(OpenAudio(false));
                foreach (var a in audio) a.Start();
                var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-y", "-filter_threads", "1", "-filter_complex_threads", "1", "-thread_queue_size", "8", "-f", "lavfi", "-i", "ddagrab=output_idx=" + Config.Display + ":framerate=" + Config.Fps + ":draw_mouse=1" };
                foreach (var a in audio) args.AddRange(new[] { "-thread_queue_size", "32", "-probesize", "32", "-analyzeduration", "0", "-f", "f32le", "-ar", "48000", "-ac", "2", "-i", a.InputPath });
                args.AddRange(new[] { "-map", "0:v", "-c:v", "h264_nvenc", "-preset", "p1", "-tune", "ll", "-rc", "cbr", "-b:v", Config.Mbps + "M", "-maxrate", Config.Mbps + "M", "-bufsize", (Config.Mbps * 2) + "M", "-g", (Config.Fps * 2).ToString(), "-bf", "0", "-rc-lookahead", "0", "-surfaces", "4", "-fps_mode", "cfr" });
                if (audio.Count == 2) args.AddRange(new[] { "-filter_complex", "[1:a][2:a]amix=inputs=2:duration=longest:dropout_transition=0:normalize=0,alimiter=limit=0.95:latency=1[a]", "-map", "[a]" });
                else if (audio.Count == 1) args.AddRange(new[] { "-map", "1:a" });
                if (audio.Count > 0) args.AddRange(new[] { "-c:a", "aac", "-b:a", "160k", "-threads:a", "1" });
                args.AddRange(new[] { "-f", "segment", "-segment_format", "mpegts", "-segment_time", "2", "-segment_list", "segments.csv", "-segment_list_type", "csv", "-segment_list_size", ((Config.Seconds / 2) + 4).ToString(), "-segment_list_flags", "+live", "-reset_timestamps", "1", "s%09d.ts" });
                process = Launch(ffmpeg, args, session, Log);
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                cleaner = new System.Threading.Timer(delegate { Maintain(); }, null, 1000, 1000);
                for (int i = 0; i < 160; i++)
                {
                    await Task.Delay(100);
                    if (Failure != null) throw new Exception(Failure);
                    if (process.HasExited) throw new Exception("Захват не запущен. Проверь экран и драйвер NVIDIA.\n" + ErrorLog);
                    if (BufferedSeconds > 0) return;
                }
                throw new TimeoutException("За 16 секунд не появился видеобуфер.\n" + ErrorLog);
            }
            catch (Exception e) { startupError = e; }
            await StopAsync();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(startupError).Throw();
        }
        List<Segment> ReadSegments()
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
                        if (Path.GetFileName(name) == name && name.StartsWith("s") && name.EndsWith(".ts") && File.Exists(Path.Combine(session, name))) result.Add(new Segment { Name = name, Start = start, End = end });
                    }
                }
            }
            catch (IOException) { }
            return result;
        }
        static AudioPipe OpenAudio(bool system)
        {
            try { return new AudioPipe(system); }
            catch (Exception e)
            {
                throw new InvalidOperationException(system ? "Не удалось открыть звук компьютера. Проверь устройство вывода и стереоформат.\n" + e.Message : "Не удалось открыть микрофон. Проверь устройство ввода и разрешение для классических приложений в настройках конфиденциальности Windows.\n" + e.Message, e);
            }
        }
        void CheckSpace()
        {
            var drive = new DriveInfo(Path.GetPathRoot(session));
            long required = (long)((Config.Mbps * 1000000.0 / 8 + 24000) * (Config.Seconds + 12) * 1.2) + 128 * 1024 * 1024;
            if (drive.AvailableFreeSpace < required) throw new IOException("Недостаточно места для буфера: нужно примерно " + (required / 1048576) + " МБ.");
        }
        void Maintain()
        {
            if (stopping) return;
            try
            {
                if (process.HasExited) { Failure = "FFmpeg остановился.\n" + ErrorLog; return; }
                foreach (var a in audio) if (a.Failure != null) { Failure = "Ошибка аудиоустройства: " + a.Failure; return; }
                lock (gate)
                {
                    if (stopping) return;
                    var all = Directory.GetFiles(session, "s*.ts").OrderBy(p => p, StringComparer.Ordinal).ToArray();
                    int keep = Config.Seconds / 2 + 5;
                    foreach (var p in all.Take(Math.Max(0, all.Length - keep))) File.Delete(p);
                    if (new DriveInfo(Path.GetPathRoot(session)).AvailableFreeSpace < 128 * 1024 * 1024) Failure = "На диске осталось менее 128 МБ; запись остановлена.";
                }
            }
            catch (Exception e) { if (!stopping) Failure = "Ошибка буфера: " + e.Message; }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);
        public async Task<string> SaveAsync()
        {
            if (!await saver.WaitAsync(0)) throw new InvalidOperationException("Предыдущий повтор ещё сохраняется.");
            string snapshot = null, output = null, partial = null;
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
                using (var remux = Launch(ffmpeg, new[] { "-hide_banner", "-loglevel", "warning", "-y", "-f", "concat", "-safe", "1", "-i", "list.ffconcat", "-map", "0", "-c", "copy", "-bsf:a", "aac_adtstoasc", "-movflags", "+faststart", partial }, snapshot, line => { lock (log) { if (log.Length < 12000) log.AppendLine(line); } }))
                {
                    await Task.Run(delegate { if (!remux.WaitForExit(60000)) { remux.Kill(); remux.WaitForExit(); throw new TimeoutException("Сохранение заняло больше минуты."); } remux.WaitForExit(); });
                    if (remux.ExitCode != 0) throw new IOException("Не удалось сохранить повтор.\n" + log);
                }
                File.Move(partial, output); return output;
            }
            finally
            {
                if (partial != null && File.Exists(partial)) try { File.Delete(partial); } catch { }
                if (snapshot != null && Directory.Exists(snapshot)) try { Directory.Delete(snapshot, true); } catch { }
                saver.Release();
            }
        }
        public async Task StopAsync()
        {
            stopping = true;
            if (cleaner != null) { cleaner.Dispose(); cleaner = null; }
            if (process != null && !process.HasExited)
            {
                try { await process.StandardInput.WriteLineAsync("q"); } catch { }
                bool exited = await Task.Run(() => process.WaitForExit(2500));
                if (!exited) { try { process.Kill(); } catch { } await Task.Run(() => process.WaitForExit(2500)); }
            }
            foreach (var a in audio) a.Dispose(); audio.Clear();
            // Await snapshots before deleting this session. Other instances' sessions are never touched.
            await saver.WaitAsync();
            try { lock (gate) { if (session != null && Directory.Exists(session)) Directory.Delete(session, true); session = null; } }
            catch (IOException) { }
            finally { saver.Release(); }
        }
        public void Dispose() { if (process != null) process.Dispose(); }
    }

    public sealed class MainForm : Form
    {
        readonly Settings settings = Settings.Load();
        Recorder recorder;
        readonly ComboBox duration = new ComboBox(), fps = new ComboBox(), sound = new ComboBox();
        readonly NumericUpDown rate = new NumericUpDown(), display = new NumericUpDown();
        readonly TextBox folder = new TextBox();
        readonly Button start = new Button(), save = new Button(), browse = new Button();
        readonly Label state = new Label(), metrics = new Label(), hint = new Label();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
        bool busy, saving, closing, quitting;
        double lastCpu, lastTime;
        readonly Stopwatch clock = Stopwatch.StartNew();
        public MainForm()
        {
            duration.DropDownStyle = fps.DropDownStyle = sound.DropDownStyle = ComboBoxStyle.DropDownList;
            Text = "ReplayLite"; ClientSize = new Size(650, 530); MinimumSize = new Size(666, 569); MaximumSize = MinimumSize;
            StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(20, 24, 33); ForeColor = Color.FromArgb(228, 234, 242); Font = new Font("Segoe UI", 10);
            Icon = SystemIcons.Application; AutoScaleMode = AutoScaleMode.Dpi;
            AddLabel("ReplayLite", 28, 20, 24, FontStyle.Bold, 500, 48);
            AddLabel("ПОСЛЕДНИЕ МИНУТЫ · ОДНА ГОРЯЧАЯ КЛАВИША", 30, 73, 9, FontStyle.Regular, 570, 26).ForeColor = Color.FromArgb(132, 148, 167);
            AddLabel("Длительность", 30, 120, 10, FontStyle.Regular, 180, 24);
            AddLabel("Кадры / секунду", 245, 120, 10, FontStyle.Regular, 170, 24);
            AddLabel("Битрейт, Мбит/с", 448, 120, 10, FontStyle.Regular, 170, 24);
            duration.Items.AddRange(new object[] { "30 секунд", "2 минуты", "5 минут", "10 минут" }); duration.SelectedIndex = settings.Seconds == 30 ? 0 : settings.Seconds == 300 ? 2 : settings.Seconds == 600 ? 3 : 1;
            fps.Items.AddRange(new object[] { "30 — меньше нагрузка", "60 — плавнее" }); fps.SelectedIndex = settings.Fps == 60 ? 1 : 0;
            rate.Minimum = 2; rate.Maximum = 30; rate.Value = settings.Mbps;
            AddInput(duration, 30, 150, 190); AddInput(fps, 245, 150, 180); AddInput(rate, 448, 150, 170);
            AddLabel("Звук", 30, 198, 10, FontStyle.Regular, 400, 24);
            sound.Items.AddRange(new object[] { "Компьютер + микрофон", "Только компьютер", "Без звука" }); sound.SelectedIndex = settings.Audio == "none" ? 2 : settings.Audio == "system" ? 1 : 0; AddInput(sound, 30, 228, 395);
            AddLabel("Экран (индекс DXGI)", 448, 198, 10, FontStyle.Regular, 170, 24);
            display.Minimum = 0; display.Maximum = 7; display.Value = settings.Display; AddInput(display, 448, 228, 170);
            AddLabel("Папка для повторов", 30, 276, 10, FontStyle.Regular, 500, 24);
            folder.Text = settings.Folder; AddInput(folder, 30, 306, 520); StyleButton(browse, "…", 560, 305, 58, false);
            browse.Click += delegate { using (var d = new FolderBrowserDialog { SelectedPath = folder.Text, Description = "Папка для сохранённых MP4" }) if (d.ShowDialog() == DialogResult.OK) folder.Text = d.SelectedPath; };
            StyleButton(start, "Включить буфер", 30, 363, 282, true); StyleButton(save, "Сохранить · Ctrl+Shift+F9", 326, 363, 292, false); save.Enabled = false;
            state.SetBounds(30, 422, 590, 28); state.Text = "Буфер выключен"; Controls.Add(state);
            metrics.SetBounds(30, 452, 590, 25); metrics.Font = new Font("Segoe UI", 9); metrics.ForeColor = Color.FromArgb(132, 148, 167); metrics.Text = "GPU-захват · NVENC · буфер на диске · без предпросмотра"; Controls.Add(metrics);
            hint.SetBounds(30, 485, 590, 30); hint.Font = new Font("Segoe UI", 8); hint.Text = "Записывается весь выбранный экран в исходном разрешении. Закрытие → трей."; hint.ForeColor = metrics.ForeColor; Controls.Add(hint);
            start.Click += async delegate { await Toggle(); }; save.Click += async delegate { await SaveReplay(); };
            tray.Icon = Icon; tray.Text = "ReplayLite — буфер выключен"; tray.Visible = true;
            var menu = new ContextMenuStrip(); menu.Items.Add("Открыть", null, delegate { Restore(); }); menu.Items.Add("Сохранить повтор", null, async delegate { await SaveReplay(); }); menu.Items.Add("Включить / остановить", null, async delegate { await Toggle(); }); menu.Items.Add("Папка повторов", null, delegate { OpenFolder(); }); menu.Items.Add("Выход", null, async delegate { await Quit(); }); tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Restore(); };
            timer.Tick += async delegate { await RefreshState(); }; timer.Start();
            FormClosing += async delegate(object sender, FormClosingEventArgs e)
            {
                if (quitting) return;
                e.Cancel = true;
                if (e.CloseReason == CloseReason.WindowsShutDown) { await Quit(); return; }
                Hide(); tray.ShowBalloonTip(2000, "ReplayLite", "Приложение в трее. Для выхода выбери «Выход».", ToolTipIcon.Info);
            };
            Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChanged;
        }
        void PowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume && !IsDisposed) BeginInvoke(new Action(async delegate { if (recorder != null && !busy && !closing) { busy = true; try { await StopRecorder(); SetState("После сна буфер остановлен. Включи его снова."); } finally { busy = false; } } }));
        }
        Label AddLabel(string text, int x, int y, float size, FontStyle style, int w, int h)
        { var label = new Label { Text = text, Font = new Font("Segoe UI", size, style) }; label.SetBounds(x, y, w, h); Controls.Add(label); return label; }
        void AddInput(Control control, int x, int y, int width)
        {
            control.SetBounds(x, y, width, 33); control.BackColor = Color.FromArgb(32, 39, 52); control.ForeColor = ForeColor;
            var combo = control as ComboBox;
            if (combo != null)
            {
                combo.DropDownStyle = ComboBoxStyle.DropDownList;
                // Native Windows themed drop-downs paint a light background; use readable dark text.
                combo.BackColor = Color.White; combo.ForeColor = Color.FromArgb(20, 24, 33);
            }
            Controls.Add(control);
        }
        void StyleButton(Button b, string text, int x, int y, int width, bool accent)
        { b.Text = text; b.SetBounds(x, y, width, 44); b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.BackColor = accent ? Color.FromArgb(79, 211, 165) : Color.FromArgb(43, 55, 73); b.ForeColor = accent ? Color.FromArgb(12, 31, 26) : ForeColor; Controls.Add(b); }
        void SetState(string text) { state.Text = text; tray.Text = ("ReplayLite — " + text).Substring(0, Math.Min(63, ("ReplayLite — " + text).Length)); }
        void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        internal void VerifyControls()
        {
            if (duration.SelectedIndex < 0 || fps.SelectedIndex < 0 || sound.SelectedIndex < 0)
                throw new InvalidOperationException("A default drop-down value was lost.");
        }
        void OpenFolder() { Directory.CreateDirectory(settings.Folder); Process.Start("explorer.exe", Recorder.Quote(settings.Folder)); }
        void Inputs(bool enabled) { foreach (var c in new Control[] { duration, fps, sound, rate, display, folder, browse }) c.Enabled = enabled; }
        async Task Toggle()
        {
            if (busy || closing) return;
            busy = true; start.Enabled = false; save.Enabled = false;
            Exception startupError = null;
            try
            {
                if (recorder != null) { await StopRecorder(); SetState("Буфер выключен"); }
                else
                {
                    if (!Path.IsPathRooted(folder.Text)) throw new ArgumentException("Укажи абсолютный путь папки повторов.");
                    settings.Seconds = new[] { 30, 120, 300, 600 }[duration.SelectedIndex]; settings.Fps = fps.SelectedIndex == 1 ? 60 : 30; settings.Mbps = (int)rate.Value; settings.Display = (int)display.Value;
                    settings.Audio = new[] { "both", "system", "none" }[sound.SelectedIndex]; settings.Folder = folder.Text; settings.Save(); Inputs(false); SetState("Запускаю захват…");
                    recorder = new Recorder(settings); await recorder.StartAsync(); lastCpu = Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds + recorder.CpuSeconds; lastTime = clock.Elapsed.TotalSeconds; SetState("Запись буфера включена"); start.Text = "Остановить буфер"; save.Enabled = true;
                }
            }
            catch (Exception e) { startupError = e; }
            if (startupError != null) { if (recorder != null) await StopRecorder(); SetState("Не удалось включить буфер"); ShowError(startupError.Message); }
            busy = false; start.Enabled = true;
        }
        async Task StopRecorder()
        {
            var r = recorder; if (r == null) return; save.Enabled = false;
            await r.StopAsync(); r.Dispose(); recorder = null; Inputs(true); start.Text = "Включить буфер";
            metrics.Text = "GPU-захват · NVENC · буфер на диске · без предпросмотра";
        }
        async Task SaveReplay()
        {
            if (recorder == null || busy || saving || closing) return;
            saving = true; save.Enabled = false; SetState("Сохраняю повтор…");
            try { var output = await recorder.SaveAsync(); SetState("Сохранено: " + Path.GetFileName(output)); tray.ShowBalloonTip(3000, "Повтор сохранён", output, ToolTipIcon.Info); }
            catch (Exception e) { ShowError(e.Message); SetState(recorder == null ? "Буфер выключен" : "Ошибка сохранения — буфер работает"); }
            finally { saving = false; save.Enabled = recorder != null && recorder.Running && !busy; }
        }
        async Task RefreshState()
        {
            if (recorder == null || busy || closing) return;
            if (recorder.Failure != null || !recorder.Running)
            { busy = true; string message = recorder.Failure ?? ("Запись остановлена.\n" + recorder.ErrorLog); await StopRecorder(); SetState("Захват остановлен"); ShowError(message); busy = false; return; }
            try
            {
                var p = Process.GetCurrentProcess(); p.Refresh(); double total = p.TotalProcessorTime.TotalSeconds + recorder.CpuSeconds, now = clock.Elapsed.TotalSeconds;
                double cpu = Math.Max(0, (total - lastCpu) / Math.Max(0.01, now - lastTime) / Environment.ProcessorCount * 100); lastCpu = total; lastTime = now;
                metrics.Text = string.Format("Буфер {0:0}/{1} с   ·   CPU {2:0.0}%   ·   RAM {3:0} МБ   ·   диск {4:0} МБ", recorder.BufferedSeconds, settings.Seconds, cpu, (p.WorkingSet64 + recorder.Memory) / 1048576.0, recorder.DiskBytes / 1048576.0);
            }
            catch (Exception e) { SetState("Ошибка состояния: " + e.Message); }
        }
        void ShowError(string message) { Restore(); MessageBox.Show(this, message, "ReplayLite", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        async Task Quit()
        {
            if (closing) return; closing = true; timer.Stop(); start.Enabled = save.Enabled = false;
            while (busy) await Task.Delay(100);
            if (recorder != null) await StopRecorder();
            tray.Visible = false; tray.Dispose(); Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChanged; Native.UnregisterHotKey(Handle, 1); quitting = true; Close();
        }
        protected override void OnShown(EventArgs e)
        { base.OnShown(e); if (!Native.RegisterHotKey(Handle, 1, 0x4000 | 0x0002 | 0x0004, (uint)Keys.F9)) hint.Text = "Ctrl+Shift+F9 занята другим приложением. Используй кнопку сохранения."; }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1) { var pending = SaveReplay(); return; }
            base.WndProc(ref m);
        }
    }
    static class Native
    {
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
            if (args.Length == 2 && args[0] == "--job-test") { SelfTest.JobExitTest(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--screenshot")
            {
                using (var form = new MainForm())
                {
                    form.Show(); form.Hide(); form.Show(); form.Activate(); Application.DoEvents(); form.VerifyControls(); Thread.Sleep(200);
                    using (var image = new Bitmap(form.Width, form.Height))
                    using (var graphics = Graphics.FromImage(image))
                    { graphics.CopyFromScreen(form.Location, Point.Empty, form.Size); image.Save(args[1]); }
                }
                return 0;
            }
            using (var singleton = new Mutex(true, "Local\\ReplayLite.Main", out SingletonCreated))
            {
                if (!SingletonCreated) { MessageBox.Show("ReplayLite уже запущен. Открой его из трея."); return 0; }
                Application.Run(new MainForm()); return 0;
            }
        }
        static bool SingletonCreated;
    }
}
