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
        readonly SelectBox duration = new SelectBox(), fps = new SelectBox(), sound = new SelectBox(), display = new SelectBox();
        readonly NumberBox rate = new NumberBox();
        readonly HotkeyBox hotkey = new HotkeyBox(); readonly Label hotkeyInfo = new Label();
        List<MonitorChoice> monitors = new List<MonitorChoice>();
        bool hotkeyRegistered;
        readonly ActionButton update = new ActionButton(); bool updating, monitorRefresh;
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
            Text = "ReplayLite"; ClientSize = new Size(760, 742); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Icon = icons[(int)TrayState.Recording]; DoubleBuffered = true;
            var logo = new PictureBox { Image = brand, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Background }; logo.SetBounds(24, 22, 48, 48); Controls.Add(logo);
            LabelAt(this, "ReplayLite", 83, 17, 300, 35, 21, true, Theme.Text);
            LabelAt(this, "Сохрани момент, который уже случился", 85, 56, 425, 24, 9, false, Theme.Muted);
            badge.SetBounds(580, 30, 156, 34); Controls.Add(badge);
            StyleButton(update, "Обновления", this, 580, 69, 156, 25, false);
            update.Click += async delegate { if (updating) return; using (var dialog = new UpdateForm(ApplyUpdate)) dialog.ShowDialog(this); if (updating) await Quit(); };

            var recording = Card(24, 98, 712, 163);
            state.SetBounds(20, 16, 415, 33); state.Font = new Font("Segoe UI", 16, FontStyle.Bold); state.ForeColor = Theme.Text; state.BackColor = Theme.Card; state.AutoEllipsis = true; recording.Controls.Add(state);
            stateDetail.SetBounds(22, 53, 425, 25); stateDetail.Font = new Font("Segoe UI", 9); stateDetail.ForeColor = Theme.Muted; stateDetail.BackColor = Theme.Card; stateDetail.AutoEllipsis = true; recording.Controls.Add(stateDetail);
            progress.SetBounds(22, 90, 425, 6); recording.Controls.Add(progress);
            StyleButton(start, "Включить буфер", recording, 20, 110, 427, 36, false);
            StyleButton(save, "Сохранить повтор", recording, 465, 18, 226, 128, true); save.Detail = "Ctrl+Shift+F9"; save.AccessibleName = "Сохранить последние минуты, Ctrl+Shift+F9"; save.Enabled = false;
            start.Click += async delegate { await Toggle(); }; save.Click += async delegate { await SaveReplay(); };

            Metric(cpuValue, "CPU", 24, 281, "—"); Metric(ramValue, "ПАМЯТЬ", 207, 281, "—"); Metric(bufferValue, "В БУФЕРЕ", 390, 281, "0 с"); Metric(diskValue, "БУФЕР НА ДИСКЕ", 573, 281, "0 МБ");

            var options = Card(24, 375, 712, 288);
            LabelAt(options, "Настройки записи", 20, 15, 320, 26, 11, true, Theme.Text);
            LabelAt(options, "Длительность", 20, 53, 205, 22, 9, false, Theme.Muted);
            LabelAt(options, "Частота кадров", 245, 53, 220, 22, 9, false, Theme.Muted);
            LabelAt(options, "Битрейт · Мбит/с", 485, 53, 205, 22, 9, false, Theme.Muted);
            duration.Items.AddRange(new object[] { "30 секунд", "2 минуты", "5 минут", "10 минут" }); duration.SelectedIndex = settings.Seconds == 30 ? 0 : settings.Seconds == 300 ? 2 : settings.Seconds == 600 ? 3 : 1;
            fps.Items.AddRange(new object[] { "30 FPS · экономнее", "60 FPS · плавнее" }); fps.SelectedIndex = settings.Fps == 60 ? 1 : 0;
            rate.Minimum = 2; rate.Maximum = 30; rate.Value = settings.Mbps;
            AddInput(options, duration, 20, 77, 205, "Длительность повтора"); AddInput(options, fps, 245, 77, 220, "Частота кадров"); AddInput(options, rate, 485, 77, 205, "Битрейт");
            LabelAt(options, "Звук", 20, 129, 205, 22, 9, false, Theme.Muted); LabelAt(options, "Монитор", 245, 129, 445, 22, 9, false, Theme.Muted);
            sound.Items.AddRange(new object[] { "Система + микрофон", "Только компьютер", "Без звука" }); sound.SelectedIndex = settings.Audio == "none" ? 2 : settings.Audio == "system" ? 1 : 0;
            RefreshMonitors();
            AddInput(options, sound, 20, 153, 205, "Источники звука"); AddInput(options, display, 245, 153, 445, "Монитор для записи");
            LabelAt(options, "Сочетание для сохранения", 20, 203, 335, 22, 9, false, Theme.Muted);
            hotkey.Binding = settings.Hotkey; AddInput(options, hotkey, 20, 227, 335, "Горячая клавиша сохранения");
            hotkeyInfo.SetBounds(375, 229, 315, 43); hotkeyInfo.BackColor = Theme.Card; hotkeyInfo.ForeColor = Theme.Muted; hotkeyInfo.Font = new Font("Segoe UI", 8); hotkeyInfo.Text = "Одиночная клавиша или Ctrl/Alt/Shift/Win + клавиша. Нажми и отпусти. Щелчок вне поля — отмена."; options.Controls.Add(hotkeyInfo);
            hotkey.CaptureStarted += delegate { if (hotkeyRegistered) Native.UnregisterHotKey(Handle, 1); hotkeyRegistered = false; hotkeyInfo.Text = "Нажми клавишу или сочетание и отпусти все клавиши. Для отмены щёлкни вне поля."; };
            hotkey.CaptureCancelled += delegate { if (!released) RegisterConfiguredHotkey(); };
            hotkey.CaptureFailed += delegate { hotkeyInfo.ForeColor = Theme.Red; hotkeyInfo.Text = "Не удалось включить ввод клавиш. Повтори попытку; прежняя настройка сохранена."; };
            hotkey.BindingProposed += delegate(object sender, BindingEventArgs e) { e.Accepted = TryChangeHotkey(e.Binding); };
            tips.SetToolTip(display, "Подключённые мониторы: модель, разрешение и основной экран. Для записи требуется подключение к NVIDIA.");

            folder.Text = settings.Folder; folder.ReadOnly = true; folder.BorderStyle = BorderStyle.None; folder.BackColor = Theme.Background; folder.ForeColor = Theme.Muted; folder.Font = new Font("Segoe UI", 8.5f); folder.SetBounds(29, 714, 700, 19); folder.TabStop = false; Controls.Add(folder);
            StyleButton(openFolder, "Папка повторов", this, 24, 679, 155, 30, false); openFolder.Click += delegate { OpenFolder(); };
            StyleButton(openLast, "Последний повтор", this, 189, 679, 179, 30, false); openLast.Enabled = false; openLast.Click += delegate { if (lastReplay != null && File.Exists(lastReplay)) Process.Start("explorer.exe", Recorder.Quote(lastReplay)); };
            StyleButton(browse, "Сменить папку", this, 378, 679, 155, 30, false);
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog { SelectedPath = folder.Text, Description = "Папка для сохранённых MP4" }) if (dialog.ShowDialog() == DialogResult.OK) { folder.Text = dialog.SelectedPath; settings.Folder = folder.Text; settings.Save(); } };
            StyleButton(toTray, "Свернуть в трей", this, 553, 679, 183, 30, false); toTray.Click += delegate { Hide(); };
            hint.SetBounds(27, 355, 706, 18); hint.Font = new Font("Segoe UI", 8); hint.ForeColor = Theme.Muted; hint.Text = "Записывается весь экран. После закрытия окна запись продолжается в трее."; Controls.Add(hint);

            tray.Icon = icons[0]; tray.Text = "ReplayLite — буфер выключен"; tray.Visible = true;
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), BackColor = Theme.Input, ForeColor = Theme.Text, Font = Font };
            menu.Items.Add("Открыть ReplayLite", null, delegate { Restore(); }); menu.Items.Add(new ToolStripSeparator());
            traySave.Text = "Сохранить повтор"; traySave.Enabled = false; traySave.Click += async delegate { await SaveReplay(); }; menu.Items.Add(traySave);
            trayToggle.Text = "Включить буфер"; trayToggle.Click += async delegate { await Toggle(); }; menu.Items.Add(trayToggle);
            menu.Items.Add("Папка повторов", null, delegate { OpenFolder(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Выход", null, async delegate { await Quit(); }); tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Restore(); };
            SetState("Буфер выключен");
            timer.Tick += async delegate { await RefreshState(); }; timer.Start();
            VisibleChanged += delegate { if (!Visible) { hotkey.CancelCapture(); StopAnimations(); } };
            Deactivate += delegate { hotkey.CancelCapture(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) { hotkey.CancelCapture(); StopAnimations(); } };
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
            control.SetBounds(x, y, width, 40); parent.Controls.Add(control);
        }
        void StyleButton(ActionButton button, string text, Control parent, int x, int y, int width, int height, bool accent)
        { button.Text = text; button.SetBounds(x, y, width, height); button.BackColor = parent == this ? Theme.Background : Theme.Card; button.Font = new Font("Segoe UI", 10, FontStyle.Bold); button.ForeColor = accent ? Color.FromArgb(9, 37, 29) : Theme.Text; button.Fill = accent ? Theme.Accent : Theme.Input; parent.Controls.Add(button); }
        void StopAnimations() { foreach (var button in new[] { start, save, browse, openFolder, openLast, toTray, update }) button.FinishAnimation(); progress.FinishAnimation(); badge.FinishAnimation(); }
        int ActiveAnimations { get { int count = 0; foreach (var button in new[] { start, save, browse, openFolder, openLast, toTray, update }) if (button.Animating) count++; return count + (progress.Animating ? 1 : 0) + (badge.Animating ? 1 : 0); } }
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
            start.Enabled = !busy && !closing && (active || monitors.Exists(m => m.CanCapture)); start.Text = active ? "Остановить буфер" : "Включить буфер";
            start.Fill = active ? Color.FromArgb(63, 39, 49) : Theme.Input; start.ForeColor = active ? Theme.Red : Theme.Text;
            save.Enabled = active && !busy && !saving && !closing; traySave.Enabled = save.Enabled;
            trayToggle.Enabled = start.Enabled; trayToggle.Text = start.Text;
            hotkey.Enabled = !busy && !closing;
            update.Enabled = !busy && !saving && !closing && !updating;
            UpdateHotkeyLabels();
        }
        void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        void OpenFolder() { Directory.CreateDirectory(settings.Folder); Process.Start("explorer.exe", Recorder.Quote(settings.Folder)); }
        void Inputs(bool enabled) { foreach (var control in new Control[] { duration, fps, sound, rate, display, browse }) control.Enabled = enabled; }
        void RefreshMonitors()
        {
            string previous = display.SelectedIndex >= 0 && display.SelectedIndex < monitors.Count ? monitors[display.SelectedIndex].Id : settings.MonitorId;
            display.ClosePopup(); monitors = MonitorCatalog.Enumerate(); display.Items.Clear();
            foreach (var monitor in monitors) display.Items.Add(monitor);
            if (monitors.Count == 0) { display.Items.Add("Мониторы не обнаружены"); display.SelectedIndex = 0; return; }
            int selected = monitors.FindIndex(m => !string.IsNullOrEmpty(previous) && m.Id == previous);
            if (selected < 0) selected = monitors.FindIndex(m => m.CanCapture && m.Adapter == settings.DisplayAdapter && m.Output == settings.Display);
            if (selected < 0) selected = monitors.FindIndex(m => m.CanCapture);
            display.SelectedIndex = selected < 0 ? 0 : selected;
        }
        void UpdateHotkeyLabels()
        { save.Detail = settings.Hotkey.ToString(); save.AccessibleName = "Сохранить последние минуты, " + save.Detail; save.Invalidate(); traySave.Text = "Сохранить повтор   " + save.Detail; }
        void RegisterConfiguredHotkey()
        {
            if (released || !IsHandleCreated) return;
            if (hotkeyRegistered) Native.UnregisterHotKey(Handle, 1);
            hotkeyRegistered = Native.RegisterHotKey(Handle, 1, 0x4000 | settings.Hotkey.Modifiers, (uint)settings.Hotkey.Key);
            hotkey.Binding = settings.Hotkey; hotkey.Error = !hotkeyRegistered; hotkey.Invalidate();
            hotkeyInfo.ForeColor = hotkeyRegistered ? Theme.Muted : Theme.Red;
            hotkeyInfo.Text = hotkeyRegistered ? "Нажми поле для изменения. Одиночная клавиша перехватывается при работе приложения." : "Сочетание занято. Выбери другое; кнопка сохранения работает.";
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
                    var selectedMonitor = display.SelectedIndex >= 0 && display.SelectedIndex < monitors.Count ? monitors[display.SelectedIndex] : null;
                    if (selectedMonitor == null || !selectedMonitor.CanCapture) throw new InvalidOperationException("Выбери монитор, подключённый к видеокарте NVIDIA.");
                    settings.Seconds = new[] { 30, 120, 300, 600 }[duration.SelectedIndex]; settings.Fps = fps.SelectedIndex == 1 ? 60 : 30; settings.Mbps = rate.Value; settings.Display = selectedMonitor.Output; settings.DisplayAdapter = selectedMonitor.Adapter; settings.MonitorId = selectedMonitor.Id; settings.Rotation = selectedMonitor.Rotation;
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
            try { var output = await recorder.SaveAsync(); lastReplay = output; openLast.Enabled = true; RefreshBufferMetrics(); progress.FinishAnimation(); SetState("Сохранено: " + Path.GetFileName(output)); if (!testing) tray.ShowBalloonTip(3000, "Повтор сохранён", output, ToolTipIcon.Info); }
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
                cpuValue.Text = cpu.ToString("0.0") + "%"; ramValue.Text = ((process.WorkingSet64 + recorder.Memory) / 1048576.0).ToString("0") + " МБ";
                RefreshBufferMetrics();
            }
            catch (Exception e) { SetState("Ошибка состояния", TrayState.Error); stateDetail.Text = e.Message; }
        }
        void RefreshBufferMetrics()
        {
            if (recorder == null) return;
            double seconds = recorder.BufferedSeconds;
            bufferValue.Text = string.Format("{0:0} / {1} с", seconds, settings.Seconds); diskValue.Text = (recorder.DiskBytes / 1048576.0).ToString("0") + " МБ"; progress.Value = (float)(seconds / settings.Seconds);
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
        async Task ApplyUpdate(string stage)
        {
            if (busy || saving || closing) throw new InvalidOperationException("Дождись завершения текущей операции и повтори обновление.");
            updating = true; busy = true; SyncActions();
            try
            {
                string helperFolder = Path.Combine(Path.GetTempPath(), "ReplayLite-helper-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(helperFolder);
                string helper = Path.Combine(helperFolder, "UpdateHelper.exe"); File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UpdateHelper.exe"), helper);
                if (recorder != null) await StopRecorder(); settings.Save();
                Process.Start(new ProcessStartInfo(helper, Process.GetCurrentProcess().Id + " " + Recorder.Quote(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')) + " " + Recorder.Quote(stage)) { UseShellExecute = false, CreateNoWindow = true });
            }
            catch { updating = false; throw; }
            finally { busy = false; if (!released) SyncActions(); }
        }
        async Task RefreshDisplayAsync()
        {
            if (monitorRefresh) return; monitorRefresh = true;
            try { while (busy && !closing && !released) await Task.Delay(100); if (released || closing) return; busy = true; try { if (recorder != null) { await StopRecorder(); SetState("Мониторы изменились — включи буфер снова"); } RefreshMonitors(); } finally { busy = false; SyncActions(); } }
            finally { monitorRefresh = false; }
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
        { base.OnShown(e); RegisterConfiguredHotkey(); }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 && message.WParam.ToInt32() == 1) { if (!hotkey.IsCapturing) { var pending = SaveReplay(); } return; }
            if (message.Msg == 0x007e && IsHandleCreated && !released) BeginInvoke(new Action(async delegate { await RefreshDisplayAsync(); }));
            base.WndProc(ref message);
        }
        internal void VerifyControls()
        { if (duration.SelectedIndex < 0 || fps.SelectedIndex < 0 || sound.SelectedIndex < 0) throw new InvalidOperationException("A default selection was lost."); }
        internal void PreviewForDocs() { folder.Text = Settings.Portable ? @"Replays\" : @"%USERPROFILE%\Videos\ReplayLite"; }
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
            foreach (var select in new[] { duration, fps, sound, display })
            {
                int initial = select.SelectedIndex;
                for (int i = 0; i < 5; i++) { select.Open(); await Task.Delay(25); select.PickForTest((initial + 1) % select.Items.Count); await Task.Delay(25); if (select.SelectedIndex != (initial + 1) % select.Items.Count) throw new Exception("Dropdown selection failed."); }
                select.SelectedIndex = initial;
            }
            report.AppendLine("DROPDOWN_REOPEN_AND_PICK_20_TIMES: PASS");
            if (monitors.Count > 0 && !(display.Items[0] is MonitorChoice)) throw new Exception("Monitor choices are not mapped to native outputs.");
            report.AppendLine("MONITOR_CHOICES_NATIVE_MAPPING: PASS");
            VerifyHotkeyForTest(); report.AppendLine("HOTKEY_PERSIST_CANCEL_COLLISION: PASS");
            start.PreviewHover(true); progress.Value = .65f; SetState("Буфер включён", TrayState.Recording);
            await WaitForAnimationsAsync();
            if (ActiveAnimations != 0 || trayState != TrayState.Recording) throw new Exception("Animation did not settle or recording tray state was lost.");
            report.AppendLine("TRANSITIONS_SETTLE: PASS");
            SetState("Сохраняю повтор…", TrayState.Saving); await WaitForAnimationsAsync();
            if (trayState != TrayState.Saving || ActiveAnimations != 0) throw new Exception("Save status failed.");
            report.AppendLine("SAVING_TRAY_STATE: PASS");
            SetState("Ошибка сохранения", TrayState.Error); await WaitForAnimationsAsync();
            if (trayState != TrayState.Error) throw new Exception("Error tray state failed.");
            start.PreviewHover(false); progress.Value = .9f; hotkey.BeginCapture(); Hide();
            if (ActiveAnimations != 0) throw new Exception("Hidden animations still running.");
            if (hotkey.IsCapturing || hotkey.CaptureHookActive) throw new Exception("Hidden window kept keyboard capture active.");
            report.AppendLine("HIDDEN_CAPTURE_HOOK_REMOVED: PASS");
            report.AppendLine("HIDDEN_ANIMATION_TIMERS_ZERO: PASS");
            var process = Process.GetCurrentProcess(); var cpuStart = process.TotalProcessorTime.TotalSeconds; var idle = Stopwatch.StartNew(); await Task.Delay(2000); process.Refresh();
            report.AppendLine("HIDDEN_IDLE_CPU_SECONDS: " + (process.TotalProcessorTime.TotalSeconds - cpuStart).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
            Restore(); start.PreviewHover(false); progress.Value = 0; SetState("Буфер выключен"); await WaitForAnimationsAsync();
            if (trayState != TrayState.Idle || ActiveAnimations != 0) throw new Exception("Idle state failed.");
            report.AppendLine("IDLE_TRAY_STATE: PASS");
            File.WriteAllText(target, report.ToString(), new System.Text.UTF8Encoding(false));
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
                if (stored.Hotkey.Key != Keys.F10 || stored.Hotkey.Modifiers != 6 || save.Detail != stored.Hotkey.ToString() || !traySave.Text.Contains(stored.Hotkey.ToString())) throw new Exception("Binding did not persist or update labels.");
                foreach (var binding in new[] { new KeyBinding(0, Keys.F10), new KeyBinding(1, Keys.F10), new KeyBinding(4, Keys.F10), new KeyBinding(8, Keys.F10), new KeyBinding(2, Keys.Insert), new KeyBinding(0, Keys.NumPad8), new KeyBinding(0, Keys.Tab), new KeyBinding(0, Keys.Escape) })
                {
                    if (!binding.Valid) throw new Exception("Supported binding rejected: " + binding);
                    hotkey.BeginCapture(); var previous = settings.Hotkey; bool accepted = hotkey.Propose(binding);
                    if (accepted) { var loaded = Settings.Load(); if (loaded.Hotkey.Modifiers != binding.Modifiers || loaded.Hotkey.Key != binding.Key || save.Detail != binding.ToString() || hotkey.CaptureHookActive) throw new Exception("Expanded binding did not persist: " + binding); }
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
