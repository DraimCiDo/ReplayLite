using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ReplayLite
{
    sealed class ClipEntry
    {
        public string Path, Name; public long Length; public DateTime Modified;
        public string Key { get { return Path + "|" + Length + "|" + Modified.Ticks; } }
    }
    sealed class ClipPreview { public double Duration; public bool HasAudio; public byte[] Image; public string Error; }
    sealed class ClipScan { public List<ClipEntry> Clips = new List<ClipEntry>(); public bool Limited; }
    static class ClipLibrary
    {
        internal const int MaximumEntries = 20000;
        static readonly SemaphoreSlim scanGate = new SemaphoreSlim(1, 1);
        internal static async Task<ClipScan> ScanAsync(string folder, CancellationToken token)
        { await scanGate.WaitAsync(token).ConfigureAwait(false); try { return await Task.Run(() => Scan(folder, token)).ConfigureAwait(false); } finally { scanGate.Release(); } }
        internal static ClipScan Scan(string folder, CancellationToken token)
        {
            var result = new ClipScan();
            foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested(); string extension = System.IO.Path.GetExtension(path);
                if (!extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var file = new FileInfo(path); if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    result.Clips.Add(new ClipEntry { Path = file.FullName, Name = file.Name, Length = file.Length, Modified = file.LastWriteTimeUtc });
                }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (result.Clips.Count >= MaximumEntries) { result.Limited = true; break; }
            }
            result.Clips.Sort((a, b) => { int order = b.Modified.CompareTo(a.Modified); return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name); }); return result;
        }
        internal static bool SameFile(ClipEntry clip)
        { try { var file = new FileInfo(clip.Path); return file.Exists && file.Length == clip.Length && file.LastWriteTimeUtc == clip.Modified && (file.Attributes & FileAttributes.ReparsePoint) == 0; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } }
        internal static async Task<ClipPreview> Preview(ClipEntry clip, CancellationToken token)
        {
            if (!SameFile(clip)) throw new IOException(L.T("Клип изменён или удалён. Обновите список."));
            var preview = new ClipPreview();
            byte[] json = await ClipProcess.Read("ffprobe.exe", new[] { "-v", "error", "-show_entries", "format=duration:stream=codec_type", "-of", "json", clip.Path }, 65536, token);
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(System.Text.Encoding.UTF8.GetString(json)); object value;
            if (data.TryGetValue("format", out value)) { object duration; var format = value as Dictionary<string, object>; if (format != null && format.TryGetValue("duration", out duration)) double.TryParse(Convert.ToString(duration, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out preview.Duration); }
            if (double.IsNaN(preview.Duration) || double.IsInfinity(preview.Duration) || preview.Duration < 0) preview.Duration = 0;
            if (data.TryGetValue("streams", out value)) foreach (Dictionary<string, object> stream in (IEnumerable)value) { object kind; if (stream.TryGetValue("codec_type", out kind) && (string)kind == "audio") preview.HasAudio = true; }
            try { preview.Image = await ClipProcess.Read("ffmpeg.exe", new[] { "-v", "error", "-nostdin", "-threads", "1", "-ss", preview.Duration > 2 ? "1" : "0", "-i", clip.Path, "-map", "0:v:0", "-an", "-sn", "-dn", "-frames:v", "1", "-vf", "scale=320:180:force_original_aspect_ratio=decrease,pad=320:180:(ow-iw)/2:(oh-ih)/2", "-threads", "1", "-f", "image2pipe", "-c:v", "mjpeg", "pipe:1" }, 512 * 1024, token); }
            catch (IOException error) { preview.Error = error.Message; }
            token.ThrowIfCancellationRequested(); return preview;
        }
    }
    sealed class ClipPreviewCache
    {
        readonly Dictionary<string, ClipPreview> items = new Dictionary<string, ClipPreview>(); readonly Queue<string> order = new Queue<string>();
        internal int Count { get { return items.Count; } }
        internal bool TryGet(ClipEntry entry, out ClipPreview preview) { return items.TryGetValue(entry.Key, out preview); }
        internal void Add(ClipEntry entry, ClipPreview preview)
        { if (items.ContainsKey(entry.Key)) return; while (items.Count >= 12) items.Remove(order.Dequeue()); items.Add(entry.Key, preview); order.Enqueue(entry.Key); }
        internal void Clear() { items.Clear(); order.Clear(); }
    }
    static class ClipProcess
    {
        internal static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        internal static int Active, Peak;
        internal static Process Start(string file, IEnumerable<string> args)
        {
            var process = new Process { StartInfo = new ProcessStartInfo(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", file), string.Join(" ", args.Select(Recorder.Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            Native.ThrowIfSessionEnding(); process.Start();
            try { Native.ProtectChild(process); } catch { Kill(process); process.Dispose(); throw; }
            int active = Interlocked.Increment(ref Active); Peak = Math.Max(Peak, active); return process;
        }
        internal static void Kill(Process process) { try { if (process != null && !process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
        static async Task<byte[]> Bounded(Stream input, int limit, CancellationToken token)
        {
            using (var memory = new MemoryStream()) { byte[] block = new byte[8192]; int read; while ((read = await input.ReadAsync(block, 0, block.Length, token).ConfigureAwait(false)) > 0) { if (memory.Length + read > limit) throw new IOException(L.T("Превышен размер данных предпросмотра.")); memory.Write(block, 0, read); } return memory.ToArray(); }
        }
        internal static async Task<byte[]> Read(string file, IEnumerable<string> args, int limit, CancellationToken token)
        {
            await Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                using (var process = Start(file, args))
                {
                    timeout.CancelAfter(7000);
                    try
                    {
                        using (timeout.Token.Register(() => Kill(process)))
                        {
                            var output = Bounded(process.StandardOutput.BaseStream, limit, timeout.Token); var errors = Bounded(process.StandardError.BaseStream, 65536, timeout.Token);
                            try { await Task.WhenAll(output, errors, Task.Run(() => process.WaitForExit())).ConfigureAwait(false); }
                            catch (OperationCanceledException) { Kill(process); token.ThrowIfCancellationRequested(); throw new IOException(L.T("Видео не ответило вовремя.")); }
                            catch { Kill(process); throw; }
                            token.ThrowIfCancellationRequested(); if (timeout.IsCancellationRequested || process.ExitCode != 0) throw new IOException(L.T("Не удалось прочитать видео."));
                            return output.Result;
                        }
                    }
                    finally { Kill(process); Interlocked.Decrement(ref Active); }
                }
            }
            finally { Gate.Release(); }
        }
    }
}
