using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReplayLite
{
    sealed class TextField : UserControl
    {
        readonly TextBox editor = new TextBox { BorderStyle = BorderStyle.None, MaxLength = 100 };
        public override string Text { get { return editor == null ? "" : editor.Text; } set { if (editor != null) editor.Text = value; } }
        public int MaxLength { get { return editor.MaxLength; } set { editor.MaxLength = value; } }
        public TextField()
        {
            Theme.Canvas(this); TabStop = false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            editor.BackColor = Theme.Input; editor.ForeColor = Theme.Text; Controls.Add(editor);
            editor.TextChanged += delegate { OnTextChanged(EventArgs.Empty); }; editor.KeyDown += delegate(object sender, KeyEventArgs e) { OnKeyDown(e); };
        }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (editor != null) { editor.Font = Font; LayoutEditor(); } }
        protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); if (editor != null) editor.ForeColor = ForeColor; }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); LayoutEditor(); }
        void LayoutEditor() { if (editor != null) editor.SetBounds(10, Math.Max(3, (Height - editor.PreferredHeight) / 2), Math.Max(1, Width - 20), editor.PreferredHeight); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); editor.Visible = Enabled; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 9))
            using (var fill = new SolidBrush(Theme.Input)) using (var line = new Pen(Theme.Border)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(line, path); }
            if (!Enabled) TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(10, 0, Width - 20, Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
    sealed class PathDisplay : Control
    {

        public PathDisplay() { Theme.Canvas(this); AccessibleRole = AccessibleRole.Text; TabStop = false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 9))
            using (var fill = new SolidBrush(Theme.Input)) using (var line = new Pen(Theme.Border)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(line, path); }
            LucideAssets.Draw(e.Graphics, "folder", new Rectangle(12, (Height - 16) / 2, 16, 16), Theme.Muted);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(37, 0, Width - 47, Height), Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
        }

    }
    sealed class SettingsScrollPanel : Panel
    {
        public readonly Panel Content = new Panel { BackColor = Theme.Card };
        readonly SettingsScrollThumb thumb = new SettingsScrollThumb();
        int contentHeight, offset;
        public int ContentHeight { get { return contentHeight; } set { contentHeight = Math.Max(0, value); LayoutContent(); } }
        internal int ScrollOffset { get { return offset; } }
        public override Rectangle DisplayRectangle { get { return new Rectangle(0, -offset, ClientSize.Width, Math.Max(ClientSize.Height, contentHeight)); } }
        public SettingsScrollPanel()
        {
            DoubleBuffered = true; BackColor = Theme.Card; Controls.Add(Content); Controls.Add(thumb);
            thumb.OffsetRequested += delegate(int value) { ScrollTo(value); };
        }
        void LayoutContent()
        {
            if (Content == null || thumb == null) return;
            offset = Math.Max(0, Math.Min(offset, Math.Max(0, contentHeight - ClientSize.Height)));
            Content.SetBounds(0, -offset, Math.Max(1, ClientSize.Width - 18), Math.Max(contentHeight, ClientSize.Height));
            thumb.SetBounds(Math.Max(0, ClientSize.Width - 14), 0, 14, ClientSize.Height);
            thumb.Visible = contentHeight > ClientSize.Height; thumb.SetMetrics(offset, contentHeight, ClientSize.Height);
        }
        public void ScrollTo(int value)
        { int next = Math.Max(0, Math.Min(value, Math.Max(0, contentHeight - ClientSize.Height))); if (next == offset) return; offset = next; LayoutContent(); }
        public void Reveal(Control control)
        { if (control.Top < offset) ScrollTo(control.Top); else if (control.Bottom > offset + ClientSize.Height) ScrollTo(control.Bottom - ClientSize.Height); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); LayoutContent(); }
        protected override void OnMouseWheel(MouseEventArgs e) { ScrollWheel(e); base.OnMouseWheel(e); }
        void ScrollWheel(MouseEventArgs e)
        { var handled = e as HandledMouseEventArgs; if (handled != null && handled.Handled) return; ScrollTo(offset - e.Delta * 48 / 120); if (handled != null) handled.Handled = true; }
        public void AttachWheelHandlers() { AttachWheel(Content); }
        void AttachWheel(Control parent)
        {
            parent.MouseWheel += delegate(object sender, MouseEventArgs e) { ScrollWheel(e); };
            foreach (Control child in parent.Controls) AttachWheel(child);
        }
    }
    sealed class SettingsScrollThumb : Control
    {
        int offset, total, viewport, dragOffset; bool dragging;
        public event Action<int> OffsetRequested;
        public SettingsScrollThumb() { BackColor = Theme.Card; AccessibleName = L.T("Прокрутка настроек"); AccessibleRole = AccessibleRole.ScrollBar; TabStop = true; SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        internal void SetMetrics(int value, int content, int visible) { offset = value; total = content; viewport = visible; Invalidate(); }
        Rectangle ThumbBounds
        {
            get { int length = Math.Min(Height, Math.Max(24, total == 0 ? Height : Height * viewport / total)); int top = Math.Max(0, total <= viewport ? 0 : (Height - length) * offset / (total - viewport)); return new Rectangle(4, top, 6, length); }
        }
        void Request(int value) { if (OffsetRequested != null) OffsetRequested(Math.Max(0, Math.Min(Math.Max(0, total - viewport), value))); }
        void DragTo(int y) { var bounds = ThumbBounds; Request((int)Math.Round((y - dragOffset) * (double)Math.Max(0, total - viewport) / Math.Max(1, Height - bounds.Height))); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; Focus(); dragging = true; var bounds = ThumbBounds; dragOffset = bounds.Contains(e.Location) ? e.Y - bounds.Top : bounds.Height / 2; Capture = true; DragTo(e.Y); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) DragTo(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) { dragging = false; Capture = false; } }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
        protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || key == Keys.PageUp || key == Keys.PageDown || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e); if (e.KeyCode == Keys.Home) Request(0); else if (e.KeyCode == Keys.End) Request(total);
            else if (e.KeyCode == Keys.Up) Request(offset - 32); else if (e.KeyCode == Keys.Down) Request(offset + 32);
            else if (e.KeyCode == Keys.PageUp) Request(offset - viewport); else if (e.KeyCode == Keys.PageDown) Request(offset + viewport); else return; e.Handled = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var track = new Pen(Theme.Input, 3)) { track.StartCap = track.EndCap = LineCap.Round; e.Graphics.DrawLine(track, 7, 3, 7, Math.Max(3, Height - 3)); }
            var bounds = ThumbBounds; if (bounds.Height < 2) return;
            using (var path = Theme.Round(bounds, 3)) using (var brush = new SolidBrush(Theme.Accent)) e.Graphics.FillPath(brush, path);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, Theme.Muted, BackColor);
        }
    }
    // No animation or polling: repaint only on input, commit only when editing ends.
    sealed class ValueSlider : Control
    {
        int value, minimum, maximum = 100;
        public event EventHandler ValueChanged, ValueCommitted;
        public int Minimum { get { return minimum; } set { minimum = value; Value = this.value; } }
        public int Maximum { get { return maximum; } set { maximum = value; Value = this.value; } }
        public int Value { get { return value; } set { int next = Math.Max(minimum, Math.Min(maximum, value)); if (next == this.value) return; this.value = next; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); } }
        public ValueSlider() { Theme.Canvas(this); AccessibleRole = AccessibleRole.Slider; TabStop = true; Height = 28; SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        void Pick(int x) { Value = minimum + (int)Math.Round((maximum - minimum) * Math.Max(0, Math.Min(1, (x - 9.0) / Math.Max(1, Width - 18)))); }
        void Commit() { if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button != MouseButtons.Left || !Enabled) return; Focus(); Capture = true; Pick(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) Pick(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left && Capture) { Pick(e.X); Capture = false; Commit(); } }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) Commit(); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Home) Value = minimum; else if (e.KeyCode == Keys.End) Value = maximum;
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value--; else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value++; else return;
            e.Handled = true;
        }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); Commit(); }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); Commit(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float y = Height / 2f, x = 9 + (Width - 18) * (value - minimum) / (float)Math.Max(1, maximum - minimum);
            using (var pen = new Pen(Theme.Input, 4)) e.Graphics.DrawLine(pen, 9, y, Width - 9, y);
            if (x > 9) using (var pen = new Pen(Enabled ? Theme.Accent : Theme.Muted, 4)) e.Graphics.DrawLine(pen, 9, y, x, y);
            using (var brush = new SolidBrush(Enabled ? Theme.Accent : Theme.Muted)) e.Graphics.FillEllipse(brush, x - 7, y - 7, 14, 14);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, Theme.Text, BackColor);
        }
    }
    sealed class RangeBox : UserControl
    {
        readonly ValueSlider slider = new ValueSlider(); readonly NumberBox number = new NumberBox();
        bool synchronizing; int committed;
        public event EventHandler ValueCommitted;
        public int Minimum { get { return slider.Minimum; } set { slider.Minimum = number.Minimum = value; } }
        public int Maximum { get { return slider.Maximum; } set { slider.Maximum = number.Maximum = value; } }
        public int Value { get { return number.Value; } set { synchronizing = true; slider.Value = number.Value = value; committed = number.Value; synchronizing = false; } }
        public RangeBox()
        {
            Theme.Canvas(this); Height = 36; Controls.Add(slider); Controls.Add(number);
            slider.ValueChanged += delegate { if (!synchronizing) { synchronizing = true; number.Value = slider.Value; synchronizing = false; } };
            number.ValueChanged += delegate { if (!synchronizing) { synchronizing = true; slider.Value = number.Value; synchronizing = false; Commit(); } };
            slider.ValueCommitted += delegate { Commit(); };
        }
        void Commit() { if (synchronizing || committed == number.Value) return; committed = number.Value; if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); slider.SetBounds(0, 3, Math.Max(20, Width - 108), 30); number.SetBounds(Math.Max(20, Width - 100), 0, 100, 36); }
        internal void SetInputName(string name) { AccessibleName = slider.AccessibleName = name; number.SetInputName(name); }
        internal void EditForTest(int value) { slider.Value = value; Commit(); }
    }
    sealed class NumberBox : UserControl
    {
        readonly TextBox editor = new TextBox();
        int minimum, maximum = 100, value;
        public event EventHandler ValueChanged;
        public int Minimum { get { return minimum; } set { minimum = value; Value = this.value; } }
        public int Maximum { get { return maximum; } set { maximum = value; Value = this.value; } }
        public int Value { get { Commit(); return value; } set { int previous = this.value; this.value = Math.Max(minimum, Math.Min(maximum, value)); editor.Text = this.value.ToString(CultureInfo.InvariantCulture); Invalidate(); if (previous != this.value && ValueChanged != null) ValueChanged(this, EventArgs.Empty); } }
        public NumberBox()
        {
            Theme.Canvas(this); AccessibleRole = AccessibleRole.SpinButton; TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            editor.BorderStyle = BorderStyle.None; editor.BackColor = Theme.Input; editor.ForeColor = Theme.Text; editor.Font = Font; editor.Text = "0"; Controls.Add(editor);
            editor.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) { Value = Value + (e.KeyCode == Keys.Up ? 1 : -1); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; } };
            editor.KeyPress += delegate(object sender, KeyPressEventArgs e) { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            editor.Leave += delegate { Commit(); Invalidate(); }; editor.Enter += delegate { Invalidate(); };
        }
        void Commit() { int previous = value, parsed; if (int.TryParse(editor.Text, out parsed)) value = Math.Max(minimum, Math.Min(maximum, parsed)); string text = value.ToString(CultureInfo.InvariantCulture); if (editor.Text != text) editor.Text = text; if (previous != value && ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); if (editor != null) editor.BackColor = Theme.Input; }
        protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); if (editor != null) editor.ForeColor = ForeColor; }
        internal void SetInputName(string name) { AccessibleName = editor.AccessibleName = name; }
        protected override void OnSizeChanged(EventArgs e) { editor.SetBounds(12, Math.Max(3, (Height - editor.PreferredHeight) / 2), Math.Max(20, Width - 50), editor.PreferredHeight); base.OnSizeChanged(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (Enabled && e.Button == MouseButtons.Left) { if (e.X >= Width - 33) Value = Value + (e.Y < Height / 2 ? 1 : -1); else editor.Focus(); } base.OnMouseDown(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { bool inSettings = false; for (Control parent = Parent; parent != null; parent = parent.Parent) if (parent is SettingsScrollPanel) { inSettings = true; break; } if (ContainsFocus && !inSettings) Value = Value + Math.Sign(e.Delta); base.OnMouseWheel(e); }
        protected override void OnEnabledChanged(EventArgs e) { editor.Visible = Enabled; editor.ForeColor = Theme.Text; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 9)) using (var brush = new SolidBrush(Theme.Input)) using (var pen = new Pen(ContainsFocus ? Theme.Accent : Theme.Border)) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            int x = Width - 21;
            if (!Enabled) TextRenderer.DrawText(e.Graphics, value.ToString(CultureInfo.InvariantCulture), Font, new Rectangle(12, 0, Width - 50, Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            LucideAssets.Draw(e.Graphics, "chevron-up", new Rectangle(x - 6, 6, 12, 12), Theme.Muted); LucideAssets.Draw(e.Graphics, "chevron-down", new Rectangle(x - 6, Height - 18, 12, 12), Theme.Muted);
        }
    }
    public struct KeyBinding
    {
        public uint Modifiers; public Keys Key;
        public KeyBinding(uint modifiers, Keys key) { Modifiers = modifiers; Key = key; }
        public bool Valid { get { int key = (int)Key; return (Modifiers & ~15u) == 0 && key >= 8 && key < 255 && Enum.IsDefined(typeof(Keys), Key) && Key != Keys.F12 && !ModifierKey(Key); } }
        internal static bool ModifierKey(Keys key) { return key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin || (key >= Keys.LShiftKey && key <= Keys.RMenu); }
        static string Name(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Num " + ((int)key - (int)Keys.NumPad0);
            switch (key) { case Keys.Return: return "Enter"; case Keys.Back: return "Backspace"; case Keys.Capital: return "Caps Lock"; case Keys.Prior: return "Page Up"; case Keys.Next: return "Page Down"; case Keys.Snapshot: return "Print Screen"; case Keys.Multiply: return "Num *"; case Keys.Add: return "Num +"; case Keys.Subtract: return "Num -"; case Keys.Divide: return "Num /"; case Keys.Decimal: return "Num ."; case Keys.Oemplus: return "="; case Keys.OemMinus: return "-"; case Keys.Oemcomma: return ","; case Keys.OemPeriod: return "."; default: return key.ToString(); }
        }
        public override string ToString() { return ((Modifiers & 2) != 0 ? "Ctrl + " : "") + ((Modifiers & 1) != 0 ? "Alt + " : "") + ((Modifiers & 4) != 0 ? "Shift + " : "") + ((Modifiers & 8) != 0 ? "Win + " : "") + Name(Key); }
        public static KeyBinding Default { get { return new KeyBinding(6, Keys.F9); } }
    }
    sealed class BindingEventArgs : EventArgs
    { public readonly KeyBinding Binding; public bool Accepted; public BindingEventArgs(KeyBinding binding) { Binding = binding; } }
    sealed class HotkeyBox : Control
    {
        public event EventHandler CaptureStarted, CaptureCancelled;
        public event EventHandler CaptureFailed;
        public event EventHandler<BindingEventArgs> BindingProposed;
        KeyBinding binding = KeyBinding.Default;
        public KeyBinding Binding { get { return binding; } set { binding = value; Invalidate(); } }
        public bool IsCapturing { get; private set; }
        public bool Error;
        KeyboardCapture capture;
        internal bool CaptureHookActive { get { return capture != null && capture.Active; } }
        public HotkeyBox() { Theme.Canvas(this); Cursor = Cursors.Hand; TabStop = true; AccessibleRole = AccessibleRole.Text; SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        internal void BeginCapture() { if (IsCapturing || !Enabled) return; IsCapturing = true; if (CaptureStarted != null) CaptureStarted(this, EventArgs.Empty); Listen(); Invalidate(); }
        void Listen()
        {
            try { if (capture != null) capture.Dispose(); capture = new KeyboardCapture(this, delegate(KeyBinding proposed) { if (!IsCapturing || IsDisposed) return; if (!Propose(proposed)) Listen(); }); }
            catch (System.ComponentModel.Win32Exception) { CancelCapture(); Error = true; if (CaptureFailed != null) CaptureFailed(this, EventArgs.Empty); Invalidate(); }
        }
        public void CancelCapture() { if (capture != null) { capture.Dispose(); capture = null; } if (!IsCapturing) return; IsCapturing = false; if (CaptureCancelled != null) CaptureCancelled(this, EventArgs.Empty); Invalidate(); }
        internal bool Propose(KeyBinding binding)
        {
            var args = new BindingEventArgs(binding); if (BindingProposed != null) BindingProposed(this, args);
            if (args.Accepted) { if (capture != null) { capture.Dispose(); capture = null; } Binding = binding; IsCapturing = false; Error = false; Invalidate(); }
            return args.Accepted;
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { CancelCapture(); base.OnLostFocus(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); BeginCapture(); base.OnMouseDown(e); }
        protected override bool IsInputKey(Keys key) { var code = key & Keys.KeyCode; return IsCapturing || code == Keys.Enter || code == Keys.Space || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e)
        { if (!IsCapturing && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)) { e.SuppressKeyPress = true; BeginCapture(); } base.OnKeyDown(e); }
        protected override void Dispose(bool disposing) { if (disposing) CancelCapture(); base.Dispose(disposing); }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) CancelCapture(); base.OnVisibleChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) CancelCapture(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 9)) using (var brush = new SolidBrush(Theme.Input)) using (var pen = new Pen(Error ? Theme.Red : IsCapturing ? Theme.Accent : Theme.Border)) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            TextRenderer.DrawText(e.Graphics, IsCapturing ? L.T("Нажми сочетание…") : Binding.ToString(), Font, new Rectangle(11, 0, Width - 22, Height), Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
    // Capture only while editing. Normal global shortcuts continue to use RegisterHotKey without polling or hooks.
    sealed class KeyCaptureState
    {
        readonly bool[] down = new bool[256], owned = new bool[256]; KeyBinding? candidate; bool released;
        static int Normalize(int key) { return key == 16 ? 160 : key == 17 ? 162 : key == 18 ? 164 : key; }
        public void Seed(int key) { if (key >= 8 && key < 255 && key != 16 && key != 17 && key != 18) down[key] = true; }
        uint Modifiers { get { return (down[162] || down[163] ? 2u : 0u) | (down[164] || down[165] ? 1u : 0u) | (down[160] || down[161] ? 4u : 0u) | (down[91] || down[92] ? 8u : 0u); } }
        public bool Feed(int key, bool pressed, out KeyBinding? complete)
        {
            complete = null; key = Normalize(key); if (key < 8 || key >= 255) return false;
            bool block = owned[key];
            if (pressed)
            {
                if (!down[key]) { down[key] = owned[key] = true; block = true; if (!KeyBinding.ModifierKey((Keys)key)) { candidate = new KeyBinding(Modifiers, (Keys)key); released = false; } }
            }
            else { down[key] = owned[key] = false; if (candidate.HasValue && (int)candidate.Value.Key == key) released = true; }
            if (released) { bool held = false; for (int i = 8; i < 255; i++) if (down[i]) { held = true; break; } if (!held) { complete = candidate; candidate = null; released = false; } }
            return block;
        }
    }
    sealed class KeyboardCapture : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int type, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        readonly Control owner; readonly IntPtr window; readonly Action<KeyBinding> proposed; readonly KeyCaptureState state = new KeyCaptureState(); readonly HookProc proc; IntPtr hook;
        public bool Active { get { return hook != IntPtr.Zero; } }
        public KeyboardCapture(Control owner, Action<KeyBinding> proposed)
        {
            this.owner = owner; this.proposed = proposed; proc = Callback;
            var form = owner.FindForm(); window = form == null ? owner.Handle : form.Handle;
            for (int key = 8; key < 255; key++) if ((GetAsyncKeyState(key) & 0x8000) != 0) state.Seed(key);
            hook = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0); if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        }
        IntPtr Callback(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || owner.IsDisposed || !owner.Focused || GetForegroundWindow() != window) return CallNextHookEx(hook, code, message, data);
            int msg = message.ToInt32(); if (msg != 0x100 && msg != 0x104 && msg != 0x101 && msg != 0x105) return CallNextHookEx(hook, code, message, data);
            KeyBinding? complete; bool block = state.Feed(Marshal.ReadInt32(data), msg == 0x100 || msg == 0x104, out complete);
            if (complete.HasValue)
            {
                var value = complete.Value; Dispose();
                // Registration, persistence and repaint happen after returning from the native hook.
                try { if (owner.IsHandleCreated) owner.BeginInvoke(new Action(delegate { if (!owner.IsDisposed) proposed(value); })); } catch (InvalidOperationException) { }
            }
            return block ? new IntPtr(1) : CallNextHookEx(hook, code, message, data);
        }
        public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
    }
}
