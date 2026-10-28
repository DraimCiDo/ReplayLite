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
        ActionButton bufferBrowse, advancedReset;
        readonly List<ActionButton> settingsTabs = new List<ActionButton>();
        SettingsScrollPanel advancedScroll; Panel[] advancedGroups;
        SelectBox systemDevice, microphoneDevice, containerChoice, codecChoice, resolutionChoice, controlChoice, themeChoice, toneChoice;
        RangeBox systemVolume, microphoneVolume, freeSpace, feedbackVolume;
        ToggleSwitch cursorCapture, stopLowSpace, animations, saveNotice, saveSound, systemEnabled, microphoneEnabled, checkUpdates, closeTray, minimized;
        PathDisplay bufferFolder, outputFolder; TextField filenameTemplate; Label filenamePreview, scalingWarning, advancedNote;
        CardPanel advancedOptions; bool advancedSync; int advancedPreviewGroup, settingsTab;
        bool IAnimationPreference.AnimationsEnabled { get { return settings.Animations; } }
        readonly string[] settingsTitles = { "Экран и буфер", "Сохранение клипов", "Видео", "Звук", "Клавиши и отклик", "Приложение и Windows" };
        void InitializePreferenceInputs()
        {
            RefreshMonitors();
            duration.Items.AddRange(new object[] { "30 секунд", "2 минуты", "5 минут", "10 минут" }); duration.SelectedIndex = settings.Seconds == 30 ? 0 : settings.Seconds == 300 ? 2 : settings.Seconds == 600 ? 3 : 1;
            sound.Items.AddRange(new object[] { "Компьютер и микрофон", "Только звук компьютера", "Без звука", "Только микрофон" }); sound.SelectedIndex = settings.Audio == "none" ? 2 : settings.Audio == "system" ? 1 : settings.Audio == "microphone" ? 3 : 0;
            fps.Items.AddRange(new object[] { "30 кадров/с · экономнее", "60 кадров/с · плавнее" }); fps.SelectedIndex = settings.Fps == 60 ? 1 : 0;
            rate.Minimum = 2; rate.Maximum = 30; rate.Value = settings.Mbps; folder.Text = settings.Folder;
            foreach (var input in new Control[] { duration, fps, sound, display, rate, hotkey, startup }) { input.BackColor = Theme.Card; input.ForeColor = Theme.Text; }
            badge.BackColor = Theme.Background; progress.BackColor = Theme.Card;
            hotkey.Binding = settings.Hotkey; hotkeyInfo.Font = startupInfo.Font = new Font("Segoe UI", 8); hotkeyInfo.ForeColor = startupInfo.ForeColor = Theme.Muted; hotkeyInfo.BackColor = startupInfo.BackColor = Theme.Card;
            hotkey.CaptureStarted += delegate { if (hotkeyRegistered) Native.UnregisterHotKey(Handle, 1); hotkeyRegistered = false; hotkeyInfo.Text = "Нажмите сочетание и отпустите клавиши.\nДля отмены щёлкните вне поля."; };
            hotkey.CaptureCancelled += delegate { if (!released) RegisterConfiguredHotkey(); };
            hotkey.CaptureFailed += delegate { hotkeyInfo.ForeColor = Theme.Red; hotkeyInfo.Text = "Не удалось включить ввод; прежняя клавиша сохранена."; };
            hotkey.BindingProposed += delegate(object sender, BindingEventArgs e) { e.Accepted = TryChangeHotkey(e.Binding); };
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog { SelectedPath = folder.Text, Description = "Папка для сохранённых клипов" }) if (dialog.ShowDialog(this) == DialogResult.OK) { folder.Text = dialog.SelectedPath; CaptureOptionsChanged(this, EventArgs.Empty); } };
            if (!testing) try { startup.Checked = StartupRegistration.Enabled(Application.ExecutablePath); } catch (Exception error) { startup.Enabled = false; startupInfo.Text = "Не удалось прочитать автозапуск"; tips.SetToolTip(startupInfo, error.Message); }
            startup.CheckedChanged += delegate
            {
                if (testing || startupChanging) return;
                try { StartupRegistration.SetEnabled(Application.ExecutablePath, startup.Checked); startupInfo.Text = ""; }
                catch (Exception error) { startupChanging = true; startup.Checked = !startup.Checked; startupChanging = false; startupInfo.Text = "Не удалось изменить автозапуск"; tips.SetToolTip(startupInfo, error.Message); }
            };
            foreach (var select in new[] { duration, fps, sound, display }) select.SelectedIndexChanged += CaptureOptionsChanged;
            rate.ValueChanged += CaptureOptionsChanged;
            tips.SetToolTip(settingsGear, "Настройки"); tips.SetToolTip(settingsBack, "Вернуться на главную"); tips.SetToolTip(update, "Проверить обновления · нажмите на логотип");
        }
        void BuildAdvancedSettings()
        {
            advancedSync = true;
            advancedOptions = Card(settingsPage, 20, 0, 720, 325);
            advancedScroll = new SettingsScrollPanel { ContentHeight = 254 }; advancedScroll.SetBounds(16, 14, 688, 256); advancedOptions.Controls.Add(advancedScroll);
            advancedGroups = new Panel[6];
            for (int i = 0; i < 6; i++)
            {
                advancedGroups[i] = new Panel { BackColor = Theme.Card }; advancedGroups[i].SetBounds(0, 0, 658, i == 0 ? 254 : 242); advancedScroll.Content.Controls.Add(advancedGroups[i]);
                int tab = i; var button = new ActionButton(); StyleButton(button, "", this, 456 + 48 * i, 87, 44, 36, false);
                button.Glyph = (NavigationGlyph)((int)NavigationGlyph.Monitor + i); button.AccessibleName = settingsTitles[i]; tips.SetToolTip(button, settingsTitles[i]); button.Click += delegate { SelectSettingsTab(tab); }; settingsTabs.Add(button);
            }
            var capture = advancedGroups[0];
            AdvancedField(capture, "Монитор", display, 0, 0, "Записывается выбранный экран. Для NVENC нужен монитор на NVIDIA.");
            var refresh = new ActionButton(); StyleButton(refresh, "Обновить мониторы", capture, 344, 20, 314, 32, false); refresh.Font = new Font("Segoe UI", 9); refresh.Click += delegate { RefreshMonitors(); };
            AdvancedField(capture, "Длительность буфера", duration, 0, 62, "Хранятся последние секунды записи. Сохранить можно и неполный буфер.");
            cursorCapture = new ToggleSwitch(); AdvancedToggle(capture, cursorCapture, "Записывать курсор", 344, 82, "Включает курсор в видеозаписи.");
            bufferFolder = new PathDisplay(); bufferBrowse = new ActionButton();
            HelpLabel(capture, "Папка временного буфера", 0, 126, "Сжатые сегменты хранятся на диске. Не выбирайте медленный или сетевой диск.");
            bufferFolder.SetBounds(0, 146, 536, 34); capture.Controls.Add(bufferFolder); StyleButton(bufferBrowse, "Выбрать", capture, 548, 146, 110, 34, false);
            bufferBrowse.Click += delegate { using (var dialog = new FolderBrowserDialog { SelectedPath = settings.BufferHome, Description = "Папка временного буфера" }) if (dialog.ShowDialog(this) == DialogResult.OK) { settings.BufferFolder = dialog.SelectedPath; RefreshBufferFolder(); AdvancedChanged(bufferBrowse, EventArgs.Empty); } };
            freeSpace = new RangeBox { Minimum = 128, Maximum = 10240 }; stopLowSpace = new ToggleSwitch();
            AdvancedField(capture, "Резерв диска · МБ", freeSpace, 0, 196, "Запись останавливается при достижении резерва. Минимальный аварийный запас всегда 128 МБ.");
            AdvancedToggle(capture, stopLowSpace, "Контролировать резерв", 344, 216, "Остановка буфера при недостатке свободного места.");

            var output = advancedGroups[1]; outputFolder = new PathDisplay();
            HelpLabel(output, "Папка клипов", 0, 0, "Здесь появляются только успешно сохранённые клипы."); outputFolder.SetBounds(0, 20, 536, 34); output.Controls.Add(outputFolder);
            StyleButton(browse, "Выбрать", output, 548, 20, 110, 34, false); folder.TextChanged += delegate { outputFolder.Text = folder.Text; tips.SetToolTip(outputFolder, folder.Text); }; outputFolder.Text = folder.Text; tips.SetToolTip(outputFolder, folder.Text);
            containerChoice = Choice(new[] { "MP4", "MKV" }, settings.Container == "mkv" ? 1 : 0); AdvancedField(output, "Формат файла", containerChoice, 0, 70, "Контейнер меняется без перекодирования. MP4 удобен для публикации, MKV — для монтажа.");
            var presets = Choice(new[] { "Дата и время", "Монитор, дата и время", "Своё имя" }, 2); AdvancedField(output, "Шаблон", presets, 344, 70, "Выберите готовый шаблон или отредактируйте имя ниже.");
            filenameTemplate = new TextField { Text = settings.FilenameTemplate, BackColor = Theme.Card, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10), MaxLength = 100 };
            HelpLabel(output, "Имя клипа", 0, 136, "Токены: {date}, {time}, {monitor}, {YYYY-MM-DD_HH-MM-SS}. Расширение добавляется автоматически; совпавшие имена получают номер."); AddInput(output, filenameTemplate, 0, 158, 658, "Шаблон имени клипа"); filenameTemplate.Height = 34;
            filenamePreview = LabelAt(output, "", 0, 198, 658, 28, 9, false, Theme.Muted);
            presets.SelectedIndexChanged += delegate { if (presets.SelectedIndex < 2) { filenameTemplate.Text = presets.SelectedIndex == 0 ? "Replay-{date}_{time}" : "Replay-{monitor}-{date}_{time}"; SaveTemplate(); } };
            filenameTemplate.TextChanged += delegate { PreviewFilename(); }; filenameTemplate.Leave += delegate { SaveTemplate(); }; filenameTemplate.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { SaveTemplate(); e.SuppressKeyPress = true; } };

            var video = advancedGroups[2];
            AdvancedField(video, "Частота кадров", fps, 0, 0, "30 FPS экономнее, 60 FPS плавнее. Частота не превышает возможности источника.");
            resolutionChoice = Choice(new[] { "Исходное · GPU", "До 1920 × 1080 · CPU", "До 1280 × 720 · CPU" }, settings.Resolution == "720" ? 2 : settings.Resolution == "1080" ? 1 : 0);
            AdvancedField(video, "Разрешение", resolutionChoice, 344, 0, "Пропорции сохраняются, увеличение отключено. Масштабирование копирует кадры в RAM и нагружает CPU; исходное разрешение этого не делает.");
            codecChoice = Choice(new[] { "H.264 · NVENC", "HEVC · NVENC", "AV1 · NVENC" }, settings.Codec == "av1" ? 2 : settings.Codec == "hevc" ? 1 : 0); AdvancedField(video, "Кодек", codecChoice, 0, 66, "Только аппаратный NVENC. HEVC/AV1 требуют совместимую NVIDIA; при отсутствии поддержки запись сообщает ошибку без перехода на CPU.");
            AdvancedField(video, "Битрейт · Мбит/с", rate, 344, 66, "Целевой битрейт. Большие значения увеличивают качество и размер буфера.");
            controlChoice = Choice(new[] { "CBR · постоянный", "VBR · переменный" }, settings.RateControl == "vbr" ? 1 : 0); AdvancedField(video, "Управление битрейтом", controlChoice, 0, 132, "CBR стремится к заданному битрейту; VBR экономит место на простых сценах, верхняя граница — выбранный битрейт.");
            scalingWarning = LabelAt(video, "", 344, 152, 314, 66, 9, false, Theme.Red);

            var audio = advancedGroups[3]; systemEnabled = new ToggleSwitch(); microphoneEnabled = new ToggleSwitch();
            AdvancedToggle(audio, systemEnabled, "Звук компьютера", 0, 0, "Записывать звук выбранного устройства вывода."); AdvancedToggle(audio, microphoneEnabled, "Микрофон", 344, 0, "Записывать выбранный микрофон. Можно включить без звука компьютера.");
            systemDevice = new SelectBox(); microphoneDevice = new SelectBox(); systemVolume = new RangeBox(); microphoneVolume = new RangeBox();
            AdvancedField(audio, "Устройство вывода", systemDevice, 0, 44, "По умолчанию следует за системным устройством. Отсутствующее устройство заменяется тишиной до возвращения."); AdvancedField(audio, "Устройство микрофона", microphoneDevice, 344, 44, "Список обновляется при открытии вкладки. Недоступное выбранное устройство не заменяется другим микрофоном.");
            AdvancedField(audio, "Громкость компьютера · %", systemVolume, 0, 112, "Громкость в записи; системная громкость Windows не меняется."); AdvancedField(audio, "Громкость микрофона · %", microphoneVolume, 344, 112, "Громкость в записи; 0% — тишина.");

            var feedback = advancedGroups[4]; AdvancedField(feedback, "Сохранить повтор", hotkey, 0, 0, "Нажмите поле, затем сочетание. Клавиши работают глобально; занятые сочетания не сохраняются.");
            hotkeyInfo.SetBounds(0, 60, 318, 48); feedback.Controls.Add(hotkeyInfo);
            toneChoice = Choice(new[] { "Короткий сигнал", "Две ноты", "Мягкий сигнал" }, settings.FeedbackTone); AdvancedField(feedback, "Звук сохранения", toneChoice, 344, 0, "Короткий сигнал воспроизводится только после успешного сохранения.");
            feedbackVolume = new RangeBox(); AdvancedField(feedback, "Громкость сигнала · %", feedbackVolume, 344, 68, "Отдельная громкость сигнала. Громкость Windows не меняется.");
            saveNotice = new ToggleSwitch(); saveSound = new ToggleSwitch(); AdvancedToggle(feedback, saveNotice, "Уведомление о сохранении", 0, 128, "Показывает системное уведомление после сохранения."); AdvancedToggle(feedback, saveSound, "Звук после сохранения", 344, 128, "Включает выбранный сигнал после успешного экспорта.");
            var listen = new ActionButton(); StyleButton(listen, "Прослушать", feedback, 344, 174, 150, 32, false); listen.Font = new Font("Segoe UI", 9); listen.Click += async delegate { listen.Enabled = false; try { await SaveFeedback.PlayAsync(toneChoice.SelectedIndex, feedbackVolume.Value); } finally { if (!listen.IsDisposed) listen.Enabled = true; } };

            var system = advancedGroups[5]; checkUpdates = new ToggleSwitch(); closeTray = new ToggleSwitch(); minimized = new ToggleSwitch(); animations = new ToggleSwitch();
            AdvancedToggle(system, startup, "Запускать с Windows", 0, 0, "Автозапуск в трее с включённым буфером. Применяется сразу.");
            AdvancedToggle(system, checkUpdates, "Проверять обновления при запуске", 344, 0, "Если доступна новая версия, появится уведомление и отметка на логотипе. Если обновления нет — ничего не показывается.");
            AdvancedToggle(system, closeTray, "Закрывать в трей", 0, 48, "Выключено: крестик завершает запись и приложение. Выход из меню трея всегда закрывает приложение."); AdvancedToggle(system, minimized, "Запускать свёрнутым", 344, 48, "Следующий обычный запуск откроет приложение в трее без автоматического включения буфера.");
            AdvancedToggle(system, animations, "Анимации интерфейса", 0, 96, "Короткие переходы кнопок и индикаторов. В скрытом окне анимации останавливаются.");
            themeChoice = Choice(new[] { "Мята · тёмная", "Океан · тёмная", "Лаванда · тёмная", "Светлая" }, Array.IndexOf(new[] { "mint", "ocean", "violet", "light" }, settings.Palette)); AdvancedField(system, "Тема приложения", themeChoice, 0, 146, "Готовая палитра применяется сразу ко всему окну и сохраняется.");
            startupInfo.SetBounds(344, 166, 314, 46); system.Controls.Add(startupInfo);

            advancedNote = settingsNote; advancedNote.BackColor = Theme.Card; advancedNote.ForeColor = Theme.Muted; advancedNote.Font = new Font("Segoe UI", 8); advancedNote.SetBounds(16, 282, 510, 28); advancedOptions.Controls.Add(advancedNote);
            advancedReset = new ActionButton(); StyleButton(advancedReset, "Сброс", advancedOptions, 572, 280, 132, 32, false); advancedReset.Font = new Font("Segoe UI", 9); advancedReset.Click += delegate { ResetAdvancedSettings(); };
            tips.SetToolTip(advancedReset, "Сбросить запись, звук, отклик, поведение окна и тему. Монитор, папка клипов, горячая клавиша и регистрация автозапуска сохраняются.");
            systemVolume.Value = settings.SystemVolume; microphoneVolume.Value = settings.MicrophoneVolume; freeSpace.Value = settings.FreeSpaceMb; feedbackVolume.Value = settings.FeedbackVolume;
            cursorCapture.Checked = settings.CaptureCursor; stopLowSpace.Checked = settings.StopWhenLowSpace; animations.Checked = settings.Animations; saveNotice.Checked = settings.SaveNotification; saveSound.Checked = settings.SaveSound;
            systemEnabled.Checked = settings.Audio == "both" || settings.Audio == "system"; microphoneEnabled.Checked = settings.Audio == "both" || settings.Audio == "microphone";
            checkUpdates.Checked = settings.CheckUpdatesOnStartup; closeTray.Checked = settings.CloseToTray; minimized.Checked = settings.StartMinimized;
            RefreshAudioChoices(); RefreshBufferFolder(); PreviewFilename(); UpdateScalingWarning();
            foreach (var select in new[] { systemDevice, microphoneDevice, containerChoice, codecChoice, resolutionChoice, controlChoice, themeChoice, toneChoice }) select.SelectedIndexChanged += AdvancedChanged;
            foreach (var range in new[] { systemVolume, microphoneVolume, freeSpace, feedbackVolume }) range.ValueCommitted += AdvancedChanged;
            foreach (var toggle in new[] { cursorCapture, stopLowSpace, animations, saveNotice, saveSound, systemEnabled, microphoneEnabled, checkUpdates, closeTray, minimized }) toggle.CheckedChanged += AdvancedChanged;
            advancedScroll.AttachWheelHandlers(); advancedSync = false; SelectSettingsTab(0); AdvancedInputs(recorder == null && !busy);
        }
        SelectBox Choice(string[] items, int index) { var choice = new SelectBox(); choice.Items.AddRange(items); choice.SelectedIndex = Math.Max(0, index); return choice; }
        void HelpLabel(Control parent, string text, int x, int y, string help)
        {
            var label = LabelAt(parent, text, x, y, 290, 18, 9, false, Theme.Muted); tips.SetToolTip(label, help);
            var question = LabelAt(parent, "?", x + 296, y, 18, 18, 9, true, Theme.Muted); question.TextAlign = ContentAlignment.MiddleCenter; question.AccessibleName = "Справка: " + text; question.Cursor = Cursors.Help; tips.SetToolTip(question, help);
        }
        void AdvancedField(Control parent, string text, Control input, int x, int y, string help)
        { HelpLabel(parent, text, x, y, help); AddInput(parent, input, x, y + 22, 314, text); input.Height = input is RangeBox ? 36 : 32; }
        void AdvancedToggle(Control parent, ToggleSwitch input, string text, int x, int y, string help)
        {
            var question = LabelAt(parent, "?", x, y + 7, 18, 18, 9, true, Theme.Muted); question.Cursor = Cursors.Help; question.TextAlign = ContentAlignment.MiddleCenter; question.AccessibleName = "Справка: " + text; tips.SetToolTip(question, help);
            input.Text = input.AccessibleName = text; input.Font = new Font("Segoe UI", 9); input.BackColor = Theme.Card; input.ForeColor = Theme.Text; input.SetBounds(x + 22, y, 292, 32); parent.Controls.Add(input); tips.SetToolTip(input, help);
        }
        void RefreshBufferFolder() { bufferFolder.Text = string.IsNullOrEmpty(settings.BufferFolder) ? "По умолчанию" : settings.BufferFolder; tips.SetToolTip(bufferFolder, settings.BufferHome); }
        void PreviewFilename()
        {
            bool valid = Settings.ValidTemplate(filenameTemplate.Text); filenamePreview.ForeColor = valid ? Theme.Muted : Theme.Red;
            if (!valid) { filenamePreview.Text = "Недопустимое имя или неизвестный токен — изменения не применены"; return; }
            var preview = new Settings { FilenameTemplate = filenameTemplate.Text, Display = settings.Display }; filenamePreview.Text = preview.ClipStem(DateTime.Now) + (containerChoice.SelectedIndex == 1 ? ".mkv" : ".mp4");
        }
        void SaveTemplate() { if (!Settings.ValidTemplate(filenameTemplate.Text)) { PreviewFilename(); return; } settings.FilenameTemplate = filenameTemplate.Text; AdvancedChanged(filenameTemplate, EventArgs.Empty); }
        void UpdateScalingWarning() { scalingWarning.Text = resolutionChoice.SelectedIndex == 0 ? "" : "Масштабирование через CPU\nПовышает нагрузку и расход RAM"; }
        void RefreshAudioChoices()
        { bool previous = advancedSync; advancedSync = true; try { FillAudioChoices(systemDevice, true, settings.SystemDevice); FillAudioChoices(microphoneDevice, false, settings.MicrophoneDevice); } finally { advancedSync = previous; } }
        static void FillAudioChoices(SelectBox select, bool system, string id)
        { select.ClosePopup(); select.Items.Clear(); foreach (var choice in AudioChoice.Enumerate(system, id)) select.Items.Add(choice); select.SelectedIndex = select.Items.FindIndex(item => ((AudioChoice)item).Id == id); if (select.SelectedIndex < 0) select.SelectedIndex = 0; }
        void SelectSettingsTab(int tab)
        {
            hotkey.CancelCapture(); CloseSettingsPopups(); settingsTab = Math.Max(0, Math.Min(5, tab));
            for (int i = 0; i < 6; i++) { advancedGroups[i].Visible = i == settingsTab; settingsTabs[i].Fill = i == settingsTab ? Theme.Input : Theme.Background; settingsTabs[i].ForeColor = i == settingsTab ? Theme.Accent : Theme.Muted; settingsTabs[i].Invalidate(); }
            advancedScroll.ContentHeight = settingsTab == 0 ? 254 : 242; advancedScroll.ScrollTo(0);
            if (settingsTab == 3) RefreshAudioChoices(); pageHeading.Text = settingsTitles[settingsTab];
        }
        void ShowAdvanced(bool show) { if (advancedOptions == null) BuildAdvancedSettings(); advancedOptions.Visible = true; SelectSettingsTab(show ? 3 : settingsTab); UpdateNavigation(); }
        void CloseSettingsPopups()
        {
            foreach (var select in new[] { duration, fps, sound, display, systemDevice, microphoneDevice, containerChoice, codecChoice, resolutionChoice, controlChoice, themeChoice, toneChoice }) if (select != null) select.ClosePopup();
        }
        void ScrollAdvancedGroup(int index) { SelectSettingsTab(index); }
        internal void PreviewAdvancedForDocs(int group) { testing = true; advancedPreviewGroup = Math.Max(0, Math.Min(5, group)); ShowSettings(true); SelectSettingsTab(advancedPreviewGroup); }
        internal void PositionAdvancedPreview() { SelectSettingsTab(advancedPreviewGroup); }
        void AdvancedInputs(bool enabled)
        {
            if (advancedOptions == null) return;
            for (int i = 0; i < 4; i++) advancedGroups[i].Enabled = enabled;
            advancedReset.Enabled = enabled; freeSpace.Enabled = enabled && stopLowSpace.Checked;
            systemDevice.Enabled = systemVolume.Enabled = enabled && systemEnabled.Checked; microphoneDevice.Enabled = microphoneVolume.Enabled = enabled && microphoneEnabled.Checked;
            advancedNote.Text = enabled ? "" : "Для изменения записи остановите буфер"; advancedNote.ForeColor = Theme.Muted;
        }
        void AdvancedChanged(object sender, EventArgs e)
        {
            if (advancedSync || released || closing) return;
            bool capture = sender != animations && sender != saveNotice && sender != saveSound && sender != toneChoice && sender != feedbackVolume && sender != themeChoice && sender != checkUpdates && sender != closeTray && sender != minimized;
            if (capture && (recorder != null || busy)) return;
            if (capture)
            {
                settings.SystemDevice = ((AudioChoice)systemDevice.Items[systemDevice.SelectedIndex]).Id; settings.MicrophoneDevice = ((AudioChoice)microphoneDevice.Items[microphoneDevice.SelectedIndex]).Id;
                settings.SystemVolume = systemVolume.Value; settings.MicrophoneVolume = microphoneVolume.Value; settings.FreeSpaceMb = freeSpace.Value; settings.StopWhenLowSpace = stopLowSpace.Checked; settings.CaptureCursor = cursorCapture.Checked;
                settings.Container = containerChoice.SelectedIndex == 1 ? "mkv" : "mp4"; settings.Codec = new[] { "h264", "hevc", "av1" }[codecChoice.SelectedIndex]; settings.Resolution = new[] { "native", "1080", "720" }[resolutionChoice.SelectedIndex]; settings.RateControl = controlChoice.SelectedIndex == 1 ? "vbr" : "cbr";
                advancedSync = true;
                try { sound.SelectedIndex = systemEnabled.Checked ? (microphoneEnabled.Checked ? 0 : 1) : (microphoneEnabled.Checked ? 3 : 2); }
                finally { advancedSync = false; }
                UpdateScalingWarning(); PreviewFilename();
            }
            settings.Animations = animations.Checked; settings.SaveNotification = saveNotice.Checked; settings.SaveSound = saveSound.Checked; settings.FeedbackVolume = feedbackVolume.Value; settings.FeedbackTone = toneChoice.SelectedIndex;
            settings.CheckUpdatesOnStartup = checkUpdates.Checked; settings.CloseToTray = closeTray.Checked; settings.StartMinimized = minimized.Checked;
            string palette = new[] { "mint", "ocean", "violet", "light" }[themeChoice.SelectedIndex]; if (settings.Palette != palette) { settings.Palette = palette; ApplyPalette(palette); }
            if (!settings.Animations) StopAnimations(); AdvancedInputs(recorder == null && !busy); if (testing) { if (recorder == null && !busy) ReadCaptureOptions(); return; }
            try { if (recorder == null && !busy) ReadCaptureOptions(); settings.Save(); advancedNote.ForeColor = Theme.Muted; advancedNote.Text = "Изменения сохранены"; }
            catch (Exception error) { advancedNote.ForeColor = Theme.Red; advancedNote.Text = "Не удалось сохранить настройки"; tips.SetToolTip(advancedNote, error.Message); }
        }
        void ResetAdvancedSettings()
        {
            if (recorder != null || busy) return;
            // One reset for all application preferences; folder, monitor, hotkey and Windows registration are retained to avoid changing external state.
            advancedSync = true;
            try
            {
                duration.SelectedIndex = 1; fps.SelectedIndex = 0; rate.Value = 8; systemEnabled.Checked = microphoneEnabled.Checked = true;
                systemDevice.SelectedIndex = microphoneDevice.SelectedIndex = 0; systemVolume.Value = microphoneVolume.Value = 100; cursorCapture.Checked = stopLowSpace.Checked = true;
                settings.BufferFolder = ""; RefreshBufferFolder(); freeSpace.Value = 128; containerChoice.SelectedIndex = codecChoice.SelectedIndex = resolutionChoice.SelectedIndex = controlChoice.SelectedIndex = themeChoice.SelectedIndex = toneChoice.SelectedIndex = 0;
                settings.FilenameTemplate = "Replay-{date}_{time}"; filenameTemplate.Text = settings.FilenameTemplate; feedbackVolume.Value = 60;
                animations.Checked = saveNotice.Checked = checkUpdates.Checked = closeTray.Checked = true; saveSound.Checked = minimized.Checked = false;
            }
            finally { advancedSync = false; }
            AdvancedChanged(advancedReset, EventArgs.Empty);
        }
        void ApplyPalette(string palette)
        {
            StopAnimations(); var before = new[] { Theme.Background, Theme.Card, Theme.Input, Theme.Border, Theme.Text, Theme.Muted, Theme.Accent, Theme.Red };
            Theme.SetPalette(palette); var after = new[] { Theme.Background, Theme.Card, Theme.Input, Theme.Border, Theme.Text, Theme.Muted, Theme.Accent, Theme.Red };
            Recolor(this, before, after);
            badge.Set(badge.Text, trayState == TrayState.Recording ? Theme.Accent : trayState == TrayState.Saving ? Theme.Blue : trayState == TrayState.Error ? Theme.Red : Theme.Muted); badge.FinishAnimation();
            SyncActions(); if (advancedOptions != null) SelectSettingsTab(settingsTab); Invalidate(true);
        }
        static Color MapColor(Color value, Color[] before, Color[] after) { for (int i = 0; i < before.Length; i++) if (value.ToArgb() == before[i].ToArgb()) return after[i]; return value; }
        static void Recolor(Control root, Color[] before, Color[] after)
        {
            root.BackColor = MapColor(root.BackColor, before, after); root.ForeColor = MapColor(root.ForeColor, before, after);
            var button = root as ActionButton; if (button != null) { button.Fill = MapColor(button.Fill, before, after); button.FinishAnimation(); }
            foreach (Control child in root.Controls) Recolor(child, before, after);
        }
        IEnumerable<ActionButton> AllActionButtons(Control parent) { foreach (Control child in parent.Controls) { var button = child as ActionButton; if (button != null) yield return button; foreach (var nested in AllActionButtons(child)) yield return nested; } }
        async Task VerifyAdvancedSettingsForTest(StringBuilder report)
        {
            ShowSettings(true);
            for (int repeat = 0; repeat < 3; repeat++)
            {
                for (int tab = 0; tab < 6; tab++) { settingsTabs[tab].PerformClick(); if (!advancedGroups[tab].Visible || pageHeading.Text != settingsTitles[tab]) throw new Exception("Settings tab navigation failed."); VerifyControlBoundsForTest(settingsPage); }
                settingsBack.PerformClick(); if (settingsPage.Visible || !recordingPage.Visible || pageHeading.Text.Length != 0) throw new Exception("Back failed."); settingsGear.PerformClick();
            }
            Inputs(false); for (int i = 0; i < 4; i++) if (advancedGroups[i].Enabled) throw new Exception("Recording settings not locked."); if (!advancedGroups[4].Enabled || !advancedGroups[5].Enabled || advancedReset.Enabled) throw new Exception("Live preferences/reset policy failed."); Inputs(true);
            SelectSettingsTab(0); advancedScroll.ContentHeight = 300; advancedScroll.ScrollTo(int.MaxValue); if (advancedScroll.ScrollOffset <= 0) throw new Exception("Capture scroll unavailable."); advancedScroll.ScrollTo(0);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            int previousReserve = freeSpace.Value;
            var wheel = new HandledMouseEventArgs(MouseButtons.None, 0, 5, 5, -120); typeof(Control).GetMethod("OnMouseWheel", flags).Invoke(freeSpace, new object[] { wheel });
            if (!wheel.Handled || advancedScroll.ScrollOffset != 44 || freeSpace.Value != previousReserve) throw new Exception("Wheel changed reserve or did not scroll.");
            var thumb = (Control)typeof(SettingsScrollPanel).GetField("thumb", flags).GetValue(advancedScroll);
            typeof(SettingsScrollThumb).GetField("dragOffset", flags).SetValue(thumb, 0); typeof(SettingsScrollThumb).GetMethod("DragTo", flags).Invoke(thumb, new object[] { thumb.Height });
            if (advancedScroll.ScrollOffset != 44) throw new Exception("Scrollbar drag failed."); advancedScroll.ScrollTo(0); advancedScroll.ContentHeight = 254;
            bool oldAnimation = animations.Checked; animations.Checked = false; start.PreviewHover(true); if (ActiveAnimations != 0) throw new Exception("Disabled animation active."); animations.Checked = oldAnimation;
            foreach (string palette in new[] { "ocean", "violet", "light", "mint" }) { ApplyPalette(palette); if (BackColor != Theme.Background) throw new Exception("Theme application failed."); }
            string oldName = settings.FilenameTemplate; filenameTemplate.Text = "../bad"; SaveTemplate(); if (settings.FilenameTemplate != oldName) throw new Exception("Invalid template applied."); filenameTemplate.Text = oldName;
            systemEnabled.Checked = false; microphoneEnabled.Checked = true; if (settings.Audio != "microphone") throw new Exception("Microphone-only failed."); systemEnabled.Checked = true;
            string file = Path.Combine(Settings.Home, "settings.txt"); bool existed = File.Exists(file); byte[] original = existed ? File.ReadAllBytes(file) : null;
            try
            {
                testing = false; containerChoice.SelectedIndex = 1; codecChoice.SelectedIndex = 1; resolutionChoice.SelectedIndex = 2; controlChoice.SelectedIndex = 1;
                filenameTemplate.Text = "Clip-{monitor}-{date}"; SaveTemplate(); systemVolume.EditForTest(63); microphoneVolume.EditForTest(27); systemEnabled.Checked = false;
                var loaded = Settings.Load(); if (loaded.Container != "mkv" || loaded.Codec != "hevc" || loaded.Resolution != "720" || loaded.RateControl != "vbr" || loaded.FilenameTemplate != filenameTemplate.Text || loaded.SystemVolume != 63 || loaded.MicrophoneVolume != 27 || loaded.Audio != "microphone") throw new Exception("Settings UI did not persist capture preferences.");
                recorder = new Recorder(settings); Inputs(false); checkUpdates.Checked = false; closeTray.Checked = false; minimized.Checked = true; feedbackVolume.EditForTest(17); toneChoice.SelectedIndex = 2; saveSound.Checked = true; themeChoice.SelectedIndex = 1;
                loaded = Settings.Load(); if (loaded.CheckUpdatesOnStartup || loaded.CloseToTray || !loaded.StartMinimized || loaded.FeedbackVolume != 17 || loaded.FeedbackTone != 2 || !loaded.SaveSound || loaded.Palette != "ocean" || loaded.Codec != "hevc") throw new Exception("Live preferences did not persist independently.");
                recorder.Dispose(); recorder = null; Inputs(true); ResetAdvancedSettings(); loaded = Settings.Load();
                if (loaded.Container != "mp4" || loaded.Codec != "h264" || loaded.Resolution != "native" || loaded.Palette != "mint" || !loaded.CloseToTray || loaded.StartMinimized || loaded.FeedbackVolume != 60 || loaded.SystemVolume != 100 || loaded.Audio != "both") throw new Exception("Single reset failed.");
            }
            finally { testing = true; if (recorder != null) { recorder.Dispose(); recorder = null; } Inputs(true); if (existed) File.WriteAllBytes(file, original); else if (File.Exists(file)) File.Delete(file); }
            using (var exitForm = new MainForm(false, true))
            {
                exitForm.settings.CloseToTray = false; exitForm.Show(); exitForm.Close(); await Task.Delay(50);
                if (!exitForm.released || !exitForm.quitting || exitForm.tray.Visible) throw new Exception("Close-to-exit preference failed.");
            }
            report.AppendLine("SIX_ICON_TABS_BACK_HELP_THEMES_RECORDING_LOCK_AND_MIC_ONLY: PASS");
            report.AppendLine("SETTINGS_UI_AUTOSAVE_LIVE_PREFERENCES_SINGLE_RESET_CUSTOM_SCROLL_CLOSE_TO_EXIT: PASS");
            AdvancedSettingsTests.Run(report); await AdvancedSettingsTests.VerifyBehaviorAsync(report);
            SelectSettingsTab(0); ShowSettings(true);
        }
    }
}
