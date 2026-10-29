using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;

namespace ReplayLite
{
    sealed class ClipPlayer : UserControl
    {
        readonly PictureBox surface = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom };
        readonly ActionButton play = new ActionButton { IconName = "pause" };
        readonly ValueSlider seek = new ValueSlider { Maximum = 1000 }, volume = new ValueSlider { Value = 70 };
        readonly ThemeLabel time = new ThemeLabel(), status = new ThemeLabel();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 };
        readonly object frameGate = new object(); readonly Stopwatch clock = new Stopwatch();
        readonly ClipEntry clip; readonly ClipPreview preview;
        CancellationTokenSource cancel; Task work = Task.FromResult(0); Bitmap frame;
        byte[] latest = new byte[640 * 360 * 3], reading = new byte[640 * 360 * 3];
        bool posted, hasFrame, disposed, changingSeek, seeking, starting; int generation; double offset, pendingSeek;
        WaveOutEvent output;
        internal long Frames { get; private set; }
        internal bool IsPlaying { get; private set; }
        internal double Position { get { return Math.Min(preview.Duration > 0 ? preview.Duration : double.MaxValue, offset + (clock.IsRunning ? clock.Elapsed.TotalSeconds : 0)); } }
        internal Task Completion { get { return work; } }
        internal string Filename { get { return clip.Name; } }
        internal bool HasRenderedFrame { get { return frame != null; } }
        internal string Diagnostic { get { return status.Text + " | frames=" + Frames + " playing=" + IsPlaying; } }
        internal Color CenterPixel { get { return frame == null ? Color.Empty : frame.GetPixel(320, 180); } }
        internal void SetVolume(int value) { volume.Value = value; }
        internal void ScaleContent(float factor) { if (Math.Abs(factor - 1) > .01) foreach (Control control in Controls) control.Scale(new SizeF(factor, factor)); }
        internal void RefreshLanguage() { play.AccessibleName = L.T(IsPlaying ? "Пауза" : "Воспроизвести"); Invalidate(true); }
        public ClipPlayer(ClipEntry clip, ClipPreview preview)
        {
            this.clip = clip; this.preview = preview; BackColor = Theme.Background;
            surface.SetBounds(0, 0, 720, 242); surface.BackColor = Theme.Background; Controls.Add(surface);
            play.SetBounds(0, 254, 38, 34); play.Fill = Theme.Input; play.AccessibleName = L.T("Пауза"); Controls.Add(play);
            seek.SetBounds(48, 257, 400, 28); seek.AccessibleName = L.T("Позиция воспроизведения"); seek.BackColor = Theme.Background; Controls.Add(seek);
            time.SetBounds(456, 258, 114, 28); time.TextAlign = ContentAlignment.MiddleCenter; time.ForeColor = Theme.Muted; Controls.Add(time);
            volume.SetBounds(586, 257, 134, 28); volume.AccessibleName = L.T("Громкость плеера"); volume.BackColor = Theme.Background; Controls.Add(volume);
            status.SetBounds(0, 296, 720, 24); status.ForeColor = Theme.Muted; status.Font = new Font("Segoe UI", 9); status.AutoEllipsis = true; Controls.Add(status);
            play.Click += async delegate { if (IsPlaying) Pause(); else await PlayAsync(); };
            seek.MouseDown += delegate { changingSeek = true; };
            seek.ValueCommitted += async delegate
            {
                if (!changingSeek && Math.Abs(seek.Value / 1000.0 * preview.Duration - Position) <= .6) return;
                changingSeek = false; pendingSeek = seek.Value / 1000.0 * preview.Duration; if (seeking) return; seeking = true;
                try { double target; do { target = pendingSeek; await SeekAsync(target); } while (target != pendingSeek && !disposed); } finally { seeking = false; }
            };
            volume.ValueChanged += delegate { if (output != null) output.Volume = volume.Value / 100f; };
            timer.Tick += delegate { if (!changingSeek && preview.Duration > 0) seek.Value = (int)(Position / preview.Duration * 1000); time.Text = Stamp(Position) + " / " + Stamp(preview.Duration); };
            VisibleChanged += delegate { if (!Visible) Pause(); };
        }
        internal static string Stamp(double seconds) { return TimeSpan.FromSeconds(double.IsNaN(seconds) || double.IsInfinity(seconds) ? 0 : Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture); }
        internal void Pause()
        {
            offset = Position; clock.Reset(); IsPlaying = false; timer.Stop(); generation++;
            if (cancel != null) cancel.Cancel(); if (output != null) { try { output.Stop(); } catch (InvalidOperationException) { } }
            play.IconName = "square-play"; play.AccessibleName = L.T("Воспроизвести"); play.Invalidate();
        }
        internal async Task SeekAsync(double position)
        { bool resume = IsPlaying; Pause(); await work; offset = Math.Max(0, Math.Min(preview.Duration, position)); time.Text = Stamp(offset) + " / " + Stamp(preview.Duration); if (resume) await PlayAsync(); }
        internal async Task PlayAsync()
        {
            if (disposed || IsPlaying || starting) return; starting = true; int request = generation;
            try { await work; } finally { starting = false; }
            if (disposed || !Visible || request != generation) return;
            if (!ClipLibrary.SameFile(clip)) { status.Text = L.T("Клип изменён или удалён. Обновите список."); return; }
            if (preview.Duration > 0 && offset >= preview.Duration - .1) offset = 0;
            cancel = new CancellationTokenSource(); int id = ++generation; var token = cancel.Token; Frames = 0;
            IsPlaying = true; play.IconName = "pause"; play.AccessibleName = L.T("Пауза"); play.Invalidate(); status.Text = L.T("Открываем видео…"); timer.Start();
            work = Run(id, token); await Task.Yield();
        }
        async Task Run(int id, CancellationToken token)
        {
            try
            {
                await ClipProcess.Gate.WaitAsync(token);
                try
                {
                    bool fallback = false;
                    try { await Decode(id, token, true); }
                    catch (IOException) { if (Frames != 0 || token.IsCancellationRequested) throw; fallback = true; }
                    if (fallback) { status.Text = L.T("Аппаратный просмотр недоступен, используем CPU."); await Decode(id, token, false); }
                }
                finally { ClipProcess.Gate.Release(); }
                if (!token.IsCancellationRequested && id == generation) { offset = preview.Duration; clock.Reset(); IsPlaying = false; timer.Stop(); play.IconName = "square-play"; play.Invalidate(); status.Text = ""; time.Text = Stamp(offset) + " / " + Stamp(preview.Duration); }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!disposed && id == generation) { Pause(); status.Text = L.T("Не удалось воспроизвести видео: ") + error.Message; } }
            finally { if (output != null) { output.Dispose(); output = null; } if (cancel != null && cancel.Token == token) { cancel.Cancel(); cancel.Dispose(); cancel = null; } }
        }
        async Task Decode(int id, CancellationToken token, bool hardware)
        {
            bool audio = preview.HasAudio; string pipeName = "ReplayLite-player-" + Guid.NewGuid().ToString("N");
            BufferedWaveProvider samples = null;
            if (audio) try { samples = new BufferedWaveProvider(new WaveFormat(48000, 16, 2)) { BufferDuration = TimeSpan.FromSeconds(1) }; output = new WaveOutEvent { DesiredLatency = 100, Volume = volume.Value / 100f }; output.Init(samples); }
            catch (Exception error) { if (output != null) { output.Dispose(); output = null; } audio = false; status.Text = L.T("Звук плеера недоступен: ") + error.Message; }
            using (var pipe = audio ? new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous) : null)
            {
                var args = new List<string> { "-v", "error", "-nostdin", "-y", "-threads", "2" };
                if (hardware) args.AddRange(new[] { "-hwaccel", "d3d11va" });
                args.AddRange(new[] { "-ss", offset.ToString("0.000", CultureInfo.InvariantCulture), "-re", "-i", clip.Path, "-map", "0:v:0", "-an", "-sn", "-dn", "-vf", "scale=640:360:force_original_aspect_ratio=decrease,pad=640:360:(ow-iw)/2:(oh-ih)/2", "-pix_fmt", "bgr24", "-threads", "1", "-f", "rawvideo", "pipe:1" });
                if (audio) args.AddRange(new[] { "-map", "0:a:0", "-vn", "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", "-f", "s16le", @"\\.\pipe\" + pipeName });
                using (var process = ClipProcess.Start("ffmpeg.exe", args))
                using (token.Register(() => { ClipProcess.Kill(process); if (pipe != null) pipe.Dispose(); }))
                {
                    try
                    {
                        var errors = ReadErrors(process.StandardError);
                        var watchdog = Task.Delay(7000, token).ContinueWith(task => { if (!task.IsCanceled && id == generation && Frames == 0) ClipProcess.Kill(process); }, TaskScheduler.Default);
                        Task audioTask = audio ? ReadAudio(pipe, samples, token) : Task.FromResult(0);
                        var audioFailure = audioTask.ContinueWith(task => { if (task.IsFaulted) ClipProcess.Kill(process); }, TaskScheduler.Default);
                        var videoTask = ReadVideo(process.StandardOutput.BaseStream, id, token);
                        var ended = Task.Run(() => process.WaitForExit());
                        await Task.WhenAll(videoTask, ended); if (pipe != null && process.ExitCode != 0) pipe.Dispose();
                        try { await audioTask; } catch (ObjectDisposedException) { } catch (IOException) { if (token.IsCancellationRequested) token.ThrowIfCancellationRequested(); }
                        if (samples != null && process.ExitCode == 0) { var drain = Stopwatch.StartNew(); while (samples.BufferedBytes > 0 && drain.ElapsedMilliseconds < 1000) await Task.Delay(10, token); }
                        token.ThrowIfCancellationRequested(); string error = await errors;
                        if (process.ExitCode != 0) throw new IOException(string.IsNullOrWhiteSpace(error) ? L.T("Не удалось прочитать видео.") : error.Substring(0, Math.Min(300, error.Length)));
                    }
                    finally { ClipProcess.Kill(process); Interlocked.Decrement(ref ClipProcess.Active); if (output != null) { output.Dispose(); output = null; } }
                }
            }
        }
        async Task ReadAudio(NamedPipeServerStream pipe, BufferedWaveProvider samples, CancellationToken token)
        {
            await pipe.WaitForConnectionAsync(token).ConfigureAwait(false); var bytes = new byte[8192]; bool started = false; int count;
            while ((count = await pipe.ReadAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false)) > 0)
            {
                while (samples.BufferedBytes > 48000) await Task.Delay(10, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); samples.AddSamples(bytes, 0, count); if (!started && samples.BufferedBytes >= 9600) { started = true; output.Play(); }
            }
        }
        static async Task<string> ReadErrors(StreamReader reader)
        { var text = new System.Text.StringBuilder(); var block = new char[1024]; int count; while ((count = await reader.ReadAsync(block, 0, block.Length).ConfigureAwait(false)) > 0) if (text.Length < 300) text.Append(block, 0, Math.Min(count, 300 - text.Length)); return text.ToString(); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); float scale = Width / 720f; LucideAssets.Draw(e.Graphics, "headphones", new Rectangle((int)(568 * scale), (int)(263 * scale), Math.Max(12, (int)(16 * scale)), Math.Max(12, (int)(16 * scale))), Theme.Muted); }
        async Task ReadVideo(Stream input, int id, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                int total = 0; while (total < reading.Length) { int read = await input.ReadAsync(reading, total, reading.Length - total, token).ConfigureAwait(false); if (read == 0) return; total += read; }
                lock (frameGate)
                {
                    var swap = latest; latest = reading; reading = swap; hasFrame = true; Frames++;
                    if (!posted && !disposed) { posted = true; try { BeginInvoke(new Action(() => RenderFrame(id))); } catch (InvalidOperationException) { posted = false; } }
                }
            }
        }
        void RenderFrame(int id)
        {
            lock (frameGate)
            {
                posted = false; if (disposed || id != generation || !hasFrame) return; hasFrame = false;
                if (frame == null) { frame = new Bitmap(640, 360, PixelFormat.Format24bppRgb); surface.Image = frame; }
                var pixels = frame.LockBits(new Rectangle(0, 0, 640, 360), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                try { Marshal.Copy(latest, 0, pixels.Scan0, latest.Length); } finally { frame.UnlockBits(pixels); }
            }
            if (!clock.IsRunning && IsPlaying) clock.Restart(); if (status.Text == L.T("Открываем видео…")) status.Text = ""; surface.Invalidate();
        }
        protected override void Dispose(bool disposing)
        { if (disposing && !disposed) { Pause(); disposed = true; timer.Dispose(); surface.Image = null; if (frame != null) frame.Dispose(); } base.Dispose(disposing); }
    }
}
