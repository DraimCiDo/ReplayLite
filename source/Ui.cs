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
    static class Theme
    {
        public static readonly Color Background = Color.FromArgb(13, 17, 24), Card = Color.FromArgb(22, 28, 39), Input = Color.FromArgb(30, 38, 51);
        public static readonly Color Border = Color.FromArgb(43, 53, 69), Text = Color.FromArgb(239, 244, 251), Muted = Color.FromArgb(143, 159, 181);
        public static readonly Color Accent = Color.FromArgb(76, 224, 178), Blue = Color.FromArgb(113, 168, 255), Red = Color.FromArgb(255, 131, 139);
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height)); var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        public static Color Mix(Color a, Color b, float t)
        { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
        public static bool CanAnimate(Control c)
        { var form = c.FindForm(); return c.Visible && form != null && form.Visible && form.WindowState != FormWindowState.Minimized; }
        public static void Canvas(Control c)
        { c.Font = new Font("Segoe UI", 10); c.ForeColor = Text; c.BackColor = Card; }
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
    sealed class ActionButton : Button
    {
        readonly Timer tween = new Timer { Interval = 16 };
        readonly Stopwatch clock = new Stopwatch();
        Color current = Theme.Input, from, target;
        Color fill = Theme.Input;
        bool hovered, pressed;
        public string Detail = "";
        public Color Fill { get { return fill; } set { fill = value; Transition(); } }
        public bool Animating { get { return tween.Enabled; } }
        public ActionButton()
        {
            Theme.Canvas(this); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            tween.Tick += delegate
            {
                if (!Theme.CanAnimate(this)) { FinishAnimation(); return; }
                float t = Math.Min(1, clock.ElapsedMilliseconds / 150f); t = 1 - (1 - t) * (1 - t);
                current = Theme.Mix(from, target, t); Invalidate(); if (clock.ElapsedMilliseconds >= 150) FinishAnimation();
            };
        }
        void Transition()
        {
            target = !Enabled ? Color.FromArgb(37, 44, 57) : pressed ? Theme.Mix(fill, Color.Black, 0.12f) : hovered ? Theme.Mix(fill, Color.White, 0.13f) : fill;
            if (!Theme.CanAnimate(this) || current == target) { FinishAnimation(); return; }
            from = current; clock.Restart(); tween.Start();
        }
        public void FinishAnimation() { tween.Stop(); current = target; Invalidate(); }
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
            var color = Enabled ? ForeColor : Color.FromArgb(133, 146, 165);
            if (Detail.Length == 0) TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            else
            {
                int y = Math.Max(4, (Height - 49) / 2);
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, y, Width, 25), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using (var small = new Font("Segoe UI", 8.5f)) TextRenderer.DrawText(e.Graphics, Detail, small, new Rectangle(0, y + 26, Width, 22), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (Focused && ShowFocusCues) { using (var path = Theme.Round(new RectangleF(4, 4, Width - 8, Height - 8), 9)) using (var pen = new Pen(Theme.Blue)) e.Graphics.DrawPath(pen, path); }
        }
        protected override void Dispose(bool disposing) { if (disposing) tween.Dispose(); base.Dispose(disposing); }
    }
    sealed class SelectBox : Control
    {
        public readonly List<object> Items = new List<object>();
        ContextMenuStrip popup;
        int index = -1;
        public int SelectedIndex
        {
            get { return index; }
            set { if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException("value"); index = value; Text = index < 0 ? "" : Items[index].ToString(); Invalidate(); }
        }
        public SelectBox()
        { Theme.Canvas(this); BackColor = Theme.Card; Cursor = Cursors.Hand; TabStop = true; AccessibleRole = AccessibleRole.ComboBox; SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        internal void Open()
        {
            if (!Enabled || Items.Count == 0) return;
            if (popup == null) popup = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), BackColor = Theme.Input, ForeColor = Theme.Text, Font = Font, ShowImageMargin = false };
            if (popup.Visible) return;
            popup.Items.Clear(); popup.MinimumSize = new Size(Width, 0);
            for (int i = 0; i < Items.Count; i++) { int choice = i; var item = popup.Items.Add(Items[i].ToString()); item.Click += delegate { SelectedIndex = choice; }; }
            // Closed is raised while ToolStrip still uses its native window. Dispose only with the owner.
            popup.Show(this, new Point(0, Height));
        }
        internal void PickForTest(int choice) { if (popup == null || !popup.Visible) throw new InvalidOperationException("Popup not open"); popup.Items[choice].PerformClick(); popup.Close(); }
        internal void ClosePopup() { if (popup != null) popup.Close(); }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible && popup != null) popup.Close(); base.OnVisibleChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled && popup != null) popup.Close(); Invalidate(); base.OnEnabledChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing && popup != null) { popup.Dispose(); popup = null; } base.Dispose(disposing); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left) Open(); base.OnMouseDown(e); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || (e.Alt && e.KeyCode == Keys.Down)) { Open(); e.Handled = true; }
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
            using (var pen = new Pen(Theme.Muted, 1.6f)) e.Graphics.DrawLines(pen, new[] { new Point(Width - 24, Height / 2 - 2), new Point(Width - 19, Height / 2 + 3), new Point(Width - 14, Height / 2 - 2) });
        }
    }
    sealed class BufferBar : Control
    {
        readonly Timer tween = new Timer { Interval = 16 }; readonly Stopwatch clock = new Stopwatch();
        float current, from, target;
        public bool Animating { get { return tween.Enabled; } }
        public float Value { get { return target; } set { var next = Math.Max(0, Math.Min(1, value)); if (next == target) return; target = next; if (!Theme.CanAnimate(this)) { FinishAnimation(); return; } from = current; clock.Restart(); tween.Start(); } }
        public BufferBar()
        {
            BackColor = Theme.Card; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            tween.Tick += delegate { if (!Theme.CanAnimate(this)) { FinishAnimation(); return; } float t = Math.Min(1, clock.ElapsedMilliseconds / 200f); current = from + (target - from) * (1 - (1 - t) * (1 - t)); Invalidate(); if (t >= 1) FinishAnimation(); };
        }
        public void FinishAnimation() { tween.Stop(); current = target; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(0, 0, Width, Height), Height / 2f)) using (var brush = new SolidBrush(Theme.Input)) e.Graphics.FillPath(brush, path);
            if (current > 0) using (var path = Theme.Round(new RectangleF(0, 0, Math.Max(Height, Width * current), Height), Height / 2f)) using (var brush = new SolidBrush(Theme.Accent)) e.Graphics.FillPath(brush, path);
        }
        protected override void Dispose(bool disposing) { if (disposing) tween.Dispose(); base.Dispose(disposing); }
    }
    sealed class StatusBadge : Control
    {
        readonly Timer tween = new Timer { Interval = 16 }; readonly Stopwatch clock = new Stopwatch();
        Color from, current = Theme.Muted, target = Theme.Muted;
        public bool Animating { get { return tween.Enabled; } }
        public StatusBadge() { BackColor = Theme.Background; Font = new Font("Segoe UI", 9, FontStyle.Bold); SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); tween.Tick += delegate { if (!Theme.CanAnimate(this)) { FinishAnimation(); return; } float t = Math.Min(1, clock.ElapsedMilliseconds / 180f); current = Theme.Mix(from, target, t); Invalidate(); if (t >= 1) FinishAnimation(); }; }
        public void Set(string text, Color color) { Text = text; if (target == color) { Invalidate(); return; } target = color; if (!Theme.CanAnimate(this)) { FinishAnimation(); return; } from = current; clock.Restart(); tween.Start(); }
        public void FinishAnimation() { tween.Stop(); current = target; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(0, 0, Width, Height), Height / 2f)) using (var brush = new SolidBrush(Theme.Mix(Theme.Background, current, .12f))) e.Graphics.FillPath(brush, path);
            using (var brush = new SolidBrush(current)) e.Graphics.FillEllipse(brush, 12, Height / 2 - 3, 6, 6);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(23, 0, Width - 28, Height), current, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        protected override void Dispose(bool disposing) { if (disposing) tween.Dispose(); base.Dispose(disposing); }
    }
    sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Theme.Input); }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) { if (e.Item.Selected) using (var brush = new SolidBrush(Color.FromArgb(45, 58, 76))) e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size)); }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted; base.OnRenderItemText(e); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
    }
    enum TrayState { Idle, Recording, Saving, Error }
    static class AppIcons
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        public static Bitmap Draw(int size, TrayState state)
        {
            var image = new Bitmap(size, size); using (var g = Graphics.FromImage(image))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.ScaleTransform(size / 64f, size / 64f);
                using (var path = Theme.Round(new RectangleF(2, 2, 60, 60), 16)) using (var brush = new SolidBrush(Color.FromArgb(23, 34, 46))) g.FillPath(brush, path);
                Color ring = state == TrayState.Idle ? Color.FromArgb(153, 172, 193) : state == TrayState.Error ? Theme.Red : state == TrayState.Saving ? Theme.Blue : Theme.Accent;
                using (var pen = new Pen(ring, 6) { StartCap = LineCap.Round, EndCap = LineCap.Flat }) g.DrawArc(pen, 14, 14, 36, 36, 35, 285);
                using (var brush = new SolidBrush(ring)) { g.FillPolygon(brush, new[] { new PointF(43, 8), new PointF(56, 20), new PointF(40, 24) }); g.FillEllipse(brush, 25, 25, 14, 14); }
                if (state != TrayState.Idle) { using (var brush = new SolidBrush(Theme.Background)) g.FillEllipse(brush, 43, 43, 20, 20); using (var brush = new SolidBrush(ring)) g.FillEllipse(brush, 47, 47, 12, 12); }
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
            foreach (var size in sizes) using (var image = Draw(size, TrayState.Recording)) using (var stream = new MemoryStream()) { image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); data.Add(stream.ToArray()); }
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
