using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReplayLite
{
    interface IAnimationPreference { bool AnimationsEnabled { get; } }
    static class Theme
    {
        public static Func<string, string> TranslateText = text => text;
        public static Color Background = Color.FromArgb(13, 17, 24), Card = Color.FromArgb(22, 28, 39), Input = Color.FromArgb(30, 38, 51);
        public static Color Border = Color.FromArgb(43, 53, 69), Text = Color.FromArgb(239, 244, 251), Muted = Color.FromArgb(143, 159, 181);
        public static Color Accent = Color.FromArgb(76, 224, 178), Blue = Color.FromArgb(113, 168, 255), Red = Color.FromArgb(255, 131, 139);
        public static Color AccentText = Color.FromArgb(20, 33, 29);
        public static void SetPalette(string name)
        {
            bool light = name == "light";
            bool ocean = name == "ocean", violet = name == "violet";
            Background = light ? Color.FromArgb(238, 239, 234) : ocean ? Color.FromArgb(18, 26, 35) : violet ? Color.FromArgb(25, 23, 34) : Color.FromArgb(18, 26, 27);
            Card = light ? Color.FromArgb(250, 250, 245) : ocean ? Color.FromArgb(28, 39, 51) : violet ? Color.FromArgb(36, 33, 47) : Color.FromArgb(27, 38, 39);
            Input = light ? Color.FromArgb(227, 232, 222) : ocean ? Color.FromArgb(39, 53, 69) : violet ? Color.FromArgb(49, 44, 62) : Color.FromArgb(37, 50, 51);
            Border = light ? Color.FromArgb(190, 202, 188) : ocean ? Color.FromArgb(58, 77, 96) : violet ? Color.FromArgb(74, 65, 91) : Color.FromArgb(58, 77, 75);
            Text = light ? Color.FromArgb(35, 48, 40) : ocean ? Color.FromArgb(230, 237, 245) : violet ? Color.FromArgb(239, 234, 245) : Color.FromArgb(231, 239, 235);
            Muted = light ? Color.FromArgb(82, 100, 87) : ocean ? Color.FromArgb(166, 182, 201) : violet ? Color.FromArgb(187, 176, 202) : Color.FromArgb(167, 185, 179);
            Accent = light ? Color.FromArgb(54, 111, 86) : ocean ? Color.FromArgb(143, 188, 219) : violet ? Color.FromArgb(191, 171, 221) : Color.FromArgb(139, 204, 180);
            AccentText = light ? Color.FromArgb(250, 250, 245) : Color.FromArgb(20, 33, 29);
            Blue = light ? Color.FromArgb(48, 98, 138) : Color.FromArgb(154, 187, 218);
            Red = light ? Color.FromArgb(161, 57, 68) : Color.FromArgb(224, 153, 159);
        }
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height)); var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        public static Color Mix(Color a, Color b, float t)
        { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
        public static bool CanAnimate(Control c)
        { var form = c.FindForm(); var preference = form as IAnimationPreference; return (preference == null || preference.AnimationsEnabled) && c.Visible && form != null && form.Visible && form.WindowState != FormWindowState.Minimized; }
        public static void Canvas(Control c)
        { c.Font = new Font("Segoe UI", 10); c.ForeColor = Text; c.BackColor = Card; }
    }
    sealed class ThemeLabel : Label
    {
        public bool HelpIcon { get; set; }
        public ThemeLabel() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            flags |= Text.IndexOf('\n') >= 0 || Height > Font.Height * 1.8 ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine;
            if (AutoEllipsis) flags |= TextFormatFlags.EndEllipsis;
            if (TextAlign == ContentAlignment.MiddleCenter || TextAlign == ContentAlignment.TopCenter || TextAlign == ContentAlignment.BottomCenter) flags |= TextFormatFlags.HorizontalCenter;
            if (TextAlign == ContentAlignment.TopRight || TextAlign == ContentAlignment.MiddleRight || TextAlign == ContentAlignment.BottomRight) flags |= TextFormatFlags.Right;
            if (TextAlign == ContentAlignment.MiddleLeft || TextAlign == ContentAlignment.MiddleCenter || TextAlign == ContentAlignment.MiddleRight) flags |= TextFormatFlags.VerticalCenter;
            Color color = Enabled ? ForeColor : Theme.Muted;
            var bounds = ClientRectangle; if (HelpIcon) bounds.Width = Math.Max(1, bounds.Width - 20);
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, color, flags);
            if (HelpIcon) { int width = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(bounds.Width, Height), flags).Width; LucideAssets.Draw(e.Graphics, "circle-question-mark", new Rectangle(Math.Min(bounds.Width + 4, width + 5), (Height - 14) / 2, 14, 14), Theme.Muted); }
        }
    }
    sealed class ThemeToolTip : ToolTip
    {
        readonly Font body = new Font("Segoe UI", 9);
        public ThemeToolTip()
        {
            OwnerDraw = true; ShowAlways = true; InitialDelay = 450; ReshowDelay = 100; AutoPopDelay = 15000; UseAnimation = UseFading = false;
            Popup += delegate(object sender, PopupEventArgs e) { var text = GetToolTip(e.AssociatedControl); var size = TextRenderer.MeasureText(text, body, new Size(320, 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding); e.ToolTipSize = new Size(Math.Min(344, Math.Max(120, size.Width + 24)), size.Height + 24); };
            Draw += delegate(object sender, DrawToolTipEventArgs e)
            {
                e.Graphics.Clear(Theme.Card); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, body, new Rectangle(12, 12, e.Bounds.Width - 24, e.Bounds.Height - 24), Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) body.Dispose(); base.Dispose(disposing); }
    }
    sealed class CardPanel : Panel
    {
        public CardPanel() { DoubleBuffered = true; BackColor = Theme.Background; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 16))
            using (var fill = new SolidBrush(Theme.Card)) using (var line = new Pen(Theme.Border))
            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(line, path); }
            base.OnPaint(e);
        }
    }
    enum NavigationGlyph { None, Gear, Back, Monitor, Folder, Video, Audio, Keyboard, Windows, Update }
    sealed class ActionButton : Button
    {
        Color current = Theme.Input;
        Font detailFont;
        Color fill = Theme.Input;
        bool hovered, pressed;
        public string Detail = "";
        public NavigationGlyph Glyph = NavigationGlyph.None;
        public bool AttentionDot = false;
        public Color Fill { get { return fill; } set { if (fill == value) return; fill = value; Transition(); } }
        public bool Animating { get { return false; } }
        public ActionButton()
        {
            Theme.Canvas(this); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        void Transition()
        {
            var next = !Enabled ? Theme.Mix(Theme.Input, Theme.Card, .4f) : pressed ? Theme.Mix(fill, Color.Black, 0.12f) : hovered ? Theme.Mix(fill, Theme.Text, 0.09f) : fill;
            if (current == next) return; current = next; Invalidate();
        }
        public void FinishAnimation() { Transition(); }
        internal void PreviewHover(bool value) { hovered = value; Transition(); }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Transition(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Transition(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Transition(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Transition(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Transition(); base.OnEnabledChanged(e); }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) FinishAnimation(); base.OnVisibleChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(1, 1, Width - 2, Height - 2), 12))
            using (var brush = new SolidBrush(current)) e.Graphics.FillPath(brush, path);
            var color = Enabled ? ForeColor : Theme.Muted;
            const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (Image != null) e.Graphics.DrawImage(Image, ClientRectangle);
            else if (Glyph != NavigationGlyph.None) DrawNavigation(e.Graphics, color);
            else if (Detail.Length == 0) TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, color, flags);
            else
            {
                int y = Math.Max(4, (Height - 49) / 2);
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(8, y, Width - 16, 25), color, flags);
                if (detailFont == null) detailFont = new Font("Segoe UI", 8.5f);
                TextRenderer.DrawText(e.Graphics, Detail, detailFont, new Rectangle(8, y + 26, Width - 16, 22), color, flags);
            }
            if (AttentionDot) using (var dot = new SolidBrush(Theme.Accent)) e.Graphics.FillEllipse(dot, Width - 10, 2, 8, 8);
            if (Focused && ShowFocusCues) { using (var path = Theme.Round(new RectangleF(4, 4, Width - 8, Height - 8), 9)) using (var pen = new Pen(Theme.Blue)) e.Graphics.DrawPath(pen, path); }
        }
        void DrawNavigation(Graphics graphics, Color color)
        {
            string[] names = { "", "settings", "undo-2", "monitor", "folder", "video", "headphones", "keyboard", "panels-top-left", "download" };
            int size = (int)Math.Round(20 * graphics.DpiX / 96f);
            LucideAssets.Draw(graphics, names[(int)Glyph], new Rectangle((Width - size) / 2, (Height - size) / 2, size, size), color);
        }
        protected override void Dispose(bool disposing) { if (disposing && detailFont != null) detailFont.Dispose(); base.Dispose(disposing); }
    }
    sealed class ToggleSwitch : CheckBox
    {
        public bool HelpIcon { get; set; }
        public ToggleSwitch()
        { Theme.Canvas(this); AutoSize = false; Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float y = (Height - 22) / 2f;
            using (var path = Theme.Round(new RectangleF(Width - 44, y, 42, 22), 11))
            using (var brush = new SolidBrush(Enabled && Checked ? Theme.Accent : Theme.Input)) e.Graphics.FillPath(brush, path);
            using (var brush = new SolidBrush(Enabled ? (Checked ? Theme.Background : Theme.Muted) : Theme.Border)) e.Graphics.FillEllipse(brush, Width - (Checked ? 21 : 41), y + 3, 16, 16);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            int textWidth = Width - (HelpIcon ? 75 : 53);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, textWidth, Height), Enabled ? ForeColor : Theme.Muted, flags);
            if (HelpIcon) { int width = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(textWidth, Height), flags).Width; LucideAssets.Draw(e.Graphics, "circle-question-mark", new Rectangle(Math.Min(textWidth + 4, width + 5), (Height - 14) / 2, 14, 14), Theme.Muted); }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(1, 1, Width - 3, Height - 3), Theme.Text, BackColor);
        }
    }
    sealed class SelectBox : Control
    {
        public readonly List<object> Items = new List<object>();
        RoundedMenu popup;
        int index = -1;
        public event EventHandler SelectedIndexChanged;
        public int SelectedIndex
        {
            get { return index; }
            set { if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException("value"); bool changed = index != value; index = value; Text = index < 0 ? "" : Theme.TranslateText(Items[index].ToString()); Invalidate(); if (changed && SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); }
        }
        public SelectBox()
        { Theme.Canvas(this); BackColor = Theme.Card; Cursor = Cursors.Hand; TabStop = true; AccessibleRole = AccessibleRole.ComboBox; SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        internal void Open()
        {
            if (!Enabled || Items.Count == 0) return;
            if (popup == null)
            {
                popup = new RoundedMenu { Font = Font };
                // WinForms dismisses menus before forwarding the owner's click.
                // Keep this dismissal for OnMouseDown so the click closes, rather than reopens, it.
                popup.Closing += delegate(object sender, ToolStripDropDownClosingEventArgs e)
                { if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked && ClientRectangle.Contains(PointToClient(Cursor.Position))) e.Cancel = true; };
            }
            if (popup.Visible) return;
            for (int i = popup.Items.Count - 1; i >= 0; i--) popup.Items[i].Dispose();
            popup.Items.Clear(); popup.MinimumSize = new Size(Width, 0);
            for (int i = 0; i < Items.Count; i++) { int choice = i; var item = popup.Items.Add(Theme.TranslateText(Items[i].ToString())); item.Click += delegate { SelectedIndex = choice; }; }
            // Closed is raised while ToolStrip still uses its native window. Dispose only with the owner.
            popup.Show(this, new Point(0, Height));
        }
        internal void PickForTest(int choice) { if (popup == null || !popup.Visible) throw new InvalidOperationException("Popup not open"); popup.Items[choice].PerformClick(); popup.Close(); }
        internal void ClosePopup() { if (popup != null) popup.Close(); }
        internal RoundedMenu PopupForTest { get { return popup; } }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible && popup != null) popup.Close(); base.OnVisibleChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled && popup != null) popup.Close(); Invalidate(); base.OnEnabledChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing && popup != null) { popup.Dispose(); popup = null; } base.Dispose(disposing); }
        internal void TogglePopup() { if (popup != null && popup.Visible) ClosePopup(); else Open(); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left) TogglePopup(); base.OnMouseDown(e); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || (e.Alt && e.KeyCode == Keys.Down)) { TogglePopup(); e.Handled = true; }
            else if (e.KeyCode == Keys.Up && index > 0) { SelectedIndex--; e.Handled = true; }
            else if (e.KeyCode == Keys.Down && index < Items.Count - 1) { SelectedIndex++; e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 9))
            using (var brush = new SolidBrush(Theme.Input)) using (var pen = new Pen(Focused ? Theme.Accent : Theme.Border))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(11, 0, Width - 40, Height), Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            LucideAssets.Draw(e.Graphics, "chevron-down", new Rectangle(Width - 27, Height / 2 - 8, 16, 16), Theme.Muted);
        }
    }
    sealed class BufferBar : Control
    {
        float current;
        public bool Animating { get { return false; } }
        public float Value { get { return current; } set { float next = Math.Max(0, Math.Min(1, value)); if (next == current) return; current = next; Invalidate(); } }
        public BufferBar() { BackColor = Theme.Card; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        public void FinishAnimation() { }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(0, 0, Width, Height), Height / 2f)) using (var brush = new SolidBrush(Theme.Input)) e.Graphics.FillPath(brush, path);
            if (current > 0) using (var path = Theme.Round(new RectangleF(0, 0, Math.Max(Height, Width * current), Height), Height / 2f)) using (var brush = new SolidBrush(Theme.Accent)) e.Graphics.FillPath(brush, path);
        }

    }
    sealed class StatusBadge : Control
    {
        Color current = Theme.Muted;
        public bool Animating { get { return false; } }
        public StatusBadge() { BackColor = Theme.Background; Font = new Font("Segoe UI", 9, FontStyle.Bold); SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        public void Set(string text, Color color) { if (Text == text && current == color) return; Text = text; current = color; Invalidate(); }
        public void FinishAnimation() { }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(0, 0, Width, Height), Height / 2f)) using (var brush = new SolidBrush(Theme.Mix(Theme.Background, current, .12f))) e.Graphics.FillPath(brush, path);
            using (var brush = new SolidBrush(current)) e.Graphics.FillEllipse(brush, 12, Height / 2 - 3, 6, 6);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(23, 0, Width - 28, Height), current, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

    }
    sealed class RoundedMenu : ContextMenuStrip
    {
        readonly List<Image> ownedImages = new List<Image>();
        public RoundedMenu()
        { Renderer = new DarkMenuRenderer(); BackColor = Theme.Input; ForeColor = Theme.Text; ShowImageMargin = false; ShowCheckMargin = false; }
        // MinimumSize stretches the window, but WinForms still lays out rows using
        // their text width. Include the requested width in the actual row metrics.
        protected override Size MaxItemSize
        { get { var size = base.MaxItemSize; size.Width = Math.Max(size.Width, MinimumSize.Width - 2); return size; } }
        internal int CornerRadius { get { return Math.Max(8, Font.Height / 2); } }
        void ClipCorners()
        {
            if (Width < 2 || Height < 2) return;
            using (var path = Theme.Round(new RectangleF(0, 0, Width, Height), CornerRadius))
            { var previous = Region; Region = new Region(path); if (previous != null) previous.Dispose(); }
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); ClipCorners(); }
        protected override void OnOpened(EventArgs e) { ClipCorners(); base.OnOpened(e); }
        internal Image AddIcon(MenuGlyph glyph)
        { var image = MenuIcons.Draw(ImageScalingSize.Width, glyph); ownedImages.Add(image); return image; }
        internal void RefreshIconColors()
        {
            var previous = ownedImages.ToArray(); ownedImages.Clear();
            foreach (ToolStripItem item in Items) if (item.Image != null && item.Image.Tag is MenuGlyph) item.Image = AddIcon((MenuGlyph)item.Image.Tag);
            foreach (var image in previous) image.Dispose();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (var image in ownedImages) image.Dispose(); ownedImages.Clear(); }
            base.Dispose(disposing);
        }
    }
    sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        internal static Color Selection { get { return Theme.Mix(Theme.Input, Theme.Accent, .13f); } }
        public DarkMenuRenderer() { RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Theme.Input); }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { using (var brush = new SolidBrush(Theme.Input)) e.Graphics.FillRectangle(brush, e.AffectedBounds); }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var smoothing = e.Graphics.SmoothingMode; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(3, 1, e.Item.Width - 6, e.Item.Height - 2), 5))
            using (var brush = new SolidBrush(Selection)) e.Graphics.FillPath(brush, path);
            e.Graphics.SmoothingMode = smoothing;
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted; base.OnRenderItemText(e); }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 9, e.Item.Height / 2, e.Item.Width - 9, e.Item.Height / 2); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var menu = e.ToolStrip as RoundedMenu; var smoothing = e.Graphics.SmoothingMode; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), menu == null ? 8 : menu.CornerRadius))
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawPath(pen, path);
            e.Graphics.SmoothingMode = smoothing;
        }
    }
    enum MenuGlyph { Open, Save, Record, Folder, Exit }
    static class MenuIcons
    {
        public static Bitmap Draw(int size, MenuGlyph glyph)
        {
            string[] names = { "external-link", "save", "circle-dot", "folder", "log-out" };
            var image = (Bitmap)LucideAssets.Get(names[(int)glyph], size, Theme.Text).Clone(); image.Tag = glyph; return image;
        }
    }
    enum TrayState { Idle, Recording, Saving, Error, Update }
    static class AppIcons
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        public static Bitmap Brand(int size) { return DrawCore(size, TrayState.Recording, false); }
        public static Bitmap Draw(int size, TrayState state) { return DrawCore(size, state, true); }
        static Bitmap DrawCore(int size, TrayState state, bool badge)
        {
            var image = new Bitmap(size, size);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color color = state == TrayState.Idle ? Theme.Muted : state == TrayState.Error ? Theme.Red : state == TrayState.Saving ? Theme.Blue : Theme.Accent;
                using (var path = Theme.Round(new RectangleF(0, 0, size, size), size * .25f)) using (var brush = new SolidBrush(Theme.Mix(Theme.Card, Theme.Accent, .1f))) graphics.FillPath(brush, path);
                int margin = Math.Max(1, size / 10); LucideAssets.Draw(graphics, "square-play", new Rectangle(margin, margin, size - 2 * margin, size - 2 * margin), color);
                if (badge)
                {
                    int diameter = Math.Max(7, (int)Math.Round(size * .4)); int x = size - diameter, y = size - diameter;
                    using (var brush = new SolidBrush(Theme.Background)) graphics.FillEllipse(brush, x - 1, y - 1, diameter + 2, diameter + 2);
                    using (var brush = new SolidBrush(color)) graphics.FillEllipse(brush, x, y, diameter, diameter);
                    string mark = state == TrayState.Idle ? "pause" : state == TrayState.Error ? "circle-alert" : state == TrayState.Saving ? "save" : state == TrayState.Update ? "arrow-up" : "circle-dot";
                    LucideAssets.Draw(graphics, mark, new Rectangle(x + 1, y + 1, diameter - 2, diameter - 2), Theme.Background);
                }
            }
            return image;
        }
        public static Icon Create(TrayState state)
        {
            using (var image = Draw(32, state))
            { IntPtr handle = image.GetHicon(); try { using (var borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); } finally { DestroyIcon(handle); } }
        }
        public static void Export(string folder)
        {
            Directory.CreateDirectory(folder);
            var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }; var data = new List<byte[]>();
            foreach (var size in sizes) using (var image = Brand(size)) using (var stream = new MemoryStream()) { image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); data.Add(stream.ToArray()); }
            using (var file = File.Create(Path.Combine(folder, "ReplayLite.ico"))) using (var writer = new BinaryWriter(file))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length); int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++) { writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(data[i].Length); writer.Write(offset); offset += data[i].Length; }
                foreach (var bytes in data) writer.Write(bytes);
            }
            foreach (TrayState state in Enum.GetValues(typeof(TrayState))) using (var image = Draw(128, state)) image.Save(Path.Combine(folder, "tray-" + state.ToString().ToLowerInvariant() + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
