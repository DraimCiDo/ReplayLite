using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReplayLite
{
    public sealed class MainForm : Form
    {
        readonly Settings settings = Settings.Load();
        Recorder recorder;
        readonly SelectBox duration = new SelectBox(), fps = new SelectBox(), sound = new SelectBox();
        readonly NumericUpDown rate = new NumericUpDown(), display = new NumericUpDown();
        readonly TextBox folder = new TextBox();
        readonly ActionButton start = new ActionButton(), save = new ActionButton(), browse = new ActionButton(), openLast = new ActionButton(), openFolder = new ActionButton(), toTray = new ActionButton();
        readonly Label state = new Label(), stateDetail = new Label(), hint = new Label();
        readonly Label cpuValue = new Label(), ramValue = new Label(), bufferValue = new Label(), diskValue = new Label();
        readonly StatusBadge badge = new StatusBadge(); readonly BufferBar progress = new BufferBar();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolStripMenuItem traySave = new ToolStripMenuItem(), trayToggle = new ToolStripMenuItem();
        readonly Timer timer = new Timer { Interval = 1000 };
        readonly ToolTip tips = new ToolTip();
        readonly Icon[] icons = { AppIcons.Create(TrayState.Idle), AppIcons.Create(TrayState.Recording), AppIcons.Create(TrayState.Saving), AppIcons.Create(TrayState.Error) };
        readonly Bitmap brand = AppIcons.Draw(64, TrayState.Recording);
        TrayState trayState = TrayState.Idle;
        bool busy, saving, closing, quitting, released, testing;
        double lastCpu, lastTime;
        string lastReplay;
        readonly Stopwatch clock = Stopwatch.StartNew();

        public MainForm()
        {
            Text = "ReplayLite"; ClientSize = new Size(760, 686); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Icon = icons[(int)TrayState.Recording]; DoubleBuffered = true;
            var logo = new PictureBox { Image = brand, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Background }; logo.SetBounds(24, 22, 48, 48); Controls.Add(logo);
            LabelAt(this, "ReplayLite", 83, 17, 300, 35, 21, true, Theme.Text);
            LabelAt(this, "Сохрани момент, который уже случился", 85, 56, 425, 24, 9, false, Theme.Muted);
            badge.SetBounds(580, 30, 156, 34); Controls.Add(badge);

            var recording = Card(24, 98, 712, 163);
            state.SetBounds(20, 16, 415, 33); state.Font = new Font("Segoe UI", 16, FontStyle.Bold); state.ForeColor = Theme.Text; state.BackColor = Theme.Card; state.AutoEllipsis = true; recording.Controls.Add(state);
            stateDetail.SetBounds(22, 53, 425, 25); stateDetail.Font = new Font("Segoe UI", 9); stateDetail.ForeColor = Theme.Muted; stateDetail.BackColor = Theme.Card; stateDetail.AutoEllipsis = true; recording.Controls.Add(stateDetail);
            progress.SetBounds(22, 90, 425, 6); recording.Controls.Add(progress);
            StyleButton(start, "Включить буфер", recording, 20, 110, 427, 36, false);
            StyleButton(save, "Сохранить повтор", recording, 465, 18, 226, 128, true); save.Detail = "Ctrl+Shift+F9"; save.AccessibleName = "Сохранить последние минуты, Ctrl+Shift+F9"; save.Enabled = false;
            start.Click += async delegate { await Toggle(); }; save.Click += async delegate { await SaveReplay(); };

            Metric(cpuValue, "CPU", 24, 281, "—"); Metric(ramValue, "ПАМЯТЬ", 207, 281, "—"); Metric(bufferValue, "В БУФЕРЕ", 390, 281, "0 с"); Metric(diskValue, "БУФЕР НА ДИСКЕ", 573, 281, "0 МБ");

            var options = Card(24, 375, 712, 232);
            LabelAt(options, "Настройки записи", 20, 15, 320, 26, 11, true, Theme.Text);
            LabelAt(options, "Длительность", 20, 53, 205, 22, 9, false, Theme.Muted);
            LabelAt(options, "Частота кадров", 245, 53, 220, 22, 9, false, Theme.Muted);
            LabelAt(options, "Битрейт · Мбит/с", 485, 53, 205, 22, 9, false, Theme.Muted);
            duration.Items.AddRange(new object[] { "30 секунд", "2 минуты", "5 минут", "10 минут" }); duration.SelectedIndex = settings.Seconds == 30 ? 0 : settings.Seconds == 300 ? 2 : settings.Seconds == 600 ? 3 : 1;
            fps.Items.AddRange(new object[] { "30 FPS · экономнее", "60 FPS · плавнее" }); fps.SelectedIndex = settings.Fps == 60 ? 1 : 0;
            rate.Minimum = 2; rate.Maximum = 30; rate.Value = settings.Mbps;
            AddInput(options, duration, 20, 77, 205, "Длительность повтора"); AddInput(options, fps, 245, 77, 220, "Частота кадров"); AddInput(options, rate, 485, 77, 205, "Битрейт");
            LabelAt(options, "Звук", 20, 129, 450, 22, 9, false, Theme.Muted); LabelAt(options, "Экран", 485, 129, 205, 22, 9, false, Theme.Muted);
            sound.Items.AddRange(new object[] { "Компьютер + микрофон", "Только компьютер", "Без звука" }); sound.SelectedIndex = settings.Audio == "none" ? 2 : settings.Audio == "system" ? 1 : 0;
            display.Minimum = 0; display.Maximum = 7; display.Value = settings.Display;
            AddInput(options, sound, 20, 153, 445, "Источники звука"); AddInput(options, display, 485, 153, 205, "Индекс экрана");
            LabelAt(options, "Настройки доступны, когда буфер остановлен", 22, 202, 660, 20, 8, false, Theme.Muted);
            tips.SetToolTip(display, "Индекс выхода видеокарты: 0 — первый. Порядок может отличаться от номеров в Windows.");

            folder.Text = settings.Folder; folder.ReadOnly = true; folder.BorderStyle = BorderStyle.None; folder.BackColor = Theme.Background; folder.ForeColor = Theme.Muted; folder.Font = new Font("Segoe UI", 8.5f); folder.SetBounds(29, 658, 700, 19); folder.TabStop = false; Controls.Add(folder);
            StyleButton(openFolder, "Папка повторов", this, 24, 623, 155, 30, false); openFolder.Click += delegate { OpenFolder(); };
            StyleButton(openLast, "Последний повтор", this, 189, 623, 179, 30, false); openLast.Enabled = false; openLast.Click += delegate { if (lastReplay != null && File.Exists(lastReplay)) Process.Start("explorer.exe", Recorder.Quote(lastReplay)); };
            StyleButton(browse, "Сменить папку", this, 378, 623, 155, 30, false);
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog { SelectedPath = folder.Text, Description = "Папка для сохранённых MP4" }) if (dialog.ShowDialog() == DialogResult.OK) { folder.Text = dialog.SelectedPath; settings.Folder = folder.Text; settings.Save(); } };
            StyleButton(toTray, "Свернуть в трей", this, 553, 623, 183, 30, false); toTray.Click += delegate { Hide(); };
            hint.SetBounds(27, 355, 706, 18); hint.Font = new Font("Segoe UI", 8); hint.ForeColor = Theme.Muted; hint.Text = "Записывается весь экран. После закрытия окна запись продолжается в трее."; Controls.Add(hint);

            tray.Icon = icons[0]; tray.Text = "ReplayLite — буфер выключен"; tray.Visible = true;
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), BackColor = Theme.Input, ForeColor = Theme.Text, Font = Font };
            menu.Items.Add("Открыть ReplayLite", null, delegate { Restore(); }); menu.Items.Add(new ToolStripSeparator());
            traySave.Text = "Сохранить повтор   Ctrl+Shift+F9"; traySave.Enabled = false; traySave.Click += async delegate { await SaveReplay(); }; menu.Items.Add(traySave);
            trayToggle.Text = "Включить буфер"; trayToggle.Click += async delegate { await Toggle(); }; menu.Items.Add(trayToggle);
            menu.Items.Add("Папка повторов", null, delegate { OpenFolder(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Выход", null, async delegate { await Quit(); }); tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Restore(); };
            SetState("Буфер выключен");
            timer.Tick += async delegate { await RefreshState(); }; timer.Start();
            VisibleChanged += delegate { if (!Visible) StopAnimations(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) StopAnimations(); };
            FormClosing += async delegate(object sender, FormClosingEventArgs e) { if (quitting) return; e.Cancel = true; if (e.CloseReason == CloseReason.WindowsShutDown) { await Quit(); return; } Hide(); };
            Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChanged;
        }

        CardPanel Card(int x, int y, int width, int height) { var card = new CardPanel(); card.SetBounds(x, y, width, height); Controls.Add(card); return card; }
        Label LabelAt(Control parent, string text, int x, int y, int width, int height, float size, bool bold, Color color)
        { var label = new Label { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = parent == this ? Theme.Background : Theme.Card, AutoEllipsis = true }; label.SetBounds(x, y, width, height); parent.Controls.Add(label); return label; }
        void Metric(Label value, string text, int x, int y, string initial)
        { var card = Card(x, y, 163, 65); LabelAt(card, text, 14, 10, 139, 17, 7.5f, false, Theme.Muted); value.SetBounds(12, 29, 139, 29); value.Font = new Font("Segoe UI", 14, FontStyle.Bold); value.ForeColor = Theme.Text; value.BackColor = Theme.Card; value.Text = initial; card.Controls.Add(value); }
        void AddInput(Control parent, Control control, int x, int y, int width, string name)
        {
            control.AccessibleName = name;
            var numeric = control as NumericUpDown;
            if (numeric == null) { control.SetBounds(x, y, width, 40); parent.Controls.Add(control); return; }
            var shell = new Panel { BackColor = Theme.Input }; shell.SetBounds(x, y, width, 40); parent.Controls.Add(shell);
            numeric.BorderStyle = BorderStyle.None; numeric.BackColor = Theme.Input; numeric.ForeColor = Theme.Text; numeric.Font = Font; numeric.SetBounds(12, 10, width - 23, 25); shell.Controls.Add(numeric);
        }
        void StyleButton(ActionButton button, string text, Control parent, int x, int y, int width, int height, bool accent)
        { button.Text = text; button.SetBounds(x, y, width, height); button.BackColor = parent == this ? Theme.Background : Theme.Card; button.Font = new Font("Segoe UI", 10, FontStyle.Bold); button.ForeColor = accent ? Color.FromArgb(9, 37, 29) : Theme.Text; button.Fill = accent ? Theme.Accent : Theme.Input; parent.Controls.Add(button); }
        void StopAnimations() { foreach (var button in new[] { start, save, browse, openFolder, openLast, toTray }) button.FinishAnimation(); progress.FinishAnimation(); badge.FinishAnimation(); }
        int ActiveAnimations { get { int count = 0; foreach (var button in new[] { start, save, browse, openFolder, openLast, toTray }) if (button.Animating) count++; return count + (progress.Animating ? 1 : 0) + (badge.Animating ? 1 : 0); } }
        void SetState(string text, TrayState? explicitState = null)
        {
            var status = explicitState ?? (recorder != null && recorder.Running ? TrayState.Recording : TrayState.Idle);
            state.Text = text.StartsWith("Сохранено:") ? "Повтор сохранён" : text;
            stateDetail.Text = text.StartsWith("Сохранено:") ? text.Substring(10).Trim() : status == TrayState.Saving ? "Подождите немного…" : status == TrayState.Recording ? "Момент уже в буфере. Сохрани его одной клавишей." : status == TrayState.Error ? "Открой сообщение об ошибке и проверь настройки." : "Включи буфер, чтобы сохранять последние минуты.";
            badge.Set(status == TrayState.Recording ? "Идёт запись" : status == TrayState.Saving ? "Подождите…" : status == TrayState.Error ? "Ошибка" : "Выключено", status == TrayState.Recording ? Theme.Accent : status == TrayState.Saving ? Theme.Blue : status == TrayState.Error ? Theme.Red : Theme.Muted);
            if (trayState != status) { tray.Icon = icons[(int)status]; trayState = status; }
            var tooltip = "ReplayLite — " + text; tray.Text = tooltip.Substring(0, Math.Min(63, tooltip.Length));
            SyncActions();
        }
        void SyncActions()
        {
            bool active = recorder != null && recorder.Running;
            start.Enabled = !busy && !closing; start.Text = active ? "Остановить буфер" : "Включить буфер";
            start.Fill = active ? Color.FromArgb(63, 39, 49) : Theme.Input; start.ForeColor = active ? Theme.Red : Theme.Text;
            save.Enabled = active && !busy && !saving && !closing; traySave.Enabled = save.Enabled;
            trayToggle.Enabled = start.Enabled; trayToggle.Text = start.Text;
        }
        void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        void OpenFolder() { Directory.CreateDirectory(settings.Folder); Process.Start("explorer.exe", Recorder.Quote(settings.Folder)); }
        void Inputs(bool enabled) { foreach (var control in new Control[] { duration, fps, sound, rate, display, browse }) control.Enabled = enabled; }
        async Task Toggle()
        {
            if (busy || closing) return;
            busy = true; SyncActions(); Exception error = null;
            try
            {
                if (recorder != null) { SetState("Останавливаю буфер…", TrayState.Saving); await StopRecorder(); SetState("Буфер выключен"); }
                else
                {
                    if (!Path.IsPathRooted(folder.Text)) throw new ArgumentException("Укажи абсолютный путь папки повторов.");
                    settings.Seconds = new[] { 30, 120, 300, 600 }[duration.SelectedIndex]; settings.Fps = fps.SelectedIndex == 1 ? 60 : 30; settings.Mbps = (int)rate.Value; settings.Display = (int)display.Value;
                    settings.Audio = new[] { "both", "system", "none" }[sound.SelectedIndex]; settings.Folder = folder.Text; settings.Save(); Inputs(false); SetState("Запускаю буфер…", TrayState.Saving);
                    recorder = new Recorder(settings); await recorder.StartAsync(); lastCpu = Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds + recorder.CpuSeconds; lastTime = clock.Elapsed.TotalSeconds; SetState("Буфер включён", TrayState.Recording);
                }
            }
            catch (Exception e) { error = e; }
            if (error != null) { if (recorder != null) await StopRecorder(); SetState("Не удалось включить буфер", TrayState.Error); ShowError(error.Message); }
            busy = false; SyncActions();
        }
        async Task StopRecorder()
        {
            var current = recorder; if (current == null) return; save.Enabled = traySave.Enabled = false;
            await current.StopAsync(); current.Dispose(); recorder = null; Inputs(true);
            progress.Value = 0; bufferValue.Text = "0 с"; diskValue.Text = "0 МБ"; cpuValue.Text = ramValue.Text = "—";
        }
        async Task SaveReplay()
        {
            if (recorder == null || busy || saving || closing) return;
            saving = true; SetState("Сохраняю повтор…", TrayState.Saving);
            try { var output = await recorder.SaveAsync(); lastReplay = output; openLast.Enabled = true; SetState("Сохранено: " + Path.GetFileName(output)); if (!testing) tray.ShowBalloonTip(3000, "Повтор сохранён", output, ToolTipIcon.Info); }
            catch (Exception e) { ShowError(e.Message); SetState("Ошибка сохранения", TrayState.Error); }
            finally { saving = false; SyncActions(); }
        }
        async Task RefreshState()
        {
            if (recorder == null || busy || closing) return;
            if (recorder.Failure != null || !recorder.Running)
            { busy = true; string message = recorder.Failure ?? ("Запись остановлена.\n" + recorder.ErrorLog); await StopRecorder(); SetState("Захват остановлен", TrayState.Error); ShowError(message); busy = false; SyncActions(); return; }
            // Health monitoring stays active in the tray; hidden UI skips metrics and all painting.
            if (!Visible || WindowState == FormWindowState.Minimized) return;
            try
            {
                var process = Process.GetCurrentProcess(); process.Refresh(); double total = process.TotalProcessorTime.TotalSeconds + recorder.CpuSeconds, now = clock.Elapsed.TotalSeconds;
                double cpu = Math.Max(0, (total - lastCpu) / Math.Max(.01, now - lastTime) / Environment.ProcessorCount * 100); lastCpu = total; lastTime = now;
                double seconds = recorder.BufferedSeconds;
                cpuValue.Text = cpu.ToString("0.0") + "%"; ramValue.Text = ((process.WorkingSet64 + recorder.Memory) / 1048576.0).ToString("0") + " МБ";
                bufferValue.Text = string.Format("{0:0} / {1} с", seconds, settings.Seconds); diskValue.Text = (recorder.DiskBytes / 1048576.0).ToString("0") + " МБ"; progress.Value = (float)(seconds / settings.Seconds);
            }
            catch (Exception e) { SetState("Ошибка состояния", TrayState.Error); stateDetail.Text = e.Message; }
        }
        void PowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        { if (e.Mode == Microsoft.Win32.PowerModes.Resume && !IsDisposed) BeginInvoke(new Action(async delegate { if (recorder != null && !busy && !closing) { busy = true; try { await StopRecorder(); SetState("После сна включи буфер снова"); } finally { busy = false; SyncActions(); } } })); }
        void ShowError(string message) { if (testing) throw new InvalidOperationException(message); Restore(); MessageBox.Show(this, message, "ReplayLite", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        async Task Quit()
        {
            if (closing) return; closing = true; timer.Stop(); StopAnimations(); SyncActions();
            while (busy) await Task.Delay(100);
            if (recorder != null) await StopRecorder();
            Native.UnregisterHotKey(Handle, 1); quitting = true; ReleaseUi(); Close();
        }
        void ReleaseUi()
        {
            if (released) return; released = true; timer.Stop(); timer.Dispose(); StopAnimations();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChanged;
            if (IsHandleCreated) Native.UnregisterHotKey(Handle, 1);
            tray.Visible = false; var menu = tray.ContextMenuStrip; tray.Dispose(); if (menu != null) menu.Dispose(); tips.Dispose();
            Icon = null; foreach (var icon in icons) icon.Dispose(); brand.Dispose();
        }
        protected override void Dispose(bool disposing) { if (disposing) ReleaseUi(); base.Dispose(disposing); }
        protected override void OnShown(EventArgs e)
        { base.OnShown(e); if (!Native.RegisterHotKey(Handle, 1, 0x4000 | 0x0002 | 0x0004, (uint)Keys.F9)) hint.Text = "Ctrl+Shift+F9 занята другой программой. Используй кнопку сохранения."; }
        protected override void WndProc(ref Message message)
        { if (message.Msg == 0x0312 && message.WParam.ToInt32() == 1) { var pending = SaveReplay(); return; } base.WndProc(ref message); }
        internal void VerifyControls()
        { if (duration.SelectedIndex < 0 || fps.SelectedIndex < 0 || sound.SelectedIndex < 0) throw new InvalidOperationException("A default selection was lost."); }
        internal async Task VerifyUiAsync(string target)
        {
            testing = true; VerifyControls(); var report = new System.Text.StringBuilder();
            start.PreviewHover(true); progress.Value = .65f; SetState("Буфер включён", TrayState.Recording);
            await Task.Delay(350);
            if (ActiveAnimations != 0 || trayState != TrayState.Recording) throw new Exception("Animation did not settle or recording tray state was lost.");
            report.AppendLine("TRANSITIONS_SETTLE: PASS");
            SetState("Сохраняю повтор…", TrayState.Saving); await Task.Delay(250);
            if (trayState != TrayState.Saving || ActiveAnimations != 0) throw new Exception("Save status failed.");
            report.AppendLine("SAVING_TRAY_STATE: PASS");
            SetState("Ошибка сохранения", TrayState.Error); await Task.Delay(250);
            if (trayState != TrayState.Error) throw new Exception("Error tray state failed.");
            start.PreviewHover(false); progress.Value = .9f; Hide();
            if (ActiveAnimations != 0) throw new Exception("Hidden animations still running.");
            report.AppendLine("HIDDEN_ANIMATION_TIMERS_ZERO: PASS");
            var process = Process.GetCurrentProcess(); var cpuStart = process.TotalProcessorTime.TotalSeconds; var idle = Stopwatch.StartNew(); await Task.Delay(2000); process.Refresh();
            report.AppendLine("HIDDEN_IDLE_CPU_SECONDS: " + (process.TotalProcessorTime.TotalSeconds - cpuStart).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
            Restore(); start.PreviewHover(false); progress.Value = 0; SetState("Буфер выключен"); await Task.Delay(250);
            if (trayState != TrayState.Idle || ActiveAnimations != 0) throw new Exception("Idle state failed.");
            report.AppendLine("IDLE_TRAY_STATE: PASS");
            File.WriteAllText(target, report.ToString(), new System.Text.UTF8Encoding(false));
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
                if (recorder == null || !recorder.Running || trayState != TrayState.Recording || !traySave.Enabled || duration.Enabled) throw new Exception("Start controls or recording icon failed.");
                report.AppendLine("LIVE_START_ACTIONS_AND_TRAY: PASS");
                for (int i = 0; i < 6; i++) { await Task.Delay(1000); await RefreshState(); }
                await SaveReplay();
                if (lastReplay == null || !File.Exists(lastReplay) || !openLast.Enabled || !traySave.Enabled || trayState != TrayState.Recording) throw new Exception("Save action or tray state failed.");
                report.AppendLine("LIVE_SAVE_ACTIONS_AND_TRAY: PASS");
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
