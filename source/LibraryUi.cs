using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReplayLite
{
    sealed class ClipCard : Control
    {
        readonly ClipEntry clip; Bitmap thumbnail; ClipPreview preview;
        internal event EventHandler Play, Locate, Copy, Delete;
        public ClipCard(ClipEntry clip)
        {
            this.clip = clip; BackColor = Theme.Background; ForeColor = Theme.Text; Size = new Size(350, 250);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AddButton("square-play", L.T("Смотреть"), 12, () => { if (Play != null) Play(this, EventArgs.Empty); });
            AddButton("folder", L.T("Показать в папке"), 98, () => { if (Locate != null) Locate(this, EventArgs.Empty); });
            AddButton("copy", L.T("Копировать путь"), 184, () => { if (Copy != null) Copy(this, EventArgs.Empty); });
            AddButton("trash", L.T("В корзину"), 270, () => { if (Delete != null) Delete(this, EventArgs.Empty); });
            AccessibleName = clip.Name; Cursor = Cursors.Default;
        }
        void AddButton(string icon, string name, int x, Action action)
        { var button = new ActionButton { IconName = icon, AccessibleName = name, Fill = Theme.Input, ForeColor = Theme.Text, BackColor = Theme.Card, Text = name }; button.SetBounds(x, 208, 66, 30); button.Click += delegate { action(); }; Controls.Add(button); }
        internal void SetPreview(ClipPreview value)
        {
            preview = value; if (thumbnail != null) { thumbnail.Dispose(); thumbnail = null; }
            if (value != null && value.Image != null) using (var stream = new MemoryStream(value.Image)) using (var image = new Bitmap(stream)) thumbnail = new Bitmap(image);
            Invalidate();
        }
        int Px(int value) { return (int)Math.Round(value * Width / 350f); }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); if (e.Y < 200 && Play != null) Play(this, EventArgs.Empty); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Background); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 12)) using (var fill = new SolidBrush(Theme.Card)) using (var border = new Pen(Theme.Border)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
            var area = new Rectangle(1, 1, Width - 2, Px(128));
            if (thumbnail != null) { float scale = Math.Min(area.Width / (float)thumbnail.Width, area.Height / (float)thumbnail.Height); var target = new Rectangle(area.X + (int)((area.Width - thumbnail.Width * scale) / 2), area.Y, (int)(thumbnail.Width * scale), (int)(thumbnail.Height * scale)); e.Graphics.DrawImage(thumbnail, target); }
            else { LucideAssets.Draw(e.Graphics, "square-play", new Rectangle(Width / 2 - Px(16), Px(37), Px(32), Px(32)), Theme.Muted); TextRenderer.DrawText(e.Graphics, L.T(preview == null ? "Загружаем превью…" : "Превью недоступно"), Font, new Rectangle(Px(12), Px(82), Width - Px(24), Px(22)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix); }
            if (preview != null && preview.Duration > 0) TextRenderer.DrawText(e.Graphics, ClipPlayer.Stamp(preview.Duration), Font, new Rectangle(Width - Px(78), Px(104), Px(66), Px(22)), Theme.Text, Theme.Background, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, clip.Name, Font, new Rectangle(Px(12), Px(138), Width - Px(24), Px(34)), Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, clip.Modified.ToLocalTime().ToString("g") + " · " + (clip.Length / 1048576.0).ToString("0.0") + " MiB · " + Path.GetExtension(clip.Path).TrimStart('.').ToUpperInvariant(), Font, new Rectangle(Px(12), Px(178), Width - Px(24), Px(23)), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        protected override void Dispose(bool disposing) { if (disposing && thumbnail != null) thumbnail.Dispose(); base.Dispose(disposing); }
    }
    public sealed partial class MainForm
    {
        readonly ActionButton libraryButton = new ActionButton(); readonly Panel libraryPage = new Panel();
        readonly ClipPreviewCache clipCache = new ClipPreviewCache();
        readonly List<ClipCard> clipCards = new List<ClipCard>();
        List<ClipEntry> clips = new List<ClipEntry>(); ThemeLabel libraryStatus, pageNumber; ActionButton libraryPrevious, libraryNext, libraryRefresh;
        CancellationTokenSource libraryCancel; FileSystemWatcher libraryWatcher; System.Windows.Forms.Timer libraryDebounce;
        Task libraryLoading = Task.FromResult(0); ClipPlayer player; int libraryGeneration, libraryIndex; bool libraryOpen, libraryBuilt, libraryOpeningPlayer;
        void InitializeLibraryNavigation()
        {
            StyleButton(libraryButton, "", this, 616, 57, 36, 32, false); libraryButton.Glyph = NavigationGlyph.Folder; libraryButton.AccessibleName = L.T("Повторы"); Tip(libraryButton, L.T("Повторы"));
            libraryButton.Click += delegate { ShowLibrary(); };
            libraryPage.SetBounds(20, 134, 720, 325); libraryPage.BackColor = Theme.Background; libraryPage.Visible = false; Controls.Add(libraryPage);
        }
        void BuildLibrary()
        {
            libraryBuilt = true; libraryStatus = new ThemeLabel { ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9), AutoEllipsis = true }; libraryStatus.SetBounds(0, 0, 410, 28); libraryPage.Controls.Add(libraryStatus);
            StyleButton(openFolder, L.T("Открыть папку"), libraryPage, 430, 0, 164, 28, false); openFolder.Font = new Font("Segoe UI", 9); openFolder.Click += delegate { try { OpenFolder(); } catch (Exception error) { libraryStatus.Text = error.Message; } };
            libraryRefresh = new ActionButton(); StyleButton(libraryRefresh, L.T("Обновить"), libraryPage, 604, 0, 116, 28, false); libraryRefresh.Font = new Font("Segoe UI", 9); libraryRefresh.Click += delegate { libraryLoading = ReloadLibrary(); };
            libraryPrevious = new ActionButton(); libraryNext = new ActionButton();
            StyleButton(libraryPrevious, L.T("Назад"), libraryPage, 214, 292, 90, 30, false); StyleButton(libraryNext, L.T("Далее"), libraryPage, 416, 292, 90, 30, false);
            pageNumber = new ThemeLabel { ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9) }; pageNumber.SetBounds(306, 292, 108, 30); libraryPage.Controls.Add(pageNumber);
            libraryPrevious.Click += delegate { if (libraryIndex > 0) { libraryIndex--; libraryLoading = LoadLibraryPage(); } }; libraryNext.Click += delegate { if ((libraryIndex + 1) * 2 < clips.Count) { libraryIndex++; libraryLoading = LoadLibraryPage(); } };
            libraryDebounce = new System.Windows.Forms.Timer { Interval = 350 }; libraryDebounce.Tick += delegate { libraryDebounce.Stop(); if (libraryOpen && Visible && player == null) libraryLoading = ReloadLibrary(); };
            float factor = libraryPage.Width / 720f; if (Math.Abs(factor - 1) > .01) foreach (Control control in libraryPage.Controls) control.Scale(new SizeF(factor, factor));
        }
        void ShowLibrary()
        {
            hotkey.CancelCapture(); CloseSettingsPopups(); if (!libraryBuilt) BuildLibrary();
            settingsOpen = false; libraryOpen = true; foreach (var tab in settingsTabs) tab.Visible = false;
            settingsPage.Visible = recordingPage.Visible = false; libraryPage.Visible = true; settingsBack.Visible = true; settingsGear.Visible = update.Visible = libraryButton.Visible = false; RefreshUpdateIndicator(false); pageHeading.Text = L.T("Повторы");
            StartLibraryWatcher(); libraryLoading = ReloadLibrary();
        }
        void CancelLibraryLoad() { libraryGeneration++; if (libraryCancel != null) libraryCancel.Cancel(); }
        void StopLibraryWatcher() { if (libraryWatcher != null) { libraryWatcher.Dispose(); libraryWatcher = null; } if (libraryDebounce != null) libraryDebounce.Stop(); }
        void StartLibraryWatcher()
        {
            StopLibraryWatcher(); if (!libraryOpen || !Visible || !Directory.Exists(settings.Folder)) return;
            try
            {
                libraryWatcher = new FileSystemWatcher(settings.Folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, IncludeSubdirectories = false };
                FileSystemEventHandler changed = delegate(object sender, FileSystemEventArgs e) { QueueLibraryRefresh(); };
                libraryWatcher.Created += changed; libraryWatcher.Deleted += changed; libraryWatcher.Changed += changed; libraryWatcher.Renamed += delegate { QueueLibraryRefresh(); }; libraryWatcher.Error += delegate { QueueLibraryRefresh(); }; libraryWatcher.EnableRaisingEvents = true;
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
        }
        int refreshPosted;
        void QueueLibraryRefresh()
        {
            if (released || closing || !IsHandleCreated || Interlocked.CompareExchange(ref refreshPosted, 1, 0) != 0) return;
            try { BeginInvoke(new Action(delegate { Interlocked.Exchange(ref refreshPosted, 0); if (libraryOpen && Visible && player == null) { libraryDebounce.Stop(); libraryDebounce.Start(); } })); }
            catch (InvalidOperationException) { Interlocked.Exchange(ref refreshPosted, 0); }
        }
        void ClearClipCards() { foreach (var card in clipCards) { foreach (Control button in card.Controls) { localizedTips.Remove(button); tips.SetToolTip(button, null); } localizedTips.Remove(card); tips.SetToolTip(card, null); libraryPage.Controls.Remove(card); card.Dispose(); } clipCards.Clear(); }
        async Task ReloadLibrary()
        {
            if (!libraryOpen || player != null || released) return; StartLibraryWatcher(); CancelLibraryLoad(); var cancellation = new CancellationTokenSource(); libraryCancel = cancellation; int id = libraryGeneration; libraryStatus.Text = L.T("Читаем папку повторов…");
            try
            {
                var scan = await ClipLibrary.ScanAsync(settings.Folder, cancellation.Token);
                if (!libraryOpen || released || id != libraryGeneration) return;
                clips = scan.Clips; libraryIndex = Math.Min(libraryIndex, Math.Max(0, (clips.Count - 1) / 2));
                await LoadLibraryPage(scan.Limited);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (libraryOpen && !released && id == libraryGeneration) { ClearClipCards(); clips.Clear(); libraryPrevious.Enabled = libraryNext.Enabled = false; pageNumber.Text = ""; libraryStatus.Text = L.T("Не удалось открыть папку: ") + error.Message; } }
            finally { if (libraryCancel == cancellation) libraryCancel = null; cancellation.Dispose(); }
        }
        async Task LoadLibraryPage(bool limited = false)
        {
            CancelLibraryLoad(); var cancellation = new CancellationTokenSource(); libraryCancel = cancellation; int id = libraryGeneration; ClearClipCards();
            libraryPrevious.Enabled = libraryIndex > 0; libraryNext.Enabled = (libraryIndex + 1) * 2 < clips.Count;
            pageNumber.Text = (libraryIndex + 1) + " / " + Math.Max(1, (clips.Count + 1) / 2);
            libraryStatus.Text = clips.Count == 0 ? L.T("Повторов пока нет. Сохраните первый клип.") : string.Format(L.T("Клипы {0}–{1} из {2}"), libraryIndex * 2 + 1, Math.Min(clips.Count, libraryIndex * 2 + 2), clips.Count) + (limited ? L.T(" · показана часть папки") : "");
            try
            {
                for (int i = libraryIndex * 2; i < Math.Min(clips.Count, libraryIndex * 2 + 2); i++)
                {
                    ClipEntry entry = clips[i]; var card = new ClipCard(entry); float factor = libraryPage.Width / 720f; card.Scale(new SizeF(factor, factor)); card.SetBounds((int)Math.Round((i % 2) * 370 * factor), (int)Math.Round(36 * factor), (int)Math.Round(350 * factor), (int)Math.Round(250 * factor)); libraryPage.Controls.Add(card); clipCards.Add(card);
                    card.Play += async delegate { await OpenClip(entry); };
                    card.Locate += delegate { ClipAction(entry, () => Process.Start("explorer.exe", "/select," + Recorder.Quote(entry.Path))); };
                    card.Copy += delegate { ClipAction(entry, () => Clipboard.SetText(entry.Path)); };
                    card.Delete += delegate { DeleteClip(entry); };
                    foreach (Control button in card.Controls) Tip(button, button.AccessibleName); Tip(card, entry.Name);
                }
                for (int i = 0; i < clipCards.Count; i++)
                {
                    ClipCard card = clipCards[i]; ClipEntry entry = clips[libraryIndex * 2 + i]; ClipPreview preview;
                    try { if (!clipCache.TryGet(entry, out preview)) { preview = await ClipLibrary.Preview(entry, cancellation.Token); clipCache.Add(entry, preview); } }
                    catch (IOException error) { preview = new ClipPreview { Error = error.Message }; }
                    if (cancellation.IsCancellationRequested || !libraryOpen || released || id != libraryGeneration) return;
                    card.SetPreview(preview);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (libraryOpen && !released && id == libraryGeneration) libraryStatus.Text = error.Message; }
            finally { if (libraryCancel == cancellation) libraryCancel = null; cancellation.Dispose(); }
        }
        void ClipAction(ClipEntry entry, Action action)
        { try { if (!ClipLibrary.SameFile(entry)) throw new IOException(L.T("Клип изменён или удалён. Обновите список.")); action(); } catch (Exception error) { libraryStatus.Text = error.Message; } }
        async Task OpenClip(ClipEntry entry)
        {
            if (!libraryOpen || player != null || libraryOpeningPlayer) return; libraryOpeningPlayer = true; CancelLibraryLoad(); libraryDebounce.Stop(); StopLibraryWatcher(); ClipPreview preview;
            var cancellation = new CancellationTokenSource(); libraryCancel = cancellation; int id = libraryGeneration;
            try
            {
                if (!clipCache.TryGet(entry, out preview)) preview = await ClipLibrary.Preview(entry, cancellation.Token);
                if (!libraryOpen || released || id != libraryGeneration) return;
                foreach (Control control in libraryPage.Controls) control.Visible = false;
                player = new ClipPlayer(entry, preview); player.SetBounds(0, 0, libraryPage.Width, libraryPage.Height); player.ScaleContent(libraryPage.Width / 720f); libraryPage.Controls.Add(player); pageHeading.Text = entry.Name; Tip(pageHeading, entry.Name); await player.PlayAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!released && libraryOpen) libraryStatus.Text = error.Message; }
            finally { libraryOpeningPlayer = false; if (libraryCancel == cancellation) libraryCancel = null; cancellation.Dispose(); if (libraryOpen && player == null) StartLibraryWatcher(); }
        }
        void ClosePlayer()
        { if (player == null) return; player.Pause(); libraryPage.Controls.Remove(player); player.Dispose(); player = null; foreach (Control control in libraryPage.Controls) control.Visible = true; pageHeading.Text = L.T("Повторы"); localizedTips.Remove(pageHeading); tips.SetToolTip(pageHeading, null); StartLibraryWatcher(); libraryLoading = ReloadLibrary(); }
        void CloseLibrary()
        { libraryOpen = false; CancelLibraryLoad(); StopLibraryWatcher(); if (player != null) { libraryPage.Controls.Remove(player); player.Dispose(); player = null; } localizedTips.Remove(pageHeading); tips.SetToolTip(pageHeading, null); libraryPage.Visible = false; }
        void LibraryVisibilityChanged()
        { if (!Visible || WindowState == FormWindowState.Minimized) { CancelLibraryLoad(); StopLibraryWatcher(); if (player != null) player.Pause(); } else if (libraryOpen) { StartLibraryWatcher(); if (player == null) libraryLoading = ReloadLibrary(); } }
        void DisposeLibrary()
        { CloseLibrary(); ClearClipCards(); clipCache.Clear(); if (libraryDebounce != null) libraryDebounce.Dispose(); }
    }
}
