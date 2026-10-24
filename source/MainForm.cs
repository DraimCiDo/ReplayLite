using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReplayLite
{
    public sealed class MainForm : Form
    {
        readonly Settings settings = Settings.Load();
        Recorder recorder;
        string lastAudioSummary;
        bool lastAudioDegraded;
        bool recoveryPending;
        int recoveryFailures;
        DateTime nextRecovery;
        DateTime recoveryHealthySince;
        readonly SelectBox duration = new SelectBox(), fps = new SelectBox(), sound = new SelectBox(), display = new SelectBox();
        readonly NumberBox rate = new NumberBox();
        readonly HotkeyBox hotkey = new HotkeyBox(); readonly Label hotkeyInfo = new Label();
        List<MonitorChoice> monitors = new List<MonitorChoice>();
        bool hotkeyRegistered;
        readonly ActionButton update = new ActionButton(); bool updating, monitorRefresh;
        readonly ToggleSwitch startup = new ToggleSwitch(); readonly Label startupInfo = new Label();
        readonly bool startInTray;
        bool startupCanceled;
        readonly System.Threading.CancellationTokenSource updateCancel = new System.Threading.CancellationTokenSource();
        DateTime nextUpdateCheck = DateTime.MaxValue;
        bool checkingUpdates, updateDialogOpen, startupChanging, lastBalloonWasUpdate;
        ReleaseInfo availableRelease; Version notifiedVersion;
        readonly TextBox folder = new TextBox();
        readonly ActionButton start = new ActionButton(), save = new ActionButton(), browse = new ActionButton(), openLast = new ActionButton(), openFolder = new ActionButton();
        readonly Panel recordingPage = new Panel(), settingsPage = new Panel();
        readonly ActionButton recordingTab = new ActionButton(), settingsTab = new ActionButton();
        readonly Label versionLabel = new Label(), settingsNote = new Label(), videoSummary = new Label();
        readonly string appVersion = typeof(MainForm).Assembly.GetName().Version.ToString(3);
        readonly Label state = new Label(), stateDetail = new Label();
        readonly Label cpuValue = new Label(), ramValue = new Label(), bufferValue = new Label(), diskValue = new Label();
        readonly StatusBadge badge = new StatusBadge(); readonly BufferBar progress = new BufferBar();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolStripMenuItem traySave = new ToolStripMenuItem(), trayToggle = new ToolStripMenuItem(), trayUpdate = new ToolStripMenuItem();
        readonly Timer timer = new Timer { Interval = 1000 };
        readonly ToolTip tips = new ToolTip();
        readonly Icon[] icons = { AppIcons.Create(TrayState.Idle), AppIcons.Create(TrayState.Recording), AppIcons.Create(TrayState.Saving), AppIcons.Create(TrayState.Error) };
        readonly Bitmap brand = AppIcons.Draw(64, TrayState.Recording);
        TrayState trayState = TrayState.Idle;
        bool busy, saving, closing, quitting, released, testing, bufferReady;
        double lastCpu, lastTime;
        string lastReplay;
        readonly Stopwatch clock = Stopwatch.StartNew();

        public MainForm(bool launchInTray = false, bool testMode = false)
        {
            startInTray = launchInTray; testing = testMode;
            Text = "ReplayLite v" + appVersion; ClientSize = new Size(760, 480); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Icon = icons[(int)TrayState.Recording]; DoubleBuffered = true;
            var logo = new PictureBox { Image = brand, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Background }; logo.SetBounds(20, 17, 48, 48); Controls.Add(logo);
            LabelAt(this, "ReplayLite", 79, 12, 165, 35, 21, true, Theme.Text);
            versionLabel.Text = "v" + appVersion; versionLabel.SetBounds(247, 23, 100, 23); versionLabel.Font = new Font("Segoe UI", 9); versionLabel.ForeColor = Theme.Muted; Controls.Add(versionLabel);
            badge.SetBounds(564, 17, 176, 32); Controls.Add(badge);
            StyleButton(update, "Проверить обновления", this, 564, 53, 176, 26, false); update.Font = new Font("Segoe UI", 9);
            update.Click += async delegate { await OpenUpdates(); };
            StyleButton(recordingTab, "Запись", this, 20, 88, 140, 32, false);
            StyleButton(settingsTab, "Настройки", this, 168, 88, 164, 32, false);
            recordingTab.Click += delegate { ShowSettings(false); }; settingsTab.Click += delegate { ShowSettings(true); };
            recordingPage.SetBounds(0, 134, 760, 325); settingsPage.SetBounds(0, 134, 760, 325);
            recordingPage.BackColor = settingsPage.BackColor = Theme.Background; Controls.Add(recordingPage); Controls.Add(settingsPage);

            var recording = Card(recordingPage, 20, 0, 720, 168);
            state.SetBounds(20, 15, 440, 33); state.Font = new Font("Segoe UI", 16, FontStyle.Bold); state.ForeColor = Theme.Text; state.BackColor = Theme.Card; state.AutoEllipsis = true; recording.Controls.Add(state);
            stateDetail.SetBounds(22, 53, 438, 36); stateDetail.Font = new Font("Segoe UI", 8.5f); stateDetail.ForeColor = Theme.Muted; stateDetail.BackColor = Theme.Card; recording.Controls.Add(stateDetail);
            progress.SetBounds(22, 98, 438, 6); recording.Controls.Add(progress);
            StyleButton(start, "Начать запись", recording, 20, 117, 440, 34, false);
            StyleButton(save, "Сохранить повтор", recording, 480, 18, 220, 133, true); save.Enabled = false;
            start.Click += async delegate { await Toggle(); }; save.Click += async delegate { await SaveReplay(); };
            Metric(cpuValue, "CPU", 20); Metric(ramValue, "ПАМЯТЬ", 203); Metric(bufferValue, "В БУФЕРЕ", 386); Metric(diskValue, "НА ДИСКЕ", 569);
            diskValue.Text = "0 МБ";
            videoSummary.SetBounds(25, 259, 710, 19); videoSummary.Font = new Font("Segoe UI", 8); videoSummary.ForeColor = Theme.Muted; videoSummary.AutoEllipsis = true; recordingPage.Controls.Add(videoSummary);
            StyleButton(openFolder, "Папка повторов", recordingPage, 20, 285, 354, 32, false); openFolder.Font = new Font("Segoe UI", 9); openFolder.Click += delegate { OpenFolder(); };
            StyleButton(openLast, "Последний повтор", recordingPage, 386, 285, 354, 32, false); openLast.Font = new Font("Segoe UI", 9); openLast.Enabled = false;
            openLast.Click += delegate { if (lastReplay != null && File.Exists(lastReplay)) Process.Start("explorer.exe", Recorder.Quote(lastReplay)); };

            var options = Card(settingsPage, 20, 0, 720, 325);
            LabelAt(options, "Настройки записи", 16, 12, 320, 25, 11, true, Theme.Text);
            settingsNote.SetBounds(370, 17, 334, 20); settingsNote.Font = new Font("Segoe UI", 8); settingsNote.ForeColor = Theme.Muted; settingsNote.BackColor = Theme.Card; settingsNote.Text = "Изменения сохраняются автоматически"; options.Controls.Add(settingsNote);
            LabelAt(options, "Экран для записи", 16, 42, 334, 20, 9, false, Theme.Muted);
            LabelAt(options, "Длительность повтора", 370, 42, 334, 20, 9, false, Theme.Muted);
            RefreshMonitors(); AddInput(options, display, 16, 64, 334, "Экран для записи");
            duration.Items.AddRange(new object[] { "30 секунд", "2 минуты", "5 минут", "10 минут" }); duration.SelectedIndex = settings.Seconds == 30 ? 0 : settings.Seconds == 300 ? 2 : settings.Seconds == 600 ? 3 : 1;
            AddInput(options, duration, 370, 64, 334, "Длительность повтора");
            LabelAt(options, "Звук", 16, 104, 334, 18, 9, false, Theme.Muted);
            LabelAt(options, "Горячая клавиша сохранения", 370, 104, 334, 18, 9, false, Theme.Muted);
            sound.Items.AddRange(new object[] { "Компьютер и микрофон", "Только звук компьютера", "Без звука" }); sound.SelectedIndex = settings.Audio == "none" ? 2 : settings.Audio == "system" ? 1 : 0;
            AddInput(options, sound, 16, 124, 334, "Источники звука");
            hotkey.Binding = settings.Hotkey; AddInput(options, hotkey, 370, 124, 334, "Горячая клавиша сохранения");
            hotkeyInfo.SetBounds(370, 166, 334, 40); hotkeyInfo.BackColor = Theme.Card; hotkeyInfo.ForeColor = Theme.Muted; hotkeyInfo.Font = new Font("Segoe UI", 8); options.Controls.Add(hotkeyInfo);
            hotkey.CaptureStarted += delegate { if (hotkeyRegistered) Native.UnregisterHotKey(Handle, 1); hotkeyRegistered = false; hotkeyInfo.Text = "Нажмите сочетание и отпустите клавиши.\nДля отмены щёлкните вне поля."; };
            hotkey.CaptureCancelled += delegate { if (!released) RegisterConfiguredHotkey(); };
            hotkey.CaptureFailed += delegate { hotkeyInfo.ForeColor = Theme.Red; hotkeyInfo.Text = "Не удалось включить ввод. Повторите попытку; прежняя клавиша сохранена."; };
            hotkey.BindingProposed += delegate(object sender, BindingEventArgs e) { e.Accepted = TryChangeHotkey(e.Binding); };
            LabelAt(options, "Плавность видео", 16, 162, 334, 18, 9, false, Theme.Muted); LabelAt(options, "Качество видео · Мбит/с", 16, 220, 334, 18, 9, false, Theme.Muted);
            fps.Items.AddRange(new object[] { "30 кадров/с · экономнее", "60 кадров/с · плавнее" }); fps.SelectedIndex = settings.Fps == 60 ? 1 : 0;
            rate.Minimum = 2; rate.Maximum = 30; rate.Value = settings.Mbps;
            AddInput(options, fps, 16, 182, 334, "Частота кадров"); AddInput(options, rate, 16, 240, 334, "Битрейт");
            LabelAt(options, "Папка повторов", 16, 279, 518, 19, 8, false, Theme.Muted);
            folder.Text = settings.Folder; folder.ReadOnly = true; folder.BorderStyle = BorderStyle.None; folder.BackColor = Theme.Card; folder.ForeColor = Theme.Text; folder.Font = new Font("Segoe UI", 8.5f); folder.SetBounds(18, 301, 516, 18); folder.TabStop = false; options.Controls.Add(folder);
            StyleButton(browse, "Изменить папку", options, 548, 282, 156, 32, false); browse.Font = new Font("Segoe UI", 9);
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog { SelectedPath = folder.Text, Description = "Папка для сохранённых MP4" }) if (dialog.ShowDialog(this) == DialogResult.OK) { folder.Text = dialog.SelectedPath; CaptureOptionsChanged(this, EventArgs.Empty); } };
            foreach (var control in new Control[] { duration, display, sound, hotkey, fps, rate }) control.Height = 36;
            foreach (var control in new Control[] { sound, fps, rate }) control.Height = 34;
            startup.SetBounds(370, 211, 334, 32); startup.Font = new Font("Segoe UI", 9); startup.Text = "Запускать с Windows"; options.Controls.Add(startup);
            startupInfo.SetBounds(370, 247, 334, 31); startupInfo.BackColor = Theme.Card; startupInfo.ForeColor = Theme.Muted; startupInfo.Font = new Font("Segoe UI", 8); startupInfo.Text = Settings.Portable ? "Буфер включается автоматически.\nДля Portable не перемещайте папку." : "Запуск в трее с включённым буфером."; options.Controls.Add(startupInfo);
            if (!testing) try { startup.Checked = StartupRegistration.Enabled(Application.ExecutablePath); } catch (Exception error) { startup.Enabled = false; startupInfo.Text = "Не удалось прочитать автозапуск."; tips.SetToolTip(startupInfo, error.Message); }
            startup.CheckedChanged += delegate
            {
                if (testing || startupChanging) return;
                try { StartupRegistration.SetEnabled(Application.ExecutablePath, startup.Checked); }
                catch (Exception error) { startupChanging = true; startup.Checked = !startup.Checked; startupChanging = false; startupInfo.Text = "Не удалось изменить автозапуск."; tips.SetToolTip(startupInfo, error.Message); }
            };
            foreach (var select in new[] { duration, fps, sound, display }) select.SelectedIndexChanged += CaptureOptionsChanged;
            rate.ValueChanged += CaptureOptionsChanged;
            tips.SetToolTip(display, "Записывается весь выбранный экран. Для записи монитор должен быть подключён к NVIDIA.");
            tips.SetToolTip(duration, "Сохраняется уже записанный фрагмент, до выбранной длительности. Полного заполнения ждать не обязательно.");
            tips.SetToolTip(rate, "Больше значение — выше качество и размер файлов. 8 Мбит/с — около 1 МБ записи в секунду.");
            tips.SetToolTip(recordingTab, "Крестик скрывает окно в трей. Завершить работу можно через меню значка рядом с часами.");
            UpdateOptionsSummary(); ShowSettings(false);
            tray.Icon = icons[0]; tray.Text = "ReplayLite — буфер выключен"; tray.Visible = true;
            var menu = new RoundedMenu { Font = Font, ShowImageMargin = true, ImageScalingSize = new Size(Math.Max(16, Font.Height - 2), Math.Max(16, Font.Height - 2)) };
            menu.Items.Add("Открыть ReplayLite", menu.AddIcon(MenuGlyph.Open), delegate { Restore(); }); menu.Items.Add(new ToolStripSeparator());
            traySave.Image = menu.AddIcon(MenuGlyph.Save); trayToggle.Image = menu.AddIcon(MenuGlyph.Record);
            traySave.Text = "Сохранить повтор"; traySave.Enabled = false; traySave.Click += async delegate { await SaveReplay(); }; menu.Items.Add(traySave);
            trayToggle.Text = "Включить буфер"; trayToggle.Click += async delegate { await Toggle(); }; menu.Items.Add(trayToggle);
            menu.Items.Add("Папка повторов", menu.AddIcon(MenuGlyph.Folder), delegate { OpenFolder(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Выход", menu.AddIcon(MenuGlyph.Exit), async delegate { await Quit(); }); tray.ContextMenuStrip = menu;
            trayUpdate.Text = "Доступно обновление"; trayUpdate.Image = menu.AddIcon(MenuGlyph.Open); trayUpdate.Visible = false; trayUpdate.Click += async delegate { await OpenUpdates(); }; menu.Items.Insert(1, trayUpdate);
            tray.DoubleClick += delegate { Restore(); };
            tray.BalloonTipClicked += async delegate { if (lastBalloonWasUpdate && availableRelease != null) await OpenUpdates(); };
            menu.Opening += delegate { if (recorder != null && recorder.Running && !busy) UpdateSaveAvailability(recorder.BufferedSeconds); };
            SetState("Готово к записи");
            timer.Tick += async delegate { await RefreshState(); }; timer.Start();
            VisibleChanged += delegate { if (!Visible) { hotkey.CancelCapture(); StopAnimations(); } else if (recorder != null && recorder.Running && !busy) RefreshBufferMetrics(); };
            Deactivate += delegate { hotkey.CancelCapture(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) { hotkey.CancelCapture(); StopAnimations(); } };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (quitting) return; if (e.CloseReason == CloseReason.WindowsShutDown) { EndWindowsSession(); return; } e.Cancel = true; Hide(); };
            Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch += SessionChanged;
        }

        CardPanel Card(Control parent, int x, int y, int width, int height) { var card = new CardPanel(); card.SetBounds(x, y, width, height); parent.Controls.Add(card); return card; }
        Label LabelAt(Control parent, string text, int x, int y, int width, int height, float size, bool bold, Color color)
        { var label = new Label { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = parent == this ? Theme.Background : Theme.Card, AutoEllipsis = true }; label.SetBounds(x, y, width, height); parent.Controls.Add(label); return label; }
        void Metric(Label value, string title, int x)
        { var card = Card(recordingPage, x, 184, 171, 65); LabelAt(card, title, 14, 10, 143, 17, 7.5f, false, Theme.Muted); value.SetBounds(12, 29, 147, 29); value.Font = new Font("Segoe UI", 14, FontStyle.Bold); value.ForeColor = Theme.Text; value.BackColor = Theme.Card; value.Text = "—"; card.Controls.Add(value); }
        int SelectedSeconds { get { return duration.SelectedIndex == 0 ? 30 : duration.SelectedIndex == 2 ? 300 : duration.SelectedIndex == 3 ? 600 : 120; } }
        static string ClockTime(double seconds) { return TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture); }
        bool SelectedMonitorSupported { get { return display.SelectedIndex >= 0 && display.SelectedIndex < monitors.Count && monitors[display.SelectedIndex].CanCapture; } }
        void ShowSettings(bool show)
        {
            hotkey.CancelCapture();
            recordingPage.Visible = !show; settingsPage.Visible = show;
            recordingTab.Fill = show ? Theme.Input : Theme.Mix(Theme.Input, Theme.Accent, .16f); recordingTab.ForeColor = show ? Theme.Muted : Theme.Accent;
            settingsTab.Fill = show ? Theme.Mix(Theme.Input, Theme.Accent, .16f) : Theme.Input; settingsTab.ForeColor = show ? Theme.Accent : Theme.Muted;
        }
        void UpdateOptionsSummary()
        {
            bool active = recorder != null && recorder.Running;
            videoSummary.Text = duration.Text + " · " + (active ? recorder.AudioSummary : sound.Text) + " · " + (fps.SelectedIndex == 1 ? "60" : "30") + " кадров/с · " + rate.Value + " Мбит/с";
            tips.SetToolTip(videoSummary, display.Text + "\n" + videoSummary.Text + (active && recorder.AudioDegraded ? "\nВыбрано: " + sound.Text + ". Недоступные устройства подключатся автоматически." : "")); tips.SetToolTip(folder, folder.Text); tips.SetToolTip(openFolder, folder.Text);
            if (recorder == null) bufferValue.Text = "0:00 / " + ClockTime(SelectedSeconds);
            UpdateHotkeyLabels();
        }
        void ReadCaptureOptions()
        {
            settings.Seconds = SelectedSeconds; settings.Fps = fps.SelectedIndex == 1 ? 60 : 30; settings.Mbps = rate.Value;
            settings.Audio = new[] { "both", "system", "none" }[sound.SelectedIndex]; settings.Folder = folder.Text;
            if (display.SelectedIndex >= 0 && display.SelectedIndex < monitors.Count)
            { var monitor = monitors[display.SelectedIndex]; settings.Display = monitor.Output; settings.DisplayAdapter = monitor.Adapter; settings.MonitorId = monitor.Id; settings.Rotation = monitor.Rotation; }
        }
        void CaptureOptionsChanged(object sender, EventArgs e)
        {
            UpdateOptionsSummary(); if (recorder == null && !busy) SetState("Готово к записи");
            if (testing || recorder != null || busy || closing || monitorRefresh) return;
            try { ReadCaptureOptions(); settings.Save(); settingsNote.ForeColor = Theme.Muted; settingsNote.Text = "Изменения сохранены"; }
            catch (Exception error) { settingsNote.ForeColor = Theme.Red; settingsNote.Text = "Не удалось сохранить настройки"; tips.SetToolTip(settingsNote, error.Message); }
        }
        void AddInput(Control parent, Control control, int x, int y, int width, string name)
        {
            control.AccessibleName = name;
            control.SetBounds(x, y, width, 40); parent.Controls.Add(control);
        }
        void StyleButton(ActionButton button, string text, Control parent, int x, int y, int width, int height, bool accent)
        { button.Text = text; button.SetBounds(x, y, width, height); button.BackColor = parent is CardPanel ? Theme.Card : Theme.Background; button.Font = new Font("Segoe UI", 10, FontStyle.Bold); button.ForeColor = accent ? Color.FromArgb(9, 37, 29) : Theme.Text; button.Fill = accent ? Theme.Accent : Theme.Input; parent.Controls.Add(button); }
        void StopAnimations() { foreach (var button in new[] { start, save, browse, openFolder, openLast, update, recordingTab, settingsTab }) button.FinishAnimation(); progress.FinishAnimation(); badge.FinishAnimation(); }
        async Task OpenUpdates()
        {
            if (updating || updateDialogOpen || closing || released) return;
            updateDialogOpen = true; Restore();
            try { using (var dialog = new UpdateForm(ApplyUpdate, availableRelease)) dialog.ShowDialog(this); }
            finally { updateDialogOpen = false; }
            if (updating) await Quit();
        }
        async Task CheckForUpdates()
        {
            if (checkingUpdates || closing || released || testing) return;
            checkingUpdates = true; nextUpdateCheck = DateTime.UtcNow.AddHours(6);
            try { var release = await Updates.Latest(updateCancel.Token); if (!released && !closing) SetAvailableRelease(release); }
            catch (Exception) { /* Automatic checks stay quiet when offline or rate limited. */ }
            finally { checkingUpdates = false; }
        }
        bool SetAvailableRelease(ReleaseInfo release)
        {
            availableRelease = release != null && release.Version > typeof(MainForm).Assembly.GetName().Version ? release : null;
            update.Text = availableRelease == null ? "Проверить обновления" : "Обновить до " + availableRelease.Version.ToString(3);
            update.Fill = availableRelease == null ? Theme.Input : Theme.Accent;
            update.ForeColor = availableRelease == null ? Theme.Text : Theme.Background;
            trayUpdate.Visible = availableRelease != null; trayUpdate.Text = update.Text;
            if (availableRelease == null || availableRelease.Version == notifiedVersion) return false;
            notifiedVersion = availableRelease.Version;
            if (!testing) { lastBalloonWasUpdate = true; tray.ShowBalloonTip(5000, "Доступно обновление ReplayLite", "Версия " + notifiedVersion.ToString(3) + ". Нажмите, чтобы открыть обновление.", ToolTipIcon.Info); }
            return true;
        }
        int ActiveAnimations { get { int count = 0; foreach (var button in new[] { start, save, browse, openFolder, openLast, update, recordingTab, settingsTab }) if (button.Animating) count++; return count + (progress.Animating ? 1 : 0) + (badge.Animating ? 1 : 0); } }
        void SetState(string text, TrayState? explicitState = null)
        {
            var status = explicitState ?? (recorder != null && recorder.Running ? TrayState.Recording : TrayState.Idle);
            state.Text = text.StartsWith("Сохранено:") ? "Повтор сохранён" : text;
            stateDetail.Text = text.StartsWith("Сохранено:") ? text.Substring(10).Trim() + "\nЗапись продолжается. Новые кадры остаются для следующего повтора." : status == TrayState.Saving ? "Дождитесь завершения текущего действия." : status == TrayState.Recording ? "Сохраните нужный момент кнопкой или горячей клавишей.\nЗапись продолжается, даже если закрыть окно." : status == TrayState.Error ? "Проверьте экран, звук и папку сохранения.\nЗатем повторите запуск." : "Включите запись до нужного момента.\nЗатем сохраните уже записанный фрагмент.";
            if (text == "Накапливаем первые кадры…") stateDetail.Text = "Запись началась. Через несколько секунд\nпоявится возможность сохранить первый повтор.";
            if (status == TrayState.Recording && recorder != null && recorder.AudioDegraded && !text.StartsWith("Сохранено:")) stateDetail.Text = "Звук: " + recorder.AudioSummary + ". Запись продолжается.\nНедоступные устройства подключатся автоматически.";
            if (status == TrayState.Idle && !SelectedMonitorSupported)
            { state.Text = "Выберите экран для записи"; stateDetail.Text = "В «Настройках» выберите монитор NVIDIA.\nСейчас выбранный экран недоступен для записи."; }
            badge.Set(status == TrayState.Recording ? "Идёт запись" : status == TrayState.Saving ? "Подождите…" : status == TrayState.Error ? "Ошибка" : "Не записывает", status == TrayState.Recording ? Theme.Accent : status == TrayState.Saving ? Theme.Blue : status == TrayState.Error ? Theme.Red : Theme.Muted);
            if (trayState != status) { tray.Icon = icons[(int)status]; trayState = status; }
            var tooltip = "ReplayLite — " + text; tray.Text = tooltip.Substring(0, Math.Min(63, tooltip.Length));
            SyncActions();
        }
        void SyncActions()
        {
            bool active = recorder != null && (recorder.Running || recoveryPending);
            start.Enabled = !busy && !closing && (active || SelectedMonitorSupported); start.Text = active ? "Остановить запись" : "Начать запись";
            start.Fill = active ? Color.FromArgb(63, 39, 49) : Theme.Input; start.ForeColor = active ? Theme.Red : Theme.Text;
            UpdateSaveAvailability(bufferReady ? 1 : 0);
            trayToggle.Enabled = start.Enabled; trayToggle.Text = active ? "Остановить запись" : "Начать запись";
            hotkey.Enabled = !busy && !closing;
            update.Enabled = !busy && !saving && !closing && !updating;
            trayUpdate.Enabled = update.Enabled;
            UpdateHotkeyLabels();
        }
        void UpdateSaveAvailability(double seconds)
        { bufferReady = seconds > 0; save.Enabled = recorder != null && recorder.Running && bufferReady && !recoveryPending && !busy && !saving && !closing; traySave.Enabled = save.Enabled; }
        void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        void OpenFolder() { Directory.CreateDirectory(settings.Folder); Process.Start("explorer.exe", Recorder.Quote(settings.Folder)); }
        void Inputs(bool enabled) { foreach (var control in new Control[] { duration, fps, sound, rate, display, browse }) control.Enabled = enabled; settingsNote.ForeColor = Theme.Muted; settingsNote.Text = enabled ? "Изменения сохраняются автоматически" : "Для изменения остановите запись"; }
        void RefreshMonitors()
        {
            string previous = recorder != null && !string.IsNullOrEmpty(recorder.Config.MonitorId) ? recorder.Config.MonitorId : display.SelectedIndex >= 0 && display.SelectedIndex < monitors.Count ? monitors[display.SelectedIndex].Id : settings.MonitorId;
            display.ClosePopup(); monitors = MonitorCatalog.Enumerate(); display.Items.Clear();
            foreach (var monitor in monitors) display.Items.Add(monitor);
            if (monitors.Count == 0) { display.Items.Add("Мониторы не обнаружены"); display.SelectedIndex = 0; return; }
            int selected = monitors.FindIndex(m => !string.IsNullOrEmpty(previous) && m.Id == previous);
            if (selected < 0) selected = monitors.FindIndex(m => m.CanCapture && m.Adapter == settings.DisplayAdapter && m.Output == settings.Display);
            if (selected < 0) selected = monitors.FindIndex(m => m.CanCapture);
            display.SelectedIndex = selected < 0 ? 0 : selected;
        }
        void UpdateHotkeyLabels()
        { string key = settings.Hotkey.ToString(); save.Detail = duration.Text + " · " + key; save.AccessibleName = "Сохранить последние " + duration.Text + ", " + key; save.Invalidate(); traySave.Text = "Сохранить повтор   " + key; tips.SetToolTip(save, "Сохранить уже записанный фрагмент. Длительность: " + duration.Text + ". Горячая клавиша: " + key); }
        void RegisterConfiguredHotkey()
        {
            if (released || !IsHandleCreated) return;
            if (hotkeyRegistered) Native.UnregisterHotKey(Handle, 1);
            hotkeyRegistered = Native.RegisterHotKey(Handle, 1, 0x4000 | settings.Hotkey.Modifiers, (uint)settings.Hotkey.Key);
            hotkey.Binding = settings.Hotkey; hotkey.Error = !hotkeyRegistered; hotkey.Invalidate();
            hotkeyInfo.ForeColor = hotkeyRegistered ? Theme.Muted : Theme.Red;
            hotkeyInfo.Text = hotkeyRegistered ? "Нажмите поле, чтобы изменить клавишу.\nОдиночная клавиша перехватывается приложением." : "Сочетание занято другой программой.\nВыберите другое или сохраните повтор кнопкой.";
            UpdateHotkeyLabels();
        }
        bool TryChangeHotkey(KeyBinding binding)
        {
            if (!binding.Valid) { hotkey.Error = true; hotkey.Invalidate(); hotkeyInfo.ForeColor = Theme.Red; hotkeyInfo.Text = "Выбери обычную клавишу или сочетание. F12 зарезервирована Windows; Ctrl/Alt/Shift/Win отдельно не назначаются."; return false; }
            var previous = settings.Hotkey;
            if (hotkeyRegistered) Native.UnregisterHotKey(Handle, 1);
            hotkeyRegistered = false;
            if (!Native.RegisterHotKey(Handle, 1, 0x4000 | binding.Modifiers, (uint)binding.Key))
            { RegisterConfiguredHotkey(); hotkey.Error = true; hotkey.Invalidate(); hotkeyInfo.ForeColor = Theme.Red; hotkeyInfo.Text = "Эта комбинация занята. Выбери другую. Прежняя настройка сохранена."; return false; }
            try { settings.Hotkey = binding; settings.Save(); }
            catch (Exception e) { Native.UnregisterHotKey(Handle, 1); settings.Hotkey = previous; RegisterConfiguredHotkey(); hotkeyInfo.ForeColor = Theme.Red; hotkeyInfo.Text = "Не удалось сохранить: " + e.Message; return false; }
            hotkeyRegistered = true; hotkey.Binding = binding; hotkeyInfo.ForeColor = Theme.Muted; hotkeyInfo.Text = "Сочетание сохранено. Нажми поле, чтобы изменить его снова."; UpdateHotkeyLabels(); return true;
        }
        async Task Toggle(bool background = false)
        {
            if (Native.SessionEnding) return;
            if (!background) startupCanceled = true;
            if (busy || closing) return;
            busy = true; SyncActions(); Exception error = null;
            try
            {
                if (recorder != null) { SetState("Останавливаем запись…", TrayState.Saving); await StopRecorder(); SetState("Запись остановлена"); }
                else
                {
                    if (!Path.IsPathRooted(folder.Text)) throw new ArgumentException("Укажи абсолютный путь папки повторов.");
                    var selectedMonitor = display.SelectedIndex >= 0 && display.SelectedIndex < monitors.Count ? monitors[display.SelectedIndex] : null;
                    if (selectedMonitor == null || !selectedMonitor.CanCapture) throw new InvalidOperationException("Выбери монитор, подключённый к видеокарте NVIDIA.");
                    ReadCaptureOptions(); settings.Save(); Inputs(false); SetState("Запускаем запись…", TrayState.Saving);
                    bufferReady = false; recorder = new Recorder(settings); await recorder.StartAsync(); lastCpu = Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds + recorder.CpuSeconds; lastTime = clock.Elapsed.TotalSeconds; RefreshBufferMetrics(); SetState(bufferReady ? "Идёт фоновая запись" : "Накапливаем первые кадры…", TrayState.Recording);
                }
            }
            catch (Exception e) { error = e; }
            if (error is OperationCanceledException && recorder != null && !closing)
            { if (Native.SessionEnding) recoveryPending = true; else ScheduleRecovery(); error = null; }
            if (error != null && Native.SessionEnding) { recoveryPending = recorder != null; error = null; }
            if (error != null)
            {
                if (recorder != null && CaptureRecovery.ShouldRetry(error))
                { ScheduleRecovery(); error = null; }
            }
            if (error != null)
            {
                try { if (recorder != null) await StopRecorder(); }
                catch (Exception cleanupError) { error = new InvalidOperationException(error.Message + "\n" + cleanupError.Message, error); }
                SetState("Не удалось начать запись", TrayState.Error); stateDetail.Text = error.Message;
            }
            busy = false; SyncActions();
            if (error != null && !background) ShowError(error.Message);
        }
        internal async Task StartInBackgroundAsync(bool startCapture = true)
        {
            if (Native.SessionEnding) return;
            var handle = Handle; RegisterConfiguredHotkey();
            if (startCapture)
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0)
                    {
                        try { await Task.Delay(3000, updateCancel.Token); } catch (OperationCanceledException) { return; }
                    }
                    if (closing || released || Native.SessionEnding || startupCanceled || recorder != null || busy) return;
                    monitorRefresh = true;
                    try { RefreshMonitors(); UpdateOptionsSummary(); }
                    finally { monitorRefresh = false; }
                    await Toggle(true);
                    if (recorder != null && recorder.Running) break;
                }
                if (!closing && !released && recorder == null && !testing)
                { lastBalloonWasUpdate = false; tray.ShowBalloonTip(5000, "Не удалось включить буфер ReplayLite", "Откройте ReplayLite из трея и проверьте экран, звук и папку записи.", ToolTipIcon.Warning); }
            }
            if (!testing && !closing && !released) await CheckForUpdates();
        }
        async Task StopRecorder()
        {
            var current = recorder; if (current == null) return; save.Enabled = traySave.Enabled = false;
            recoveryPending = false; recoveryFailures = 0; recoveryHealthySince = DateTime.MinValue;
            try { await current.StopAsync(); }
            finally
            {
                current.Dispose(); recorder = null; Inputs(true); lastAudioSummary = null;
                bufferReady = false; progress.Value = 0; bufferValue.Text = "0:00 / " + ClockTime(SelectedSeconds); diskValue.Text = "0 МБ"; cpuValue.Text = ramValue.Text = "—"; UpdateOptionsSummary();
            }
        }
        async Task SaveReplay()
        {
            if (Native.SessionEnding) return;
            if (recorder == null || busy || saving || closing) return;
            if (recoveryPending || !recorder.Running) return;
            if (recorder.BufferedSeconds <= 0) { UpdateSaveAvailability(0); SetState("Накапливаем первые кадры…", TrayState.Recording); return; }
            saving = true; SetState("Сохраняю повтор…", TrayState.Saving);
            try { var output = await recorder.SaveAsync(); lastReplay = output; openLast.Enabled = true; RefreshBufferMetrics(); progress.FinishAnimation(); SetState("Сохранено: " + Path.GetFileName(output)); if (!testing) { lastBalloonWasUpdate = false; tray.ShowBalloonTip(3000, "Повтор сохранён", output, ToolTipIcon.Info); } }
            catch (Exception e) { ShowError(e.Message); SetState("Ошибка сохранения", TrayState.Error); }
            finally { saving = false; SyncActions(); }
        }
        async Task RefreshState()
        {
            if (Native.SessionEnding) return;
            if (!testing && !closing && !released && DateTime.UtcNow >= nextUpdateCheck) { var pending = CheckForUpdates(); }
            if (recorder == null || busy || closing) return;
            if (recoveryPending)
            { if (!saving && DateTime.UtcNow >= nextRecovery) await TryRecoverCapture(); return; }
            if (recorder.Failure != null || recorder.NeedsRecovery || !recorder.Running)
            {
                busy = true; string message = recorder.Failure ?? ("Запись остановлена.\n" + recorder.ErrorLog);
                if (recorder.NeedsRecovery || CaptureRecovery.IsTransient(message + recorder.ErrorLog))
                { busy = false; ScheduleRecovery(); if (!saving && DateTime.UtcNow >= nextRecovery) await TryRecoverCapture(); return; }
                try { await StopRecorder(); }
                catch (Exception error) { message += "\n" + error.Message; }
                finally { busy = false; SetState("Захват остановлен", TrayState.Error); SyncActions(); }
                ShowError(message); return;
            }
            // Health monitoring stays active in the tray; hidden UI skips metrics and all painting.
            if (!Visible || WindowState == FormWindowState.Minimized) return;
            try
            {
                var process = Process.GetCurrentProcess(); process.Refresh(); double total = process.TotalProcessorTime.TotalSeconds + recorder.CpuSeconds, now = clock.Elapsed.TotalSeconds;
                double cpu = Math.Max(0, (total - lastCpu) / Math.Max(.01, now - lastTime) / Environment.ProcessorCount * 100); lastCpu = total; lastTime = now;
                cpuValue.Text = cpu.ToString("0.0") + "%"; ramValue.Text = ((process.WorkingSet64 + recorder.Memory) / 1048576.0).ToString("0") + " МБ";
                RefreshBufferMetrics();
            }
            catch (Exception e) { SetState("Ошибка состояния", TrayState.Error); stateDetail.Text = e.Message; }
        }
        void ScheduleRecovery()
        {
            if (recorder == null || closing || released || Native.SessionEnding || recoveryPending) return;
            var now = DateTime.UtcNow; bool unstable = recoveryHealthySince != DateTime.MinValue && now - recoveryHealthySince < TimeSpan.FromSeconds(30);
            if (unstable) recoveryFailures = Math.Min(30, recoveryFailures + 1); else recoveryFailures = 0;
            recoveryPending = true; nextRecovery = now.AddSeconds(unstable ? CaptureRecovery.RetrySeconds(recoveryFailures) : 0);
            SetState("Ожидаем доступ к экрану…", TrayState.Saving);
            stateDetail.Text = "Перезапускаем захват; при необходимости используем резервный способ.\nЗапись можно остановить обычной кнопкой.";
            UpdateSaveAvailability(0); SyncActions();
        }
        async Task TryRecoverCapture()
        {
            if (recorder == null || busy || saving || closing || Native.SessionEnding || !recoveryPending) return;
            busy = true; SyncActions(); Exception fatal = null;
            try
            {
                await recorder.RecoverAsync(updateCancel.Token);
                if (!closing && !released)
                {
                    recoveryPending = false; recoveryHealthySince = DateTime.UtcNow;
                    monitorRefresh = true;
                    try { RefreshMonitors(); UpdateOptionsSummary(); } finally { monitorRefresh = false; }
                    lastCpu = Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds + recorder.CpuSeconds; lastTime = clock.Elapsed.TotalSeconds;
                    SetState("Запись восстановлена", TrayState.Recording); RefreshBufferMetrics();
                    if (recorder.UsingGraphicsCapture) stateDetail.Text = "Используется резервный захват Windows Graphics Capture.\nКадры кодируются на видеокарте.";
                    if (recorder.BufferResetOnRecovery) stateDetail.Text = "Формат экрана изменился — буфер начат заново.\nНовые кадры уже записываются.";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (CaptureRecovery.ShouldRetry(error))
                {
                    recoveryFailures++; nextRecovery = DateTime.UtcNow.AddSeconds(CaptureRecovery.RetrySeconds(recoveryFailures));
                    if (!closing && !released) { SetState("Ожидаем доступ к экрану…", TrayState.Saving); stateDetail.Text = "Повторим запуск автоматически.\nПока экран недоступен, новые кадры не записываются."; }
                }
                else fatal = error;
            }
            if (fatal != null)
            {
                try { await StopRecorder(); } catch (Exception) { }
                SetState("Не удалось восстановить запись", TrayState.Error);
            }
            busy = false; SyncActions();
            if (fatal != null && !closing && !released) ShowError(fatal.Message);
        }
        void RefreshBufferMetrics()
        {
            if (recorder == null) return;
            string currentAudio = recorder.AudioSummary; bool degraded = recorder.AudioDegraded;
            if (currentAudio != lastAudioSummary || degraded != lastAudioDegraded)
            { lastAudioSummary = currentAudio; lastAudioDegraded = degraded; UpdateOptionsSummary(); if (trayState == TrayState.Recording && !saving) SetState(state.Text, TrayState.Recording); }
            double seconds = recorder.BufferedSeconds;
            bufferValue.Text = ClockTime(seconds) + " / " + ClockTime(settings.Seconds); diskValue.Text = (recorder.DiskBytes / 1048576.0).ToString("0") + " МБ"; progress.Value = (float)(seconds / settings.Seconds); UpdateSaveAvailability(seconds);
            if (bufferReady && state.Text == "Накапливаем первые кадры…") SetState("Идёт фоновая запись", TrayState.Recording);
        }
        void PowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        { if (e.Mode == Microsoft.Win32.PowerModes.Resume && !IsDisposed && IsHandleCreated) BeginInvoke(new Action(delegate { if (recorder != null) ScheduleRecovery(); })); }
        void SessionChanged(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        { if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock && IsHandleCreated && !released) BeginInvoke(new Action(delegate { if (recoveryPending) nextRecovery = DateTime.UtcNow; })); }
        void ShowError(string message) { if (closing || released || Native.SessionEnding) return; if (testing) throw new InvalidOperationException(message); Restore(); MessageBox.Show(this, message, "ReplayLite", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        void EndWindowsSession()
        {
            if (released) return;
            Native.SetSessionEnding(true); closing = quitting = startupCanceled = true;
            recoveryPending = false; updateCancel.Cancel(); timer.Stop(); StopAnimations();
            // No asynchronous close or new process: Windows may terminate us after WM_ENDSESSION returns.
            Native.StopChildrenForShutdown();
            ReleaseUi();
        }
        async Task Quit()
        {
            if (closing) return; closing = true; updateCancel.Cancel(); timer.Stop(); StopAnimations(); SyncActions();
            while (busy) await Task.Delay(100);
            if (recorder != null) await StopRecorder();
            Native.UnregisterHotKey(Handle, 1); quitting = true; ReleaseUi(); Close();
        }
        async Task ApplyUpdate(string stage)
        {
            if (busy || saving || closing) throw new InvalidOperationException("Дождись завершения текущей операции и повтори обновление.");
            updating = true; busy = true; SyncActions();
            try
            {
                string helperFolder = Path.Combine(Path.GetTempPath(), "ReplayLite-helper-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(helperFolder);
                string helper = Path.Combine(helperFolder, "UpdateHelper.exe"); File.Copy(Path.Combine(stage, "UpdateHelper.exe"), helper);
                if (recorder != null) await StopRecorder(); settings.Save();
                Process.Start(new ProcessStartInfo(helper, Process.GetCurrentProcess().Id + " " + Recorder.Quote(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')) + " " + Recorder.Quote(stage)) { UseShellExecute = false, CreateNoWindow = true });
            }
            catch { updating = false; throw; }
            finally { busy = false; if (!released) SyncActions(); }
        }
        async Task RefreshDisplayAsync()
        {
            if (Native.SessionEnding) return;
            if (monitorRefresh) return; monitorRefresh = true;
            try { while (busy && !closing && !released && !Native.SessionEnding) await Task.Delay(100); if (released || closing || Native.SessionEnding) return; RefreshMonitors(); UpdateOptionsSummary(); if (recorder != null) ScheduleRecovery(); }
            finally { monitorRefresh = false; }
        }
        void ReleaseUi()
        {
            if (released) return; released = true; updateCancel.Cancel(); updateCancel.Dispose(); timer.Stop(); timer.Dispose(); StopAnimations();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch -= SessionChanged;
            if (IsHandleCreated) Native.UnregisterHotKey(Handle, 1);
            tray.Visible = false; var menu = tray.ContextMenuStrip; tray.Dispose(); if (menu != null) menu.Dispose(); tips.Dispose();
            Icon = null; foreach (var icon in icons) icon.Dispose(); brand.Dispose();
        }
        protected override void Dispose(bool disposing) { if (disposing) ReleaseUi(); base.Dispose(disposing); }
        protected override async void OnShown(EventArgs e)
        { base.OnShown(e); RegisterConfiguredHotkey(); if (!testing && !startInTray) await CheckForUpdates(); }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0011)
            { Native.SetSessionEnding(true); message.Result = new IntPtr(1); return; }
            if (message.Msg == 0x0016)
            {
                if (message.WParam != IntPtr.Zero) EndWindowsSession();
                else { Native.SetSessionEnding(false); if (recorder != null) ScheduleRecovery(); }
                message.Result = IntPtr.Zero; return;
            }
            if (message.Msg == 0x0312 && message.WParam.ToInt32() == 1) { if (!hotkey.IsCapturing) { var pending = SaveReplay(); } return; }
            if (message.Msg == 0x007e && IsHandleCreated && !released) BeginInvoke(new Action(async delegate { await RefreshDisplayAsync(); }));
            base.WndProc(ref message);
        }
        internal void VerifyControls()
        { if (duration.SelectedIndex < 0 || fps.SelectedIndex < 0 || sound.SelectedIndex < 0) throw new InvalidOperationException("A default selection was lost."); }
        internal void PreviewForDocs() { testing = true; folder.Text = Settings.Portable ? @"Replays\" : @"%USERPROFILE%\Videos\ReplayLite"; }
        internal void PreviewSettingsForDocs() { PreviewForDocs(); ShowSettings(true); }
        async Task WaitForAnimationsAsync()
        {
            var timeout = Stopwatch.StartNew(); long settledSince = -1;
            while (timeout.ElapsedMilliseconds < 3000)
            {
                if (ActiveAnimations == 0)
                {
                    if (settledSince < 0) settledSince = timeout.ElapsedMilliseconds;
                    if (timeout.ElapsedMilliseconds - settledSince >= 100) return;
                }
                else settledSince = -1;
                await Task.Delay(25);
            }
            throw new TimeoutException("UI animations did not settle within 3 seconds.");
        }
        internal async Task VerifyUiAsync(string target)
        {
            testing = true; VerifyControls(); var report = new System.Text.StringBuilder();
            if (settingsPage.Visible || !recordingPage.Visible || versionLabel.Text != "v" + appVersion || !Text.EndsWith(appVersion)) throw new Exception("Default page or application version missing.");
            Size originalSize = ClientSize;
            ShowSettings(true);
            if (!settingsPage.Visible || recordingPage.Visible) throw new Exception("Settings navigation failed.");
            if (ClientSize != originalSize || recordingPage.Bounds != settingsPage.Bounds) throw new Exception("Changing tabs resized the window.");
            VerifyControlBoundsForTest(settingsPage); ShowSettings(false); VerifyControlBoundsForTest(recordingPage);
            if (!cpuValue.Visible || !ramValue.Visible || !bufferValue.Visible || !diskValue.Visible) throw new Exception("Recording metrics were hidden.");
            ShowSettings(true);
            report.AppendLine("VERSION_AND_SETTINGS_NAVIGATION: PASS");
            report.AppendLine("COMPACT_FIXED_SIZE_TABS_AND_FOUR_METRICS: PASS");
            foreach (var select in new[] { duration, fps, sound, display })
            {
                int initial = select.SelectedIndex;
                select.TogglePopup(); await Task.Delay(25); if (!select.PopupForTest.Visible) throw new Exception("First dropdown click did not open.");
                select.TogglePopup(); await Task.Delay(25); if (select.PopupForTest.Visible || select.SelectedIndex != initial) throw new Exception("Second dropdown click did not close without changing selection.");
                for (int i = 0; i < 5; i++) { select.Open(); await Task.Delay(25); VerifyMenuForTest(select.PopupForTest, false); select.PickForTest((initial + 1) % select.Items.Count); await Task.Delay(25); if (select.SelectedIndex != (initial + 1) % select.Items.Count) throw new Exception("Dropdown selection failed."); }
                select.SelectedIndex = initial;
            }
            report.AppendLine("DROPDOWN_REOPEN_AND_PICK_20_TIMES: PASS");
            report.AppendLine("DROPDOWN_FULL_WIDTH_HIGHLIGHT_AND_ROUND_CORNERS: PASS");
            report.AppendLine("DROPDOWN_SECOND_CLICK_CLOSES: PASS");
            VerifyStartupForTest(); report.AppendLine("STARTUP_REGISTRY_QUOTING_ENABLE_DISABLE_OWNERSHIP: PASS");
            using (var launch = new MainForm(true, true))
            {
                int shown = 0; launch.VisibleChanged += delegate { if (launch.Visible) shown++; };
                await launch.StartInBackgroundAsync(false);
                if (shown != 0 || launch.Visible || !launch.IsHandleCreated || !launch.tray.Visible) throw new Exception("Background initialization showed a window.");
                launch.folder.Text = ""; await launch.StartInBackgroundAsync();
                if (shown != 0 || launch.Visible || launch.busy || launch.recorder != null || launch.trayState != TrayState.Error) throw new Exception("Failed automatic capture showed a window or left startup busy.");
                launch.recorder = new Recorder(new Settings()); launch.ScheduleRecovery();
                if (!launch.start.Enabled || launch.save.Enabled || !launch.recoveryPending || launch.start.Text != "Остановить запись") throw new Exception("Recovery stop command unavailable.");
                await launch.Toggle(); if (launch.recorder != null || launch.recoveryPending || launch.busy) throw new Exception("Manual stop did not cancel pending recovery.");
                launch.recorder = new Recorder(new Settings()); launch.recorder.Failure = "[Parsed_ddagrab_0] Desktop duplication access denied";
                launch.saving = true; await launch.RefreshState(); launch.saving = false;
                if (!launch.recoveryPending || launch.busy || launch.recorder == null || launch.Visible) throw new Exception("Text-only duplication denial was not queued for quiet recovery.");
                await launch.Toggle();
                launch.Restore(); if (!launch.Visible) throw new Exception("Background window cannot be restored.");
            }
            report.AppendLine("STARTUP_INITIALIZATION_NO_VISIBLE_TRANSITIONS_AND_RESTORE: PASS");
            report.AppendLine("STARTUP_FAILURE_RETRIES_STAY_HIDDEN: PASS");
            report.AppendLine("RECOVERY_PENDING_CAN_BE_STOPPED: PASS");
            report.AppendLine("TEXT_ONLY_ACCESS_DENIED_QUEUES_RECOVERY_WITHOUT_MODAL: PASS");
            using (var policy = new MainForm(true, true))
            {
                policy.recorder = new Recorder(new Settings()); policy.ScheduleRecovery(); var deadline = policy.nextRecovery;
                policy.ScheduleRecovery(); if (policy.nextRecovery != deadline) throw new Exception("Display event storm bypasses recovery backoff.");
                policy.recoveryPending = false; policy.recoveryHealthySince = DateTime.UtcNow; policy.ScheduleRecovery();
                if (policy.nextRecovery < DateTime.UtcNow.AddSeconds(1) || policy.recoveryFailures != 1) throw new Exception("Short-lived capture success reset failure backoff.");
                policy.recoveryPending = false; policy.recoveryHealthySince = DateTime.UtcNow.AddSeconds(-31); policy.ScheduleRecovery();
                if (policy.recoveryFailures != 0 || policy.nextRecovery > DateTime.UtcNow.AddSeconds(1)) throw new Exception("Stable capture did not reset backoff.");
            }
            report.AppendLine("FLAPPING_AND_DISPLAY_EVENT_STORMS_KEEP_BOUNDED_BACKOFF: PASS");
            using (var shutdown = new MainForm(true, true))
            {
                try
                {
                    var handle = shutdown.Handle; shutdown.recorder = new Recorder(new Settings()); shutdown.ScheduleRecovery();
                    var query = Message.Create(handle, 0x0011, IntPtr.Zero, IntPtr.Zero); shutdown.WndProc(ref query);
                    await shutdown.RefreshState(); await shutdown.RefreshDisplayAsync(); await shutdown.Toggle(); await shutdown.SaveReplay();
                    shutdown.ShowError("Must stay quiet at shutdown");
                    if (query.Result != new IntPtr(1) || !Native.SessionEnding || shutdown.recorder == null || shutdown.busy || shutdown.closing || shutdown.Visible) throw new Exception("Shutdown query started capture, showed UI or exited prematurely.");
                    var canceled = Message.Create(handle, 0x0016, IntPtr.Zero, IntPtr.Zero); shutdown.WndProc(ref canceled);
                    if (Native.SessionEnding || !shutdown.recoveryPending || shutdown.closing) throw new Exception("Canceled shutdown did not permit recovery.");
                    using (var child = Recorder.Launch(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe"), new[] { "-v", "error", "-re", "-f", "lavfi", "-i", "color=s=64x64:r=1", "-t", "30", "-f", "null", "-" }, AppDomain.CurrentDomain.BaseDirectory, null))
                    {
                        var ended = Message.Create(handle, 0x0016, new IntPtr(1), IntPtr.Zero); shutdown.WndProc(ref ended);
                        if (!child.WaitForExit(2000) || !shutdown.released || !shutdown.closing || shutdown.recoveryPending || shutdown.timer.Enabled || shutdown.tray.Visible) throw new Exception("Confirmed shutdown kept media process or UI alive.");
                    }
                }
                finally { Native.SetSessionEnding(false); }
            }
            report.AppendLine("SHUTDOWN_QUERY_BLOCKS_CAPTURE_AND_ERRORS_CANCEL_RESUMES: PASS");
            report.AppendLine("CONFIRMED_SHUTDOWN_STOPS_CHILDREN_AND_TRAY_SYNCHRONOUSLY: PASS");
            var newer = new ReleaseInfo { Version = new Version(99, 1, 2, 0) };
            if (!SetAvailableRelease(newer) || !trayUpdate.Available || !update.Text.Contains("99.1.2") || SetAvailableRelease(newer)) throw new Exception("Update notification missing or repeated.");
            var updateMenu = (RoundedMenu)tray.ContextMenuStrip; updateMenu.Show(this, new Point(30, 100)); await Task.Delay(25); VerifyMenuForTest(updateMenu, true, 6); updateMenu.Close();
            if (SetAvailableRelease(new ReleaseInfo { Version = typeof(MainForm).Assembly.GetName().Version }) || trayUpdate.Available || availableRelease != null) throw new Exception("Current version offered as an update.");
            report.AppendLine("UPDATE_NOTICE_NEW_VERSION_ONLY_NO_DUPLICATE: PASS");
            var trayMenu = (RoundedMenu)tray.ContextMenuStrip;
            trayMenu.Show(this, new Point(30, 100)); await Task.Delay(25);
            VerifyMenuForTest(trayMenu, true); trayMenu.Close();
            report.AppendLine("TRAY_MENU_DARK_MARGIN_AND_ACTION_ICONS: PASS");
            if (monitors.Count > 0 && !(display.Items[0] is MonitorChoice)) throw new Exception("Monitor choices are not mapped to native outputs.");
            report.AppendLine("MONITOR_CHOICES_NATIVE_MAPPING: PASS");
            VerifyHotkeyForTest(); report.AppendLine("HOTKEY_PERSIST_CANCEL_COLLISION: PASS");
            VerifyCaptureOptionsForTest(); report.AppendLine("CAPTURE_OPTIONS_AUTOSAVE_AND_SUMMARY: PASS");
            hotkey.BeginCapture(); ShowSettings(false);
            if (hotkey.IsCapturing || hotkey.CaptureHookActive) throw new Exception("Page navigation kept keyboard capture active.");
            await VerifyReplayActionsForTest(); report.AppendLine("SYNTHETIC_UI_EMPTY_BUFFER_TRAY_SAVE_AND_REFILL: PASS");
            report.AppendLine("CAPTURE_FAILURE_RELEASES_BUSY_AND_START_CONTROLS: PASS");
            start.PreviewHover(true); progress.Value = .65f; SetState("Буфер включён", TrayState.Recording);
            await WaitForAnimationsAsync();
            if (ActiveAnimations != 0 || trayState != TrayState.Recording) throw new Exception("Animation did not settle or recording tray state was lost.");
            report.AppendLine("TRANSITIONS_SETTLE: PASS");
            SetState("Сохраняю повтор…", TrayState.Saving); await WaitForAnimationsAsync();
            if (trayState != TrayState.Saving || ActiveAnimations != 0) throw new Exception("Save status failed.");
            report.AppendLine("SAVING_TRAY_STATE: PASS");
            SetState("Ошибка сохранения", TrayState.Error); await WaitForAnimationsAsync();
            if (trayState != TrayState.Error) throw new Exception("Error tray state failed.");
            start.PreviewHover(false); progress.Value = .9f; ShowSettings(true); hotkey.BeginCapture(); Close();
            if (Visible || IsDisposed || !tray.Visible) throw new Exception("Closing the window did not keep the application in the tray.");
            report.AppendLine("CLOSE_BUTTON_HIDES_TO_TRAY: PASS");
            if (ActiveAnimations != 0) throw new Exception("Hidden animations still running.");
            if (hotkey.IsCapturing || hotkey.CaptureHookActive) throw new Exception("Hidden window kept keyboard capture active.");
            report.AppendLine("HIDDEN_CAPTURE_HOOK_REMOVED: PASS");
            report.AppendLine("HIDDEN_ANIMATION_TIMERS_ZERO: PASS");
            var process = Process.GetCurrentProcess(); var cpuStart = process.TotalProcessorTime.TotalSeconds; var idle = Stopwatch.StartNew(); await Task.Delay(2000); process.Refresh();
            report.AppendLine("HIDDEN_IDLE_CPU_SECONDS: " + (process.TotalProcessorTime.TotalSeconds - cpuStart).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
            Restore(); ShowSettings(false); start.PreviewHover(false); progress.Value = 0; SetState("Готово к записи"); await WaitForAnimationsAsync();
            if (trayState != TrayState.Idle || ActiveAnimations != 0) throw new Exception("Idle state failed.");
            report.AppendLine("IDLE_TRAY_STATE: PASS");
            File.WriteAllText(target, report.ToString(), new System.Text.UTF8Encoding(false));
        }
        static void VerifyControlBoundsForTest(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (!control.Visible) continue;
                if (!parent.ClientRectangle.Contains(control.Bounds)) throw new Exception("Control extends beyond its container: " + control.Text);
                VerifyControlBoundsForTest(control);
            }
        }
        static void VerifyStartupForTest()
        {
            string registryPath = @"Software\ReplayLite\StartupTests\" + Guid.NewGuid().ToString("N");
            string executable = Path.Combine(Path.GetTempPath(), "ReplayLite test space", "ReplayLite.exe");
            try
            {
                if (StartupRegistration.Enabled(executable, registryPath)) throw new Exception("Startup enabled by default.");
                StartupRegistration.SetEnabled(executable, true, registryPath);
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryPath)) if ((string)key.GetValue("ReplayLite") != "\"" + executable + "\" --startup") throw new Exception("Startup path is not quoted.");
                if (!StartupRegistration.Enabled(executable, registryPath)) throw new Exception("Startup registration not persisted.");
                StartupRegistration.SetEnabled(executable + ".other", false, registryPath);
                if (!StartupRegistration.Enabled(executable, registryPath)) throw new Exception("Another copy removed startup.");
                StartupRegistration.SetEnabled(executable, false, registryPath);
                if (StartupRegistration.Enabled(executable, registryPath)) throw new Exception("Startup registration not removed.");
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, false); }
        }
        void VerifyCaptureOptionsForTest()
        {
            string file = Path.Combine(Settings.Home, "settings.txt"); bool existed = File.Exists(file); byte[] previous = existed ? File.ReadAllBytes(file) : null;
            int oldDuration = duration.SelectedIndex, oldFps = fps.SelectedIndex, oldAudio = sound.SelectedIndex, oldRate = rate.Value;
            try
            {
                testing = false; duration.SelectedIndex = (oldDuration + 1) % duration.Items.Count; fps.SelectedIndex = 1 - oldFps; sound.SelectedIndex = (oldAudio + 1) % sound.Items.Count; rate.Value = oldRate == 8 ? 10 : 8;
                var loaded = Settings.Load();
                if (loaded.Seconds != SelectedSeconds || loaded.Fps != (fps.SelectedIndex == 1 ? 60 : 30) || loaded.Mbps != rate.Value || loaded.Audio != new[] { "both", "system", "none" }[sound.SelectedIndex]) throw new Exception("Capture options were not saved automatically.");
                if (!videoSummary.Text.Contains(duration.Text) || !save.Detail.Contains(duration.Text) || !videoSummary.Text.Contains(sound.Text)) throw new Exception("Capture options did not update the recording summary.");
            }
            finally
            {
                testing = true; duration.SelectedIndex = oldDuration; fps.SelectedIndex = oldFps; sound.SelectedIndex = oldAudio; rate.Value = oldRate; ReadCaptureOptions();
                if (existed) File.WriteAllBytes(file, previous); else if (File.Exists(file)) File.Delete(file);
            }
        }
        async Task VerifyReplayActionsForTest()
        {
            string root = Path.Combine(Path.GetTempPath(), "ReplayLite-ui-buffer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var config = new Settings { Seconds = settings.Seconds, Audio = "none", Folder = root }; Exception failure = null;
            try
            {
                var ffmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe");
                var process = Recorder.Launch(ffmpeg, new[] { "-v", "error", "-re", "-f", "lavfi", "-i", "testsrc2=size=64x64:rate=5", "-c:v", "libx264", "-preset", "ultrafast", "-g", "10", "-bf", "0", "-f", "segment", "-segment_format", "mpegts", "-segment_time", "2", "-segment_list", "segments.csv", "-segment_list_type", "csv", "-reset_timestamps", "1", "s%09d.ts" }, root, null);
                recorder = new Recorder(config); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(Recorder).GetField("session", flags).SetValue(recorder, root); typeof(Recorder).GetField("process", flags).SetValue(recorder, process);
                Inputs(false); UpdateSaveAvailability(0); SetState("Идёт фоновая запись", TrayState.Recording);
                await SaveReplay(); if (lastReplay != null || save.Enabled || traySave.Enabled) throw new Exception("Empty replay was offered for saving.");
                var timeout = Stopwatch.StartNew(); while (recorder.BufferedSeconds <= 0 && timeout.ElapsedMilliseconds < 6000) await Task.Delay(100);
                if (recorder.BufferedSeconds <= 0) throw new Exception("Synthetic UI buffer did not fill.");
                Hide(); UpdateSaveAvailability(0); tray.ContextMenuStrip.Show(new Point(40, 40));
                if (!traySave.Enabled) throw new Exception("Hidden window left the tray save command disabled.");
                tray.ContextMenuStrip.Close(); Restore(); await SaveReplay();
                if (lastReplay == null || !File.Exists(lastReplay) || !openLast.Enabled || !recorder.Running) throw new Exception("Saving did not expose the replay or continue capture.");
                if (save.Enabled != (recorder.BufferedSeconds > 0)) throw new Exception("Save availability did not follow buffer reset.");
                timeout.Restart(); while (recorder.BufferedSeconds <= 0 && timeout.ElapsedMilliseconds < 6000) await Task.Delay(100);
                RefreshBufferMetrics(); if (!save.Enabled || !traySave.Enabled) throw new Exception("Saving did not become available after refill.");
                try { await process.StandardInput.WriteLineAsync("q"); } catch (IOException) { }
                if (!await Task.Run(() => process.WaitForExit(5000))) throw new Exception("Fixture process did not stop."); process.WaitForExit();
                saving = true; await RefreshState(); saving = false;
                if (!recoveryPending || recorder == null || busy) throw new Exception("Unknown capture exit showed an error or discarded recorder.");
                recoveryPending = false; recorder.Failure = "Synthetic capture failure"; bool reported = false;
                try { await RefreshState(); } catch (InvalidOperationException error) { reported = error.Message.Contains("Synthetic capture failure"); }
                if (!reported || busy || recorder != null || start.Enabled != SelectedMonitorSupported || !duration.Enabled || !browse.Enabled) throw new Exception("Capture failure left the start controls locked.");
            }
            catch (Exception error) { failure = error; }
            busy = true; await StopRecorder(); busy = false; lastReplay = null; openLast.Enabled = false; SetState("Готово к записи"); Deployment.Cleanup(root);
            if (failure != null) throw failure;
        }
        static void VerifyMenuForTest(RoundedMenu menu, bool withIcons, int expectedCommands = 5)
        {
            if (!menu.Visible || menu.Region == null || menu.Region.IsVisible(0, 0) || !menu.Region.IsVisible(menu.Width / 2, 1)) throw new Exception("Menu corners are not rounded.");
            ToolStripItem first = null, last = null; int count = 0;
            foreach (ToolStripItem item in menu.Items)
            {
                if (!(item is ToolStripMenuItem) || !item.Available) continue;
                if (item.Bounds.Right < menu.ClientSize.Width - 2) throw new Exception("Menu row does not span the popup: " + item.Bounds + " / " + menu.ClientSize);
                if (withIcons && item.Image == null) throw new Exception("Tray command has no icon.");
                if (first == null) first = item; last = item; count++;
            }
            if (first == null || last == null || (withIcons && count != expectedCommands)) throw new Exception("Menu commands missing.");
            first.Select(); menu.Refresh();
            using (var image = new Bitmap(menu.Width, menu.Height))
            {
                menu.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                if (image.GetPixel(menu.ClientSize.Width - 9, first.Bounds.Top + first.Height / 2).ToArgb() != DarkMenuRenderer.Selection.ToArgb()) throw new Exception("Menu highlight stops before the right edge.");
                if (withIcons && image.GetPixel(4, last.Bounds.Top + last.Height / 2).ToArgb() != Theme.Input.ToArgb()) throw new Exception("Tray image margin is not dark.");
            }
        }
        void VerifyHotkeyForTest()
        {
            string file = Path.Combine(Settings.Home, "settings.txt"); bool existed = File.Exists(file); byte[] originalFile = existed ? File.ReadAllBytes(file) : null;
            var original = settings.Hotkey; bool reserved = false;
            try
            {
                hotkey.BeginCapture();
                if (!hotkey.CaptureHookActive) throw new Exception("Editing hook was not installed.");
                if (hotkey.Propose(new KeyBinding(0, Keys.F12))) throw new Exception("Reserved F12 accepted.");
                reserved = Native.RegisterHotKey(Handle, 91, 0x4000 | 6, (uint)Keys.F11);
                if (hotkey.Propose(new KeyBinding(6, Keys.F11))) throw new Exception("Occupied hotkey accepted.");
                if (settings.Hotkey.Key != original.Key || settings.Hotkey.Modifiers != original.Modifiers) throw new Exception("Failed binding overwrote settings.");
                if (reserved) { Native.UnregisterHotKey(Handle, 91); reserved = false; }
                hotkey.CancelCapture();
                if (hotkey.Binding.Key != original.Key) throw new Exception("Cancel did not restore binding.");
                hotkey.BeginCapture();
                if (!hotkey.Propose(new KeyBinding(6, Keys.F10))) throw new Exception("Test hotkey could not be registered.");
                var stored = Settings.Load();
                if (stored.Hotkey.Key != Keys.F10 || stored.Hotkey.Modifiers != 6 || !save.Detail.Contains(stored.Hotkey.ToString()) || !traySave.Text.Contains(stored.Hotkey.ToString())) throw new Exception("Binding did not persist or update labels.");
                foreach (var binding in new[] { new KeyBinding(0, Keys.F10), new KeyBinding(1, Keys.F10), new KeyBinding(4, Keys.F10), new KeyBinding(8, Keys.F10), new KeyBinding(2, Keys.Insert), new KeyBinding(0, Keys.NumPad8), new KeyBinding(0, Keys.Tab), new KeyBinding(0, Keys.Escape) })
                {
                    if (!binding.Valid) throw new Exception("Supported binding rejected: " + binding);
                    hotkey.BeginCapture(); var previous = settings.Hotkey; bool accepted = hotkey.Propose(binding);
                    if (accepted) { var loaded = Settings.Load(); if (loaded.Hotkey.Modifiers != binding.Modifiers || loaded.Hotkey.Key != binding.Key || !save.Detail.Contains(binding.ToString()) || hotkey.CaptureHookActive) throw new Exception("Expanded binding did not persist: " + binding); }
                    else { if (settings.Hotkey.Key != previous.Key || settings.Hotkey.Modifiers != previous.Modifiers) throw new Exception("Occupied expanded binding lost the previous setting."); hotkey.CancelCapture(); }
                }
                if (new KeyBinding(16, Keys.A).Valid || new KeyBinding(0, Keys.LButton).Valid || new KeyBinding(0, Keys.ControlKey).Valid) throw new Exception("Invalid primary key accepted.");
                var captureState = new KeyCaptureState(); KeyBinding? complete;
                captureState.Feed((int)Keys.RWin, true, out complete); captureState.Feed((int)Keys.LShiftKey, true, out complete); captureState.Feed((int)Keys.Tab, true, out complete);
                captureState.Feed((int)Keys.Tab, true, out complete); if (complete.HasValue) throw new Exception("Key repeat committed capture.");
                captureState.Feed((int)Keys.LShiftKey, false, out complete); captureState.Feed((int)Keys.Tab, false, out complete); if (complete.HasValue) throw new Exception("Capture committed before modifiers were released.");
                captureState.Feed((int)Keys.RWin, false, out complete); if (!complete.HasValue || complete.Value.Modifiers != 12 || complete.Value.Key != Keys.Tab) throw new Exception("Win/Shift capture lost the original modifiers.");
                captureState = new KeyCaptureState(); captureState.Seed((int)Keys.LControlKey); captureState.Feed((int)Keys.Return, true, out complete); captureState.Feed((int)Keys.Return, false, out complete);
                if (captureState.Feed((int)Keys.LControlKey, false, out complete) || !complete.HasValue || complete.Value.Modifiers != 2) throw new Exception("Previously held modifier release was swallowed.");
                hotkey.BeginCapture(); hotkey.CancelCapture(); if (hotkey.CaptureHookActive) throw new Exception("Cancelled capture left a hook running.");
            }
            finally
            {
                if (reserved) Native.UnregisterHotKey(Handle, 91);
                hotkey.CancelCapture(); settings.Hotkey = original; RegisterConfiguredHotkey();
                if (existed) File.WriteAllBytes(file, originalFile); else if (File.Exists(file)) File.Delete(file);
            }
        }
        internal async Task VerifyBackgroundStartupAsync(string root, bool checkRecovery = false)
        {
            Directory.CreateDirectory(root); testing = true;
            string settingsFile = Path.Combine(Settings.Home, "settings.txt");
            bool existed = File.Exists(settingsFile); byte[] original = existed ? File.ReadAllBytes(settingsFile) : null;
            int shown = 0; VisibleChanged += delegate { if (Visible) shown++; };
            Exception failure = null; var report = new System.Text.StringBuilder();
            try
            {
                folder.Text = Path.Combine(root, "replays");
                await StartInBackgroundAsync();
                if (shown != 0 || Visible || !tray.Visible) throw new Exception("Startup made the window visible.");
                report.AppendLine("STARTUP_NEVER_SHOWS_WINDOW: PASS");
                if (recorder == null || !recorder.Running || recorder.BufferedSeconds <= 0) throw new Exception("Automatic capture did not start: " + stateDetail.Text);
                report.AppendLine("STARTUP_REAL_NVENC_BUFFER_AUTO_START: PASS");
                report.AppendLine("STARTUP_AUDIO_MODE: " + recorder.AudioSummary);
                var audioFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var activePipes = (List<AudioPipe>)typeof(Recorder).GetField("audio", audioFlags).GetValue(recorder);
                int audioHooks = 0; foreach (var audioPipe in activePipes) if (typeof(AudioPipe).GetField("changes", audioFlags).GetValue(audioPipe) != null) audioHooks++;
                report.AppendLine("STARTUP_AUDIO_NOTIFICATION_HOOKS: " + audioHooks);
                int pid = recorder.ProcessId;
                tray.ContextMenuStrip.Show(new Point(40, 40));
                if (!traySave.Enabled || !trayToggle.Enabled) throw new Exception("Startup tray commands unavailable.");
                tray.ContextMenuStrip.Close();
                if (shown != 0) throw new Exception("Tray menu showed the main window.");
                report.AppendLine("STARTUP_TRAY_SAVE_READY: PASS");
                if (checkRecovery)
                {
                    busy = true;
                    var capture = (Process)typeof(Recorder).GetField("process", audioFlags).GetValue(recorder);
                    try { await capture.StandardInput.WriteLineAsync("q"); } catch (IOException) { }
                    bool exited = await Task.Run(() => capture.WaitForExit(5000));
                    if (!exited) throw new Exception("Live capture did not stop for recovery test.");
                    capture.WaitForExit();
                    typeof(Recorder).GetMethod("Log", audioFlags).Invoke(recorder, new object[] { "[Parsed_ddagrab_0] DDA ReleaseFrame failed!" });
                    busy = false; await RefreshState();
                    if (recorder == null || !recorder.Running || !recorder.UsingGraphicsCapture || recoveryPending || recorder.ProcessId == pid || recorder.BufferResetOnRecovery || shown != 0) throw new Exception("Live hardware WGC recovery failed or showed a window.");
                    pid = recorder.ProcessId; report.AppendLine("LIVE_DXGI_RECOVERY_NEW_PROCESS_OLD_BUFFER_HIDDEN_WINDOW: PASS");
                    busy = true; string clip = await recorder.SaveAsync(); await CaptureRecoveryTests.Decode(clip); busy = false;
                    report.AppendLine("LIVE_WGC_WITH_SYSTEM_AND_MIC_AUDIO_EXPORT_DECODE: " + clip);
                }
                Restore();
                if (!Visible || shown != 1 || recorder.ProcessId != pid || !recorder.Running) throw new Exception("Restoring the window interrupted automatic capture.");
                report.AppendLine("STARTUP_RESTORE_PRESERVES_CAPTURE: PASS");
            }
            catch (Exception error) { failure = error; if (recorder != null) report.AppendLine(recorder.ErrorLog); }
            busy = true; if (recorder != null) await StopRecorder(); busy = false;
            if (existed) File.WriteAllBytes(settingsFile, original); else if (File.Exists(settingsFile)) File.Delete(settingsFile);
            if (failure != null) throw new Exception(report.ToString(), failure);
            File.WriteAllText(Path.Combine(root, "startup-test.txt"), report.ToString(), new System.Text.UTF8Encoding(false));
        }
        internal async Task VerifyLiveUiAsync(string root)
        {
            testing = true; Directory.CreateDirectory(root); timer.Stop();
            string settingsFile = Path.Combine(Settings.Home, "settings.txt");
            bool settingsExisted = File.Exists(settingsFile); byte[] priorSettings = settingsExisted ? File.ReadAllBytes(settingsFile) : null;
            Exception failure = null; var report = new System.Text.StringBuilder();
            try
            {
                folder.Text = root; sound.SelectedIndex = 2; duration.SelectedIndex = 0;
                await Toggle();
                if (recorder == null || !recorder.Running || trayState != TrayState.Recording || duration.Enabled) throw new Exception("Start controls or recording icon failed.");
                report.AppendLine("LIVE_START_ACTIONS_AND_TRAY: PASS");
                for (int i = 0; i < 6; i++) { await Task.Delay(1000); await RefreshState(); }
                await SaveReplay();
                if (lastReplay == null || !File.Exists(lastReplay) || !openLast.Enabled || traySave.Enabled != (recorder.BufferedSeconds > 0) || trayState != TrayState.Recording) throw new Exception("Save action or tray state failed.");
                report.AppendLine("LIVE_SAVE_ACTIONS_AND_TRAY: PASS");
                if (recorder.BufferedSeconds > 2.1 || progress.Value > 2.1f / settings.Seconds || progress.Animating) throw new Exception("Saved buffer was not reset in the UI.");
                report.AppendLine("LIVE_BUFFER_COUNTER_AND_PROGRESS_RESET: PASS");
                await Toggle();
                if (recorder != null || trayState != TrayState.Idle || traySave.Enabled || !duration.Enabled || !browse.Enabled) throw new Exception("Stop controls or idle icon failed.");
                report.AppendLine("LIVE_STOP_ACTIONS_AND_TRAY: PASS");
            }
            catch (Exception error) { failure = error; }
            if (recorder != null) await StopRecorder();
            if (settingsExisted) File.WriteAllBytes(settingsFile, priorSettings); else if (File.Exists(settingsFile)) File.Delete(settingsFile);
            if (failure != null) throw failure;
            File.WriteAllText(Path.Combine(root, "ui-live-test.txt"), report.ToString(), new System.Text.UTF8Encoding(false));
        }
    }
}
