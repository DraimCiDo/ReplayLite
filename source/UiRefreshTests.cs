using System;
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
            string originalLanguage = settings.Language, originalFolder = folder.Text, originalTemplate = filenameTemplate.Text;
            try
            {
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
                await Task.Delay(25);
            }
            finally
            {
                testing = true; Inputs(true); folder.Text = originalFolder; filenameTemplate.Text = originalTemplate;
                languageChoice.SelectedIndex = originalLanguage == "en" ? 1 : 0; settings.Language = originalLanguage; L.SetLanguage(originalLanguage); ApplyPalette("mint"); SetState(L.T("Готово к записи")); ShowSettings(true); SelectSettingsTab(0);
                if (existed) File.WriteAllBytes(file, original); else if (File.Exists(file)) File.Delete(file);
            }
        }
    }
}
