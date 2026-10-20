using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReplayLite
{
    static class CaptureRecoveryTests
    {
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        sealed class Capture
        {
            internal int Failures, Width = 64;
            internal bool PermanentFailure, EndSessionOnLaunch, CancelStart;
            internal Process Launch(List<string> production, string session, Action<string> log)
            {
                if (CancelStart) { Native.SetSessionEnding(false); throw new OperationCanceledException("Windows завершает сеанс."); }
                if (Failures > 0) { Failures--; throw new IOException("[Parsed_ddagrab_0] DuplicateOutput failed: 80070005"); }
                if (PermanentFailure) throw new IOException("Недостаточно места для буфера");
                int output = production.FindLastIndex(s => s == "-f");
                var args = new List<string> { "-v", "error", "-re", "-f", "lavfi", "-i", "testsrc2=size=" + Width + "x64:rate=5", "-an", "-c:v", "libx264", "-preset", "ultrafast", "-g", "10", "-bf", "0" };
                args.AddRange(production.Skip(output));
                var process = Recorder.Launch(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe"), args, session, log);
                if (EndSessionOnLaunch) Native.SetSessionEnding(true);
                return process;
            }
        }
        static List<Segment> Segments(Recorder recorder)
        { return (List<Segment>)typeof(Recorder).GetMethod("ReadSegments", Private).Invoke(recorder, null); }
        static string Session(Recorder recorder) { return (string)typeof(Recorder).GetField("session", Private).GetValue(recorder); }
        static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        static string Hash(string path)
        { using (var hash = SHA256.Create()) using (var file = File.OpenRead(path)) return Convert.ToBase64String(hash.ComputeHash(file)); }
        static async Task LoseDesktop(Recorder recorder)
        {
            var process = (Process)typeof(Recorder).GetField("process", Private).GetValue(recorder);
            try { await process.StandardInput.WriteLineAsync("q"); } catch (IOException) { }
            bool exited = await Task.Run(() => process.WaitForExit(5000)); Assert(exited, "Fixture did not stop"); process.WaitForExit();
            typeof(Recorder).GetMethod("Log", Private).Invoke(recorder, new object[] { "[Parsed_ddagrab_0] AcquireNextFrame failed: 887a0026" });
        }
        static async Task Decode(string file)
        {
            var errors = new StringBuilder();
            using (var process = Recorder.Launch(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe"), new[] { "-v", "error", "-i", file, "-f", "null", "-" }, Path.GetDirectoryName(file), line => { lock (errors) errors.AppendLine(line); }))
            {
                bool exited = await Task.Run(() => process.WaitForExit(10000)); if (!exited) { process.Kill(); throw new Exception("Decode timed out"); }
                process.WaitForExit(); Assert(process.ExitCode == 0 && errors.Length == 0, "Recovered clip failed decoding: " + errors);
            }
        }
        static async Task InterruptProbe(Recorder recorder)
        {
            string name = "replaylite-probe-test-" + Guid.NewGuid().ToString("N");
            using (var cancel = new CancellationTokenSource())
            using (var pipe = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.Out, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous))
            {
                try
                {
                    var connected = pipe.WaitForConnectionAsync();
                    var probe = (Task<string>)typeof(Recorder).GetMethod("ProbeVideoFormatAsync", Private).Invoke(recorder, new object[] { @"\\.\pipe\" + name, cancel.Token });
                    Assert(await Task.WhenAny(connected, Task.Delay(4000)) == connected, "Probe did not connect to fixture"); await connected;
                    Native.SetSessionEnding(true); bool canceled = false;
                    try { await probe; } catch (OperationCanceledException) { canceled = true; }
                    Assert(canceled, "In-flight ffprobe ignored session shutdown");
                }
                finally { cancel.Cancel(); Native.SetSessionEnding(false); }
            }
        }
        internal static async Task<int> Run(string root)
        {
            Directory.CreateDirectory(root); var report = new StringBuilder(); var capture = new Capture(); int exit = 1;
            using (var recorder = new Recorder(new Settings { Seconds = 30, Audio = "none", Folder = root }, capture.Launch))
            {
                try
                {
                    Assert(CaptureRecovery.IsTransient("[Parsed_ddagrab_0] AcquireNextFrame failed: 887a0026"), "Access lost not classified");
                    Assert(!CaptureRecovery.IsTransient("Guessed Channel Layout: stereo") && !CaptureRecovery.IsTransient("Недостаточно места для буфера"), "Permanent error classified as transient");
                    Assert(CaptureRecovery.RetrySeconds(20) == 30, "Retry backoff not bounded"); report.AppendLine("DXGI_CLASSIFICATION_AND_BOUNDED_BACKOFF: PASS");
                    await recorder.StartAsync(); await Task.Delay(2200); await LoseDesktop(recorder);
                    var old = Segments(recorder); Assert(old.Count > 0, "No retained fixture frames");
                    var hashes = old.ToDictionary(s => s.Name, s => Hash(Path.Combine(Session(recorder), s.Name)));
                    int oldPid = recorder.ProcessId; double before = recorder.BufferedSeconds;
                    Native.SetSessionEnding(true);
                    try
                    {
                        bool blockedProbe = false, blockedCapture = false, blockedRecovery = false;
                        try { await (Task<string>)typeof(Recorder).GetMethod("ProbeVideoFormatAsync", Private).Invoke(recorder, new object[] { Path.Combine(Session(recorder), old[0].Name), CancellationToken.None }); } catch (OperationCanceledException) { blockedProbe = true; }
                        try { using (var child = Recorder.Launch("does-not-exist.exe", new string[0], root, null)) { } } catch (OperationCanceledException) { blockedCapture = true; }
                        try { await recorder.RecoverAsync(CancellationToken.None); } catch (OperationCanceledException) { blockedRecovery = true; }
                        Assert(blockedProbe && blockedCapture && blockedRecovery && recorder.BufferedSeconds >= before - .05, "Shutdown launched media or discarded buffer");
                    }
                    finally { Native.SetSessionEnding(false); }
                    report.AppendLine("SHUTDOWN_BLOCKS_FFPROBE_CAPTURE_AND_RECOVERY_PRESERVES_BUFFER: PASS");
                    await InterruptProbe(recorder); report.AppendLine("SHUTDOWN_INTERRUPTS_RUNNING_FFPROBE: PASS");
                    capture.Failures = 2;
                    for (int i = 0; i < 2; i++)
                    {
                        bool failed = false;
                        try { await recorder.RecoverAsync(CancellationToken.None); }
                        catch (IOException error) { failed = CaptureRecovery.IsTransient(error.ToString()); }
                        Assert(failed && recorder.BufferedSeconds >= before - .05, "Temporary recovery failure discarded buffer");
                    }
                    report.AppendLine("TEMPORARILY_UNAVAILABLE_DESKTOP_RETAINS_BUFFER: PASS");
                    await recorder.RecoverAsync(CancellationToken.None);
                    Assert(recorder.Running && recorder.ProcessId != oldPid && recorder.BufferedSeconds > before && !recorder.BufferResetOnRecovery, "Capture did not recover with retained buffer");
                    foreach (var pair in hashes) Assert(Hash(Path.Combine(Session(recorder), pair.Key)) == pair.Value, "Retained file overwritten");
                    var combined = Segments(recorder);
                    Assert(combined.Select(s => s.Name).Distinct().Count() == combined.Count && combined.Zip(combined.Skip(1), (a, b) => b.Start >= a.End - .001).All(v => v), "Recovery timeline or numbering overlaps");
                    report.AppendLine("RECOVERY_PRESERVES_SEGMENTS_AND_CONTINUES_TIMELINE: PASS");
                    string first = await recorder.SaveAsync(); await Decode(first);
                    Assert(!Segments(recorder).Any(s => hashes.ContainsKey(s.Name)), "Saved retained frames repeated");
                    report.AppendLine("EXPORT_ACROSS_RESTART_DECODE_AND_RESET: PASS");
                    var deadline = Stopwatch.StartNew(); while (recorder.BufferedSeconds < 2 && deadline.ElapsedMilliseconds < 7000) await Task.Delay(100);
                    await LoseDesktop(recorder); await recorder.RecoverAsync(CancellationToken.None); string second = await recorder.SaveAsync(); await Decode(second);
                    report.AppendLine("SECOND_LOSS_AFTER_SAVE_RECOVERS: PASS");
                    await LoseDesktop(recorder); capture.Width = 80; await recorder.RecoverAsync(CancellationToken.None);
                    Assert(recorder.BufferResetOnRecovery && recorder.Running, "Changed resolution did not reset incompatible buffer");
                    string resized = await recorder.SaveAsync(); await Decode(resized); report.AppendLine("RESOLUTION_CHANGE_RESETS_ONLY_INCOMPATIBLE_BUFFER: PASS");
                    await LoseDesktop(recorder); var cancel = new CancellationTokenSource(); cancel.Cancel(); bool canceled = false;
                    try { await recorder.RecoverAsync(cancel.Token); } catch (OperationCanceledException) { canceled = true; } cancel.Dispose();
                    Assert(canceled, "Recovery ignores cancellation"); report.AppendLine("RECOVERY_CANCELLATION: PASS");
                    capture.PermanentFailure = true; bool permanent = false;
                    try { await recorder.RecoverAsync(CancellationToken.None); } catch (IOException error) { permanent = !CaptureRecovery.IsTransient(error.ToString()); }
                    Assert(permanent, "Stale DXGI log hides permanent failure"); report.AppendLine("PERMANENT_FAILURE_NOT_HIDDEN_BY_OLD_DXGI_LOG: PASS");
                    foreach (bool alreadyCanceled in new[] { false, true })
                    {
                        var starting = new Capture { EndSessionOnLaunch = !alreadyCanceled, CancelStart = alreadyCanceled };
                        using (var startup = new Recorder(new Settings { Seconds = 30, Audio = "none", Folder = root }, starting.Launch))
                        {
                            bool interrupted = false;
                            try { await startup.StartAsync(); } catch (OperationCanceledException) { interrupted = true; }
                            finally { Native.SetSessionEnding(false); }
                            Assert(interrupted && Directory.Exists(Session(startup)), "Shutdown interruption destroyed startup session");
                            starting.EndSessionOnLaunch = starting.CancelStart = false; await startup.RecoverAsync(CancellationToken.None);
                            Assert(startup.Running, "Canceled shutdown could not recover interrupted startup"); await startup.StopAsync();
                        }
                    }
                    report.AppendLine("CANCELED_SHUTDOWN_RECOVERS_INTERRUPTED_INITIAL_START: PASS"); exit = 0;
                }
                catch (Exception error) { report.AppendLine("FAIL: " + error); report.AppendLine(recorder.ErrorLog); }
                await recorder.StopAsync(); Assert(!recorder.Running, "Recovery recorder did not stop");
            }
            File.WriteAllText(Path.Combine(root, "capture-recovery-test.txt"), report.ToString(), new UTF8Encoding(false)); return exit;
        }
    }
}
