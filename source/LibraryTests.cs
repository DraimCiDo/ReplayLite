using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReplayLite
{
    public sealed partial class MainForm
    {
        static async Task Until(Func<bool> ready, int milliseconds, string message)
        { var clock = System.Diagnostics.Stopwatch.StartNew(); while (!ready() && clock.ElapsedMilliseconds < milliseconds) await Task.Delay(30); if (!ready()) throw new Exception(message); }
        internal async Task VerifyLibraryAsync(string root)
        {
            Directory.CreateDirectory(root); var report = new StringBuilder(); string originalFolder = settings.Folder, originalLanguage = L.Language; timer.Stop();
            try
            {
                foreach (float scale in new[] { 1f, 1.5f, 2f }) using (var title = new BrandTitle { Text = "ReplayLite", VersionText = "v0.4.3", Font = new Font("Segoe UI", 23 * scale, FontStyle.Bold), Size = new Size((int)(240 * scale), (int)(64 * scale)), BackColor = Theme.Background, ForeColor = Theme.Text })
                using (var image = new Bitmap(title.Width, title.Height)) { title.DrawToBitmap(image, title.ClientRectangle); if (!title.ClientRectangle.Contains(title.VersionBounds) || !title.ClientRectangle.Contains(title.TitleBounds) || title.VersionBounds.IntersectsWith(title.TitleBounds)) throw new Exception("Header clips at scale " + scale); }
                report.AppendLine("COMPOSITE_HEADER_VISIBLE_GLYPHS_SCALES_100_150_200: PASS");
                string fixture = Path.Combine(root, "Клипы с пробелами"); Directory.CreateDirectory(fixture); string good = Path.Combine(fixture, "Кириллица-01.mp4");
                await ClipProcess.Read("ffmpeg.exe", new[] { "-v", "error", "-f", "lavfi", "-i", "color=c=red:s=640x360:r=30", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000", "-t", "3", "-c:v", "libx264", "-threads", "1", "-preset", "ultrafast", "-c:a", "aac", good }, 1024, CancellationToken.None);
                File.Copy(good, Path.Combine(fixture, "Second.MP4")); File.WriteAllText(Path.Combine(fixture, "broken.mkv"), "not a video"); File.WriteAllText(Path.Combine(fixture, "ignored.mp4.partial"), "partial"); File.WriteAllText(Path.Combine(fixture, "ignored.ts"), "buffer");
                var scan = ClipLibrary.Scan(fixture, CancellationToken.None); if (scan.Clips.Count != 3 || scan.Limited) throw new Exception("Library includes partial/buffer files or misses MP4/MKV.");
                ClipEntry entry = scan.Clips.Find(item => item.Path == good); ClipPreview preview = await ClipLibrary.Preview(entry, CancellationToken.None);
                if (!preview.HasAudio || preview.Duration < 2.8 || preview.Duration > 3.2 || preview.Image == null) throw new Exception("Clip metadata/thumbnail is not real.");
                using (var stream = new MemoryStream(preview.Image)) using (var image = new Bitmap(stream)) if (image.GetPixel(160, 90).R < 200 || image.GetPixel(160, 90).B > 50) throw new Exception("Thumbnail colors are incorrect.");
                bool broken = false; try { await ClipLibrary.Preview(scan.Clips.Find(item => item.Name == "broken.mkv"), CancellationToken.None); } catch (IOException) { broken = true; }
                if (!broken) throw new Exception("Invalid media was reported as a valid preview.");
                using (var canceled = new CancellationTokenSource()) { canceled.Cancel(); bool stopped = false; try { await ClipLibrary.Preview(entry, canceled.Token); } catch (OperationCanceledException) { stopped = true; } if (!stopped) throw new Exception("Preview ignores cancellation."); }
                report.AppendLine("LIBRARY_FILTER_REAL_METADATA_THUMBNAIL_CORRUPT_AND_CANCEL: PASS");
                settings.Folder = folder.Text = fixture; ShowLibrary(); await libraryLoading;
                if (recordingPage.Visible || settingsPage.Visible || !libraryOpen || clipCards.Count != 2 || libraryButton.Visible || update.Visible || !libraryNext.Enabled) throw new Exception("Library page navigation or pagination failed.");
                var oldCard = new WeakReference(clipCards[0]); libraryIndex = 1; await LoadLibraryPage(); if (clipCards.Count != 1 || !libraryPrevious.Enabled || libraryNext.Enabled) throw new Exception("Last library page is invalid.");
                libraryLoading = Task.FromResult(0); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); if (oldCard.IsAlive) throw new Exception("Disposed cards remain referenced after pagination.");
                languageChoiceForLibraryTest("en"); await libraryLoading; if (pageHeading.Text != "Replays") throw new Exception("Library header did not translate.");
                languageChoiceForLibraryTest("ru"); await libraryLoading;
                for (int i = 0; i < 20; i++) { clipCache.Add(new ClipEntry { Path = "cache-" + i, Length = i }, preview); } if (clipCache.Count != 12) throw new Exception("Preview cache grows beyond its bound.");
                report.AppendLine("LIBRARY_PAGINATION_RU_EN_DISPOSED_CARDS_RELEASED_CACHE_TWELVE: PASS");
                int gdiBefore; using (var process = System.Diagnostics.Process.GetCurrentProcess()) gdiBefore = GetGuiResources(process.Handle, 0);
                for (int cycle = 0; cycle < 10; cycle++) { libraryIndex = cycle % 2; await LoadLibraryPage(); }
                libraryLoading = Task.FromResult(0); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                using (var process = System.Diagnostics.Process.GetCurrentProcess()) { int growth = GetGuiResources(process.Handle, 0) - gdiBefore; if (growth > 2) throw new Exception("Library pagination leaks GDI resources: " + growth); report.AppendLine("TEN_LIBRARY_PAGES_GDI_DELTA=" + growth); }
                await OpenClip(entry); if (player == null) throw new Exception("Embedded player did not open."); player.SetVolume(0);
                try { await Until(() => player.Frames >= 6 && player.HasRenderedFrame, 10000, "Embedded player never decoded frames."); } catch { throw new Exception("Embedded player: " + player.Diagnostic); }
                if (player.CenterPixel.R < 200 || player.CenterPixel.B > 50) throw new Exception("Embedded player frame colors are wrong.");
                player.Pause(); await player.Completion; long frames = player.Frames; await Task.Delay(200); if (player.IsPlaying || player.Frames != frames || ClipProcess.Active != 0) throw new Exception("Paused player retains decode work.");
                await player.SeekAsync(.5); await player.PlayAsync(); await Until(() => player.Frames >= 4, 10000, "Playback failed after seek.");
                Hide(); await player.Completion; if (player.IsPlaying || ClipProcess.Active != 0) throw new Exception("Hidden player retains decoding/audio."); Show();
                ClosePlayer(); await libraryLoading; ShowSettings(false); if (libraryOpen || libraryWatcher != null || player != null || !libraryButton.Visible) throw new Exception("Leaving library retains worker or watcher.");
                report.AppendLine("EMBEDDED_REAL_VIDEO_AUDIO_PAUSE_SEEK_RESUME_HIDE_DISPOSE: PASS");
                File.WriteAllText(Path.Combine(root, "library-test.txt"), report.ToString(), new UTF8Encoding(false));
                ShowLibrary(); await libraryLoading; string deletable = Path.Combine(fixture, "delete-fixture.mp4"); File.WriteAllText(deletable, "owned test fixture"); var info = new FileInfo(deletable); var victim = new ClipEntry { Path = info.FullName, Name = info.Name, Length = info.Length, Modified = info.LastWriteTimeUtc };
                DeleteClip(victim, () => false, path => { throw new Exception("Canceled deletion was executed."); }); if (!File.Exists(deletable)) throw new Exception("Canceled deletion removed data.");
                DeleteClip(victim, () => true, path => { throw new IOException("Synthetic locked file"); }); if (!File.Exists(deletable)) throw new Exception("Failed deletion removed data.");
                DeleteClip(victim, () => true); await libraryLoading; if (File.Exists(deletable)) throw new Exception("Recycle operation failed.");
                File.AppendAllText(good, "changed"); if (ClipLibrary.SameFile(entry)) throw new Exception("Changed clip identity was accepted.");
                settings.Folder = Path.Combine(root, "missing"); await ReloadLibrary(); if (clipCards.Count != 0 || libraryNext.Enabled) throw new Exception("Missing folder did not become an error state.");
                report.AppendLine("LIBRARY_RECYCLE_CANCEL_FAILURE_REAL_RECYCLE_CHANGED_FILE_MISSING_FOLDER: PASS");
                VerifyPinnedIconFixture(root, report); ShowSettings(false); await Until(() => ClipProcess.Active == 0, 3000, "Library left a media process running.");
                if (ClipProcess.Peak != 1) throw new Exception("Library launched media work in parallel: " + ClipProcess.Peak);
                report.AppendLine("LIBRARY_MEDIA_PROCESS_PEAK_ONE_NO_PROCESSES_AFTER_CLOSE: PASS");
                File.WriteAllText(Path.Combine(root, "library-test.txt"), report.ToString(), new UTF8Encoding(false));
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(root, "library-test.txt"), report + error.ToString(), new UTF8Encoding(false)); throw; }
            finally { CloseLibrary(); settings.Folder = folder.Text = originalFolder; L.SetLanguage(originalLanguage); }
        }
        void languageChoiceForLibraryTest(string language) { L.SetLanguage(language); }
        void VerifyPinnedIconFixture(string root, StringBuilder report)
        {
            string links = Path.Combine(root, "links"); Directory.CreateDirectory(links); Type type = Type.GetTypeFromProgID("WScript.Shell"); object shell = Activator.CreateInstance(type); object link = null;
            string path = Path.Combine(links, "ReplayLite-test.lnk");
            try
            {
                link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path }); Type linkType = link.GetType(); linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Application.ExecutablePath }); linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                string icon = Path.Combine(root, "theme.ico"); using (var output = File.Create(icon)) taskbarIcon.Save(output);
                if (ShellIntegration.UpdatePinnedIcons(Application.ExecutablePath + ".other", links, icon) != 0 || ShellIntegration.UpdatePinnedIcons(Application.ExecutablePath, links, icon) != 1) throw new Exception("Pinned shortcut matching is not isolated to this app.");
                Marshal.FinalReleaseComObject(link); link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path }); string actual = (string)link.GetType().InvokeMember("IconLocation", BindingFlags.GetProperty, null, link, null); if (!actual.Contains(icon)) throw new Exception("Shortcut icon did not change.");
                ApplyPalette("violet"); if (ShellIntegration.SendMessage(Handle, 0x7f, new IntPtr(1), IntPtr.Zero) != taskbarIcon.Handle) throw new Exception("Native taskbar icon did not receive current theme."); ApplyPalette("mint");
                report.AppendLine("OWN_SHORTCUT_ICON_UPDATED_UNRELATED_TARGET_UNTOUCHED_NATIVE_THEME_ICON: PASS");
            }
            finally { if (link != null) Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
        }
    }
}
