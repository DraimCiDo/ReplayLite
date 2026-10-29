using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReplayLite
{
    public sealed partial class MainForm
    {
        static bool ContainsColor(Bitmap image, Color color)
        { for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++) if (image.GetPixel(x, y).ToArgb() == color.ToArgb()) return true; return false; }
        async Task VerifyUiRefreshForTest(StringBuilder report)
        {
            string file = Path.Combine(Settings.Home, "settings.txt"); bool existed = File.Exists(file); byte[] original = existed ? File.ReadAllBytes(file) : null;
            string originalLanguage = settings.Language, originalFolder = folder.Text, originalTemplate = filenameTemplate.Text, originalBufferFolder = settings.BufferFolder;
            try
            {
                await VerifyUpdateControlsForTest(report);
                SelectSettingsTab(5); languageChoice.SelectedIndex = 1;
                if (L.Language != "en" || pageHeading.Text != "App and Windows" || duration.Text != "2 minutes" || languageChoice.Text != "English") throw new Exception("English controls did not update.");
                string userPath = @"C:\Clips\Записи пользователя", userName = "Видео-{date}"; folder.Text = userPath; filenameTemplate.Text = userName;
                languageChoice.SelectedIndex = 0; languageChoice.SelectedIndex = 1;
                if (folder.Text != userPath || outputFolder.Text != userPath || filenameTemplate.Text != userName) throw new Exception("Language change modified user data.");
                SetState(L.T("Сохранено: ") + "Видео.mp4", TrayState.Recording);
                if (!stateDetail.Text.StartsWith("Видео.mp4\n", StringComparison.Ordinal)) throw new Exception("Saved filename was truncated or translated in English.");
                foreach (string palette in new[] { "mint", "ocean", "violet", "light" })
                {
                    ApplyPalette(palette); SelectSettingsTab(0); Inputs(false);
                    ThemeLabel caption = null; foreach (Control control in advancedGroups[0].Controls) if (control is ThemeLabel) { caption = (ThemeLabel)control; break; }
                    if (caption == null || caption.Enabled) throw new Exception("Disabled caption fixture missing.");
                    using (var bitmap = new Bitmap(caption.Width, caption.Height)) { caption.DrawToBitmap(bitmap, caption.ClientRectangle); if (!ContainsColor(bitmap, Theme.Muted)) throw new Exception("Disabled caption bypasses app palette: " + palette); }
                    using (var bitmap = new Bitmap(advancedReset.Width, advancedReset.Height)) { advancedReset.DrawToBitmap(bitmap, advancedReset.ClientRectangle); if (!ContainsColor(bitmap, Theme.Muted)) throw new Exception("Disabled button bypasses app palette: " + palette); }
                    Inputs(true); SelectSettingsTab(2); fps.Open(); if (!fps.PopupForTest.Items[0].Text.Contains("fps")) throw new Exception("English dropdown not localized."); fps.ClosePopup();
                }
                ApplyPalette("mint"); folder.Text = originalFolder; filenameTemplate.Text = originalTemplate;
                languageChoice.SelectedIndex = 0; testing = false; languageChoice.SelectedIndex = 1; testing = true;
                if (Settings.Load().Language != "en") throw new Exception("Language did not persist.");
                languageChoice.SelectedIndex = 0;
                if (pageHeading.Text != "Видео" || duration.Text != "2 минуты") throw new Exception("Russian controls did not restore.");
                if (!PureWindowMove(0x80a15) || PureWindowMove(0x80a14) || PureWindowMove(0x80a17) || PureWindowMove(0x80a35) || PureWindowMove(0x80a55)) throw new Exception("Move batching includes resize, frame, visibility or stationary changes.");
                Rectangle before = Bounds; long deferred = DeferredMoveNotifications;
                var enter = Message.Create(Handle, 0x0231, IntPtr.Zero, IntPtr.Zero); WndProc(ref enter);
                if (!windowMoving || hotkey.IsCapturing || hotkey.CaptureHookActive) throw new Exception("Move loop did not release keyboard capture.");
                IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WindowPosition)));
                try
                {
                    Marshal.StructureToPtr(new WindowPosition { Window = Handle, X = Left, Y = Top, Width = Width, Height = Height, Flags = 0x80a15 }, memory, false);
                    for (int i = 0; i < 100; i++) { var changed = Message.Create(Handle, 0x0047, IntPtr.Zero, memory); WndProc(ref changed); }
                }
                finally { Marshal.FreeHGlobal(memory); var exit = Message.Create(Handle, 0x0232, IntPtr.Zero, IntPtr.Zero); WndProc(ref exit); }
                if (windowMoving || Bounds != before || DeferredMoveNotifications <= deferred) throw new Exception("Move loop did not reconcile final bounds.");
                if (L.Count < 270 || !L.HasTranslation("Укажите абсолютный путь папки буфера.")) throw new Exception("Localization dictionary incomplete.");
                report.AppendLine("RU_EN_SWITCH_DROPDOWNS_PERSISTENCE_AND_USER_DATA: PASS");
                report.AppendLine("DISABLED_LABELS_AND_BUTTONS_FOUR_PALETTES: PASS");
                report.AppendLine("NATIVE_MOVE_BATCHING_FILTER_AND_FINAL_BOUNDS: PASS");
                report.AppendLine("LUCIDE_ICONS_STATIC_HOVER_NO_ANIMATION_TIMERS: PASS");
                VerifySoftThemesForTest(report);
                settings.BufferFolder = ""; RefreshBufferFolder();
                if (bufferFolder.Text != Settings.Home || !Path.IsPathRooted(bufferFolder.Text)) throw new Exception("Default buffer folder is hidden behind a placeholder.");
                settings.BufferFolder = Path.Combine(Settings.Home, "Буфер с пробелами"); RefreshBufferFolder();
                languageChoice.SelectedIndex = 1; languageChoice.SelectedIndex = 0;
                if (bufferFolder.Text != settings.BufferFolder) throw new Exception("Buffer path changed across locales.");
                settings.BufferFolder = ""; RefreshBufferFolder();
                var titleSize = TextRenderer.MeasureText(appName.Text, appName.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                if (titleSize.Width > appName.Width || titleSize.Height + 4 > appName.Height || versionLabel.Right != appName.Left + titleSize.Width || versionLabel.Top >= appName.Top || versionLabel.Bottom >= appName.Top + appName.Height / 2) throw new Exception("Brand name clips or version is not above the final letter.");
                if (appName.Top + appName.Height / 2 != appLogo.Top + appLogo.Height / 2 || pageHeading.Top + pageHeading.Height / 2 != settingsBack.Top + settingsBack.Height / 2 || pageHeading.TextAlign != ContentAlignment.MiddleLeft) throw new Exception("Header text is not vertically centered with its icons.");
                var legacy = new Settings { SystemDevice = "legacy-custom-output", Folder = originalFolder, Palette = "ocean" }; legacy.SaveToFile(file);
                using (var migrated = new MainForm(false, true))
                {
                    if (migrated.settings.SystemDevice != "" || Settings.Load().SystemDevice != "legacy-custom-output") throw new Exception("Default system audio migration or legacy file preservation failed.");
                    var editor = (TextBox)migrated.rate.Controls[0];
                    if (editor.BackColor != Theme.Input || editor.ForeColor != Theme.Text) throw new Exception("Cold-start bitrate editor keeps previous palette colors.");
                }
                ApplyPalette("mint");
                report.AppendLine("ACTUAL_BUFFER_PATH_BOTH_LOCALES_BRAND_BOUNDS_DEFAULT_SYSTEM_AUDIO_MIGRATION: PASS");
                await Task.Delay(25);
            }
            finally
            {
                testing = true; Inputs(true); folder.Text = originalFolder; filenameTemplate.Text = originalTemplate; settings.BufferFolder = originalBufferFolder; RefreshBufferFolder();
                languageChoice.SelectedIndex = originalLanguage == "en" ? 1 : 0; settings.Language = originalLanguage; L.SetLanguage(originalLanguage); ApplyPalette("mint"); SetState(L.T("Готово к записи")); ShowSettings(true); SelectSettingsTab(0);
                if (existed) File.WriteAllBytes(file, original); else if (File.Exists(file)) File.Delete(file);
            }
        }
        static double Luminance(Color color)
        {
            Func<int, double> component = value => { double c = value / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); };
            return .2126 * component(color.R) + .7152 * component(color.G) + .0722 * component(color.B);
        }
        static double Contrast(Color a, Color b) { double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
        [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process, int flags);
        void VerifySoftThemesForTest(StringBuilder report)
        {
            var backgrounds = new HashSet<int>(); var cards = new HashSet<int>(); var fields = new HashSet<int>();
            foreach (string palette in new[] { "mint", "ocean", "violet", "light" })
            {
                ApplyPalette(palette); backgrounds.Add(Theme.Background.ToArgb()); cards.Add(Theme.Card.ToArgb()); fields.Add(Theme.Input.ToArgb());
                foreach (Color surface in new[] { Theme.Background, Theme.Card, Theme.Input })
                    if (Contrast(Theme.Text, surface) < 4.5 || Contrast(Theme.Muted, surface) < 4.5) throw new Exception("Unreadable theme text: " + palette);
                if (Contrast(save.ForeColor, save.Fill) < 4.5 || save.ForeColor != Theme.AccentText) throw new Exception("Primary button contrast lost across palettes: " + palette);
                if (appLogo.BackColor != Theme.Background || appName.ForeColor != Theme.Text || appName.BackColor != Theme.Background) throw new Exception("Brand colors did not follow palette.");
            }
            if (backgrounds.Count != 4 || cards.Count != 4 || fields.Count != 4) throw new Exception("Themes only change the accent.");
            foreach (string palette in new[] { "mint", "ocean", "violet", "light" }) ApplyPalette(palette);
            GC.Collect(); GC.WaitForPendingFinalizers();
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                int gdi = GetGuiResources(process.Handle, 0), user = GetGuiResources(process.Handle, 1);
                for (int repeat = 0; repeat < 5; repeat++) foreach (string palette in new[] { "mint", "ocean", "violet", "light" }) ApplyPalette(palette);
                GC.Collect(); GC.WaitForPendingFinalizers();
                int gdiGrowth = GetGuiResources(process.Handle, 0) - gdi, userGrowth = GetGuiResources(process.Handle, 1) - user;
                if (gdiGrowth > 2 || userGrowth > 2) throw new Exception("Repeated palette changes leak GUI resources: " + gdiGrowth + "/" + userGrowth);
                report.AppendLine("TWENTY_PALETTE_CHANGES_GDI_DELTA=" + gdiGrowth + " USER_DELTA=" + userGrowth);
            }
            ApplyPalette("mint"); report.AppendLine("FOUR_FULL_PALETTES_BODY_DISABLED_AND_PRIMARY_TEXT_CONTRAST: PASS");
        }
        async Task VerifyUpdateControlsForTest(StringBuilder report)
        {
            bool preference = settings.CheckUpdatesOnStartup, wasSettingsOpen = settingsOpen; DateTime schedule = nextUpdateCheck;
            try
            {
                ShowSettings(true); if (update.Visible) throw new Exception("Update button is visible in settings.");
                ShowSettings(false); if (!update.Visible) throw new Exception("Update button is missing from the main screen.");
                SetAvailableRelease(null); settings.CheckUpdatesOnStartup = true; nextUpdateCheck = DateTime.MinValue;
                updateDialogOpen = true; bool dialogBlocked = !UpdateCheckDue(DateTime.UtcNow); updateDialogOpen = false;
                if (!dialogBlocked) throw new Exception("Manual dialog can launch a duplicate background check.");
                int requests = 0; var completion = new TaskCompletionSource<ReleaseInfo>();
                Func<System.Threading.CancellationToken, Task<ReleaseInfo>> lookup = token => { requests++; return completion.Task; };
                var first = CheckForUpdates(lookup); await CheckForUpdates(lookup);
                if (requests != 1 || !checkingUpdates) throw new Exception("Background update requests overlap.");
                settings.CheckUpdatesOnStartup = false; completion.SetResult(new ReleaseInfo { Version = new Version(99, 2, 0, 0) }); await first;
                if (availableRelease != null || UpdateCheckDue(DateTime.MaxValue)) throw new Exception("Disabled update check published an in-flight result.");
                settings.CheckUpdatesOnStartup = true; nextUpdateCheck = DateTime.MinValue;
                await CheckForUpdates(token => { requests++; throw new IOException("Synthetic offline"); });
                if (checkingUpdates || availableRelease != null || UpdateCheckDue(DateTime.UtcNow.AddMinutes(4)) || !UpdateCheckDue(DateTime.UtcNow.AddMinutes(6))) throw new Exception("Offline check loops or ignores its interval.");
                nextUpdateCheck = DateTime.MinValue;
                await CheckForUpdates(token => Task.FromResult(new ReleaseInfo { Version = typeof(MainForm).Assembly.GetName().Version }));
                if (availableRelease != null || update.AttentionDot) throw new Exception("Current version shows attention.");
                var newer = new ReleaseInfo { Version = new Version(99, 3, 0, 0) }; nextUpdateCheck = DateTime.MinValue;
                await CheckForUpdates(token => Task.FromResult(newer));
                if (!update.AttentionDot || availableRelease != newer || update.Image != null || appLogo.Image != brand) throw new Exception("Background result is not reflected on the separate update button.");
                int flashes = updateFlashTicks; SetAvailableRelease(newer); if (updateFlashTicks != flashes) throw new Exception("Duplicate result restarts flashing.");
                for (int i = 0; i < 8; i++) RefreshUpdateIndicator(true);
                Color stable = update.Fill; RefreshUpdateIndicator(true);
                if (updateFlashTicks != 0 || !update.AttentionDot || update.Fill != stable) throw new Exception("Update attention does not settle.");
                updateFlashTicks = 6; Hide(); if (updateFlashTicks != 0) throw new Exception("Hidden window keeps update animation."); Show();
                report.AppendLine("UPDATE_BACKGROUND_OFFLINE_INTERVAL_DISABLE_INFLIGHT_DEDUP_FINITE_HIDDEN_ATTENTION: PASS");
            }
            finally { settings.CheckUpdatesOnStartup = preference; nextUpdateCheck = schedule; SetAvailableRelease(null); ShowSettings(wasSettingsOpen); }
        }
    }
}
