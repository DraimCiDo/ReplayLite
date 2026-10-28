using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReplayLite
{
    public sealed partial class MainForm
    {
        readonly ActionButton advancedButton = new ActionButton();
        ActionButton bufferBrowse, advancedReset;
        SettingsScrollPanel advancedScroll; Panel[] advancedGroups;
        SelectBox systemDevice, microphoneDevice;
        RangeBox systemVolume, microphoneVolume, freeSpace;
        ToggleSwitch cursorCapture, stopLowSpace, animations, saveNotice, saveSound;
        PathDisplay bufferFolder; Label advancedNote;
        CardPanel basicOptions, advancedOptions; bool advancedSync, advancedVisible; int advancedPreviewGroup;
        bool IAnimationPreference.AnimationsEnabled { get { return settings.Animations; } }

        void RegisterAdvancedSettings(CardPanel options)
        {
            basicOptions = options;
            StyleButton(advancedButton, "Расширенные", this, 556, 88, 184, 32, false);
            advancedButton.Font = new Font("Segoe UI", 9);
            advancedButton.Click += delegate { ShowAdvanced(true); };
        }
        void BuildAdvancedSettings()
        {
            // No advanced controls, endpoints or timers are created during normal startup.
            bufferBrowse = new ActionButton();
            advancedReset = new ActionButton();
            advancedGroups = new[] { new Panel(), new Panel(), new Panel(), new Panel() };
            systemDevice = new SelectBox(); microphoneDevice = new SelectBox();
            systemVolume = new RangeBox(); microphoneVolume = new RangeBox(); freeSpace = new RangeBox();
            cursorCapture = new ToggleSwitch(); stopLowSpace = new ToggleSwitch(); animations = new ToggleSwitch(); saveNotice = new ToggleSwitch(); saveSound = new ToggleSwitch();
            bufferFolder = new PathDisplay(); advancedNote = new Label();
            advancedOptions = Card(settingsPage, 20, 0, 720, 325);
            advancedScroll = new SettingsScrollPanel { ContentHeight = 456 };
            advancedScroll.SetBounds(16, 16, 688, 260); advancedOptions.Controls.Add(advancedScroll);
            string[] names = { "Захват", "Аудиоустройства и громкость", "Временный буфер и диск", "Интерфейс и уведомления" };
            int[] tops = { 0, 62, 212, 362 }, heights = { 52, 140, 140, 92 };
            for (int i = 0; i < names.Length; i++)
            {
                advancedGroups[i].SetBounds(0, tops[i], 658, heights[i]); advancedGroups[i].BackColor = Theme.Card; advancedScroll.Content.Controls.Add(advancedGroups[i]);
                LabelAt(advancedGroups[i], names[i], 0, 0, 658, 20, 10, true, Theme.Text);
            }
            var capture = advancedGroups[0];
            AdvancedToggle(capture, cursorCapture, "Записывать курсор", 0, 22);

            var audioPage = advancedGroups[1];
            AdvancedField(audioPage, "Устройство звука компьютера", systemDevice, 0, 24);
            AdvancedField(audioPage, "Микрофон", microphoneDevice, 338, 24);
            AdvancedField(audioPage, "Громкость компьютера · %", systemVolume, 0, 82);
            AdvancedField(audioPage, "Громкость микрофона · %", microphoneVolume, 338, 82);

            var storage = advancedGroups[2];
            LabelAt(storage, "Папка временного буфера", 0, 24, 658, 18, 9, false, Theme.Muted);
            bufferFolder.AccessibleName = "Папка временного буфера"; bufferFolder.SetBounds(0, 44, 536, 34); storage.Controls.Add(bufferFolder);
            StyleButton(bufferBrowse, "Изменить", storage, 548, 44, 110, 34, false); bufferBrowse.Font = new Font("Segoe UI", 9);
            bufferBrowse.Click += delegate { using (var dialog = new FolderBrowserDialog { SelectedPath = settings.BufferHome, Description = "Папка временных сегментов буфера" }) if (dialog.ShowDialog(this) == DialogResult.OK) { settings.BufferFolder = dialog.SelectedPath; RefreshBufferFolder(); AdvancedChanged(this, EventArgs.Empty); } };
            AdvancedField(storage, "Минимум свободного места · МБ", freeSpace, 0, 82); freeSpace.Minimum = 128; freeSpace.Maximum = 10240;
            AdvancedToggle(storage, stopLowSpace, "Остановка при нехватке места", 338, 100);

            var general = advancedGroups[3];
            AdvancedToggle(general, animations, "Анимации интерфейса", 0, 24);
            AdvancedToggle(general, saveNotice, "Уведомление о сохранении", 338, 24);
            AdvancedToggle(general, saveSound, "Звук при сохранении", 0, 60);
            advancedNote.SetBounds(16, 288, 520, 25); advancedNote.Font = new Font("Segoe UI", 8); advancedNote.ForeColor = Theme.Muted; advancedNote.BackColor = Theme.Card; advancedOptions.Controls.Add(advancedNote);
            StyleButton(advancedReset, "Сброс", advancedOptions, 548, 282, 156, 32, false); advancedReset.Font = new Font("Segoe UI", 9); advancedReset.Click += delegate { ResetAdvancedSettings(); };
            advancedSync = true;
            systemVolume.Value = settings.SystemVolume; microphoneVolume.Value = settings.MicrophoneVolume;
            freeSpace.Value = settings.FreeSpaceMb; stopLowSpace.Checked = settings.StopWhenLowSpace;
            cursorCapture.Checked = settings.CaptureCursor; animations.Checked = settings.Animations; saveNotice.Checked = settings.SaveNotification; saveSound.Checked = settings.SaveSound;
            RefreshAudioChoices(); RefreshBufferFolder(); advancedSync = false;
            foreach (var select in new[] { systemDevice, microphoneDevice }) select.SelectedIndexChanged += AdvancedChanged;
            foreach (var range in new[] { systemVolume, microphoneVolume, freeSpace }) range.ValueCommitted += AdvancedChanged;
            foreach (var toggle in new[] { cursorCapture, stopLowSpace, animations, saveNotice, saveSound }) toggle.CheckedChanged += AdvancedChanged;
            advancedScroll.AttachWheelHandlers(); AdvancedInputs(recorder == null && !busy); advancedOptions.Visible = false;
        }
        void AdvancedField(Control parent, string text, Control input, int x, int y)
        { LabelAt(parent, text, x, y, 318, 18, 9, false, Theme.Muted); AddInput(parent, input, x, y + 20, 318, text); input.Height = input is RangeBox ? 36 : 30; }
        void AdvancedToggle(Control parent, ToggleSwitch input, string text, int x, int y)
        { input.Text = input.AccessibleName = text; input.Font = new Font("Segoe UI", 9); input.SetBounds(x, y, 318, 28); parent.Controls.Add(input); }
        void RefreshBufferFolder() { bufferFolder.Text = string.IsNullOrEmpty(settings.BufferFolder) ? "По умолчанию" : settings.BufferFolder; bufferFolder.AccessibleDescription = settings.BufferHome; tips.SetToolTip(bufferFolder, settings.BufferHome); }
        internal void PreviewAdvancedForDocs(int group)
        { testing = true; advancedPreviewGroup = Math.Max(0, Math.Min(3, group)); ShowSettings(true); ShowAdvanced(true); }
        internal void PositionAdvancedPreview() { ScrollAdvancedGroup(advancedPreviewGroup); }
        void RefreshAudioChoices()
        {
            bool previous = advancedSync; advancedSync = true;
            try { FillAudioChoices(systemDevice, true, settings.SystemDevice); FillAudioChoices(microphoneDevice, false, settings.MicrophoneDevice); }
            finally { advancedSync = previous; }
        }
        static void FillAudioChoices(SelectBox select, bool system, string id)
        { select.ClosePopup(); select.Items.Clear(); foreach (var choice in AudioChoice.Enumerate(system, id)) select.Items.Add(choice); select.SelectedIndex = select.Items.FindIndex(item => ((AudioChoice)item).Id == id); if (select.SelectedIndex < 0) select.SelectedIndex = 0; }
        void ShowAdvanced(bool show)
        {
            bool created = advancedOptions == null && show;
            if (created) BuildAdvancedSettings();
            hotkey.CancelCapture(); CloseSettingsPopups(); advancedVisible = show; basicOptions.Visible = !show;
            if (advancedOptions != null) advancedOptions.Visible = show;
            if (show) { if (!created) RefreshAudioChoices(); AdvancedInputs(recorder == null && !busy); advancedScroll.ScrollTo(0); advancedOptions.BringToFront(); }
            UpdateNavigation();
        }
        void CloseSettingsPopups()
        {
            foreach (var select in new[] { duration, fps, sound, display }) select.ClosePopup();
            if (advancedOptions != null) { systemDevice.ClosePopup(); microphoneDevice.ClosePopup(); }
        }
        void ScrollAdvancedGroup(int index)
        { CloseSettingsPopups(); advancedScroll.Reveal(advancedGroups[index]); }
        void AdvancedInputs(bool enabled)
        {
            if (advancedOptions == null) return;
            for (int i = 0; i < 3; i++) advancedGroups[i].Enabled = enabled;
            advancedReset.Enabled = enabled;
            freeSpace.Enabled = stopLowSpace.Checked;
            advancedNote.Text = enabled ? "" : "Для изменения параметров захвата остановите запись."; advancedNote.ForeColor = Theme.Muted;
        }
        void AdvancedChanged(object sender, EventArgs e)
        {
            if (advancedSync || released || closing) return;
            bool captureSetting = sender != animations && sender != saveNotice && sender != saveSound;
            if (captureSetting && (recorder != null || busy)) return;
            if (captureSetting)
            {
                settings.SystemDevice = ((AudioChoice)systemDevice.Items[systemDevice.SelectedIndex]).Id;
                settings.MicrophoneDevice = ((AudioChoice)microphoneDevice.Items[microphoneDevice.SelectedIndex]).Id;
                settings.SystemVolume = systemVolume.Value; settings.MicrophoneVolume = microphoneVolume.Value;
                settings.FreeSpaceMb = freeSpace.Value; settings.StopWhenLowSpace = stopLowSpace.Checked; settings.CaptureCursor = cursorCapture.Checked;
                freeSpace.Enabled = stopLowSpace.Checked;
            }
            settings.Animations = animations.Checked; settings.SaveNotification = saveNotice.Checked; settings.SaveSound = saveSound.Checked;
            if (!settings.Animations) StopAnimations(); if (testing) return;
            try { if (recorder == null && !busy) ReadCaptureOptions(); settings.Save(); advancedNote.ForeColor = Theme.Muted; advancedNote.Text = "Изменения сохранены"; }
            catch (Exception error) { advancedNote.ForeColor = Theme.Red; advancedNote.Text = "Не удалось сохранить настройки"; tips.SetToolTip(advancedNote, error.Message); }
        }
        void ResetAdvancedSettings()
        {
            if (recorder != null || busy) return;
            advancedSync = true;
            try
            {
                cursorCapture.Checked = true; systemDevice.SelectedIndex = microphoneDevice.SelectedIndex = 0; systemVolume.Value = microphoneVolume.Value = 100;
                settings.BufferFolder = ""; RefreshBufferFolder(); freeSpace.Value = 128; stopLowSpace.Checked = true;
                animations.Checked = saveNotice.Checked = true; saveSound.Checked = false;
            }
            finally { advancedSync = false; }
            AdvancedChanged(advancedReset, EventArgs.Empty);
        }
        IEnumerable<ActionButton> AllActionButtons(Control parent)
        {
            foreach (Control child in parent.Controls) { var button = child as ActionButton; if (button != null) yield return button; foreach (var nested in AllActionButtons(child)) yield return nested; }
        }
        async Task VerifyAdvancedSettingsForTest(StringBuilder report)
        {
            Size size = ClientSize;
            for (int repeat = 0; repeat < 3; repeat++)
            {
                ShowSettings(false); settingsGear.PerformClick();
                if (!settingsPage.Visible || advancedVisible || settingsGear.Visible || !settingsBack.Visible) throw new Exception("Gear navigation failed.");
                hotkey.BeginCapture(); advancedButton.PerformClick();
                if (!advancedVisible || basicOptions.Visible || !advancedOptions.Visible || advancedButton.Visible || hotkey.IsCapturing) throw new Exception("Advanced navigation or hook cleanup failed.");
                for (int group = 0; group < 4; group++) { ScrollAdvancedGroup(group); VerifyControlBoundsForTest(settingsPage); }
                settingsBack.PerformClick(); if (advancedVisible || !basicOptions.Visible) throw new Exception("Back did not return to basic settings.");
                settingsBack.PerformClick(); if (settingsPage.Visible || !recordingPage.Visible) throw new Exception("Back did not return to recording.");
            }
            if (ClientSize != size) throw new Exception("Settings navigation resized window.");
            ShowSettings(true); ShowAdvanced(true); Inputs(false);
            advancedScroll.ScrollTo(int.MaxValue); if (advancedScroll.ScrollOffset != advancedScroll.ContentHeight - advancedScroll.ClientSize.Height) throw new Exception("Custom scroll maximum incorrect.");
            advancedScroll.ScrollTo(-100); if (advancedScroll.ScrollOffset != 0) throw new Exception("Custom scroll minimum incorrect.");
            int volumeBeforeWheel = systemVolume.Value;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var wheel = new HandledMouseEventArgs(MouseButtons.None, 0, 5, 5, -120);
            typeof(Control).GetMethod("OnMouseWheel", flags).Invoke(systemVolume, new object[] { wheel });
            if (advancedScroll.ScrollOffset != 48 || !wheel.Handled || systemVolume.Value != volumeBeforeWheel) throw new Exception("Wheel did not scroll or changed volume.");
            var thumb = (Control)typeof(SettingsScrollPanel).GetField("thumb", flags).GetValue(advancedScroll);
            typeof(SettingsScrollThumb).GetField("dragOffset", flags).SetValue(thumb, 0);
            typeof(SettingsScrollThumb).GetMethod("DragTo", flags).Invoke(thumb, new object[] { thumb.Height });
            int maximumScroll = advancedScroll.ContentHeight - advancedScroll.Height;
            if (advancedScroll.ScrollOffset != maximumScroll) throw new Exception("Scrollbar drag did not reach bottom.");
            typeof(SettingsScrollThumb).GetMethod("OnKeyDown", flags).Invoke(thumb, new object[] { new KeyEventArgs(Keys.Home) });
            if (advancedScroll.ScrollOffset != 0) throw new Exception("Scrollbar Home failed.");
            typeof(SettingsScrollThumb).GetMethod("OnKeyDown", flags).Invoke(thumb, new object[] { new KeyEventArgs(Keys.End) });
            if (advancedScroll.ScrollOffset != maximumScroll) throw new Exception("Scrollbar End failed.");
            if (bufferFolder.Height != 34 || bufferFolder.Top != bufferBrowse.Top || bufferFolder.Height != bufferBrowse.Height || (string.IsNullOrEmpty(settings.BufferFolder) && bufferFolder.Text != "По умолчанию")) throw new Exception("Buffer path display or button alignment failed.");
            for (int i = 0; i < 3; i++) if (advancedGroups[i].Enabled) throw new Exception("Capture settings enabled during recording.");
            if (!advancedGroups[3].Enabled || advancedReset.Enabled) throw new Exception("General settings or reset recording policy failed.");
            Inputs(true); bool previousAnimation = settings.Animations; animations.Checked = false; start.PreviewHover(true);
            if (ActiveAnimations != 0) throw new Exception("Disabled animations still running."); animations.Checked = previousAnimation;
            int previousVolume = systemVolume.Value; systemVolume.EditForTest(50); if (settings.SystemVolume != 50) throw new Exception("Volume slider did not update setting."); systemVolume.Value = previousVolume; settings.SystemVolume = previousVolume;
            report.AppendLine("GEAR_CURVED_BACK_SCROLL_GROUPS_AND_RECORDING_LOCK: PASS");
            report.AppendLine("CUSTOM_GREEN_SCROLLBAR_BOUNDS_AND_COMPACT_LOGO_UPDATE: PASS");
            report.AppendLine("ANIMATIONS_DISABLED_NO_TIMERS: PASS");
            AdvancedSettingsTests.Run(report); await AdvancedSettingsTests.VerifyBehaviorAsync(report);
            string settingsFile = Path.Combine(Settings.Home, "settings.txt"); bool existed = File.Exists(settingsFile); byte[] previousFile = existed ? File.ReadAllBytes(settingsFile) : null;
            bool oldNotice = saveNotice.Checked, oldSound = saveSound.Checked, oldAnimations = animations.Checked;
            int oldSystemVolume = systemVolume.Value, oldMicrophoneVolume = microphoneVolume.Value;
            string oldSystemDevice = settings.SystemDevice, oldMicrophoneDevice = settings.MicrophoneDevice;
            string oldBufferFolder = settings.BufferFolder; int oldFreeSpace = freeSpace.Value; bool oldStop = stopLowSpace.Checked, oldCursor = cursorCapture.Checked;
            try
            {
                testing = false; saveNotice.Checked = !oldNotice; saveSound.Checked = !oldSound; systemVolume.EditForTest(63); microphoneVolume.EditForTest(27);
                var loaded = Settings.Load();
                if (loaded.SaveNotification == oldNotice || loaded.SaveSound == oldSound || loaded.SystemVolume != 63 || loaded.MicrophoneVolume != 27) throw new Exception("Advanced UI changes did not persist.");
                recorder = new Recorder(settings); Inputs(false); animations.Checked = !oldAnimations;
                if (settings.Animations == oldAnimations || advancedGroups[0].Enabled || !advancedGroups[3].Enabled || Settings.Load().Animations != settings.Animations) throw new Exception("General setting not saved during capture.");
                testing = true; recorder.Dispose(); recorder = null; Inputs(true); int oldRate = rate.Value, oldFps = fps.SelectedIndex; ResetAdvancedSettings();
                if (systemVolume.Value != 100 || microphoneVolume.Value != 100 || settings.SystemDevice != "" || settings.MicrophoneDevice != "" || settings.BufferFolder != "" || freeSpace.Value != 128 || !cursorCapture.Checked || !stopLowSpace.Checked || !animations.Checked || !saveNotice.Checked || saveSound.Checked || rate.Value != oldRate || fps.SelectedIndex != oldFps) throw new Exception("Advanced reset changed basic settings or did not reset additional parameters.");
            }
            finally
            {
                testing = true; if (recorder != null) { recorder.Dispose(); recorder = null; } Inputs(true);
                saveNotice.Checked = oldNotice; saveSound.Checked = oldSound; animations.Checked = oldAnimations; systemVolume.Value = oldSystemVolume; microphoneVolume.Value = oldMicrophoneVolume;
                settings.SystemDevice = oldSystemDevice; settings.MicrophoneDevice = oldMicrophoneDevice; RefreshAudioChoices(); AdvancedChanged(systemVolume, EventArgs.Empty);
                settings.BufferFolder = oldBufferFolder; RefreshBufferFolder(); freeSpace.Value = oldFreeSpace; stopLowSpace.Checked = oldStop; cursorCapture.Checked = oldCursor; AdvancedChanged(freeSpace, EventArgs.Empty);
                if (existed) File.WriteAllBytes(settingsFile, previousFile); else if (File.Exists(settingsFile)) File.Delete(settingsFile);
                ShowAdvanced(false); ShowSettings(true);
            }
            report.AppendLine("ADVANCED_UI_AUTOSAVE_SINGLE_RESET_PRESERVES_BASIC_AND_LIVE_GENERAL: PASS");
        }
    }
}
