using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace ReplayLite
{
    sealed class NumberBox : UserControl
    {
        readonly TextBox editor = new TextBox();
        int minimum, maximum = 100, value;
        public int Minimum { get { return minimum; } set { minimum = value; Value = this.value; } }
        public int Maximum { get { return maximum; } set { maximum = value; Value = this.value; } }
        public int Value { get { Commit(); return value; } set { this.value = Math.Max(minimum, Math.Min(maximum, value)); editor.Text = this.value.ToString(CultureInfo.InvariantCulture); Invalidate(); } }
        public NumberBox()
        {
            Theme.Canvas(this); AccessibleRole = AccessibleRole.SpinButton; TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            editor.BorderStyle = BorderStyle.None; editor.BackColor = Theme.Input; editor.ForeColor = Theme.Text; editor.Font = Font; editor.Text = "0"; Controls.Add(editor);
            editor.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) { Value = Value + (e.KeyCode == Keys.Up ? 1 : -1); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; } };
            editor.KeyPress += delegate(object sender, KeyPressEventArgs e) { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            editor.Leave += delegate { Commit(); Invalidate(); }; editor.Enter += delegate { Invalidate(); };
        }
        void Commit() { int parsed; if (int.TryParse(editor.Text, out parsed)) value = Math.Max(minimum, Math.Min(maximum, parsed)); string text = value.ToString(CultureInfo.InvariantCulture); if (editor.Text != text) editor.Text = text; }
        protected override void OnSizeChanged(EventArgs e) { editor.SetBounds(12, Math.Max(3, (Height - editor.PreferredHeight) / 2), Math.Max(20, Width - 50), editor.PreferredHeight); base.OnSizeChanged(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (Enabled && e.Button == MouseButtons.Left) { if (e.X >= Width - 33) Value = Value + (e.Y < Height / 2 ? 1 : -1); else editor.Focus(); } base.OnMouseDown(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { if (ContainsFocus) Value = Value + Math.Sign(e.Delta); base.OnMouseWheel(e); }
        protected override void OnEnabledChanged(EventArgs e) { editor.ForeColor = Enabled ? Theme.Text : Theme.Muted; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 9)) using (var brush = new SolidBrush(Theme.Input)) using (var pen = new Pen(ContainsFocus ? Theme.Accent : Theme.Border)) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            int x = Width - 21;
            using (var pen = new Pen(Theme.Muted, 1.5f)) { e.Graphics.DrawLines(pen, new[] { new Point(x - 4, 15), new Point(x, 11), new Point(x + 4, 15) }); e.Graphics.DrawLines(pen, new[] { new Point(x - 4, Height - 15), new Point(x, Height - 11), new Point(x + 4, Height - 15) }); }
        }
    }
    public struct KeyBinding
    {
        public uint Modifiers; public Keys Key;
        public KeyBinding(uint modifiers, Keys key) { Modifiers = modifiers; Key = key; }
        public bool Valid { get { int key = (int)Key; return Modifiers > 0 && (Modifiers & ~7u) == 0 && Key != Keys.F12 && ((key >= (int)Keys.A && key <= (int)Keys.Z) || (key >= (int)Keys.D0 && key <= (int)Keys.D9) || (key >= (int)Keys.F1 && key <= (int)Keys.F24)); } }
        public override string ToString() { return ((Modifiers & 2) != 0 ? "Ctrl + " : "") + ((Modifiers & 1) != 0 ? "Alt + " : "") + ((Modifiers & 4) != 0 ? "Shift + " : "") + (Key >= Keys.D0 && Key <= Keys.D9 ? ((int)Key - (int)Keys.D0).ToString() : Key.ToString()); }
        public static KeyBinding Default { get { return new KeyBinding(6, Keys.F9); } }
    }
    sealed class BindingEventArgs : EventArgs
    { public readonly KeyBinding Binding; public bool Accepted; public BindingEventArgs(KeyBinding binding) { Binding = binding; } }
    sealed class HotkeyBox : Control
    {
        public event EventHandler CaptureStarted, CaptureCancelled;
        public event EventHandler<BindingEventArgs> BindingProposed;
        KeyBinding binding = KeyBinding.Default;
        public KeyBinding Binding { get { return binding; } set { binding = value; Invalidate(); } }
        public bool IsCapturing { get; private set; }
        public bool Error;
        public HotkeyBox() { Theme.Canvas(this); Cursor = Cursors.Hand; TabStop = true; AccessibleRole = AccessibleRole.Text; SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        internal void BeginCapture() { if (IsCapturing || !Enabled) return; IsCapturing = true; if (CaptureStarted != null) CaptureStarted(this, EventArgs.Empty); Invalidate(); }
        public void CancelCapture() { if (!IsCapturing) return; IsCapturing = false; if (CaptureCancelled != null) CaptureCancelled(this, EventArgs.Empty); Invalidate(); }
        internal bool Propose(KeyBinding binding)
        {
            if (!binding.Valid) return false;
            var args = new BindingEventArgs(binding); if (BindingProposed != null) BindingProposed(this, args);
            if (args.Accepted) { Binding = binding; IsCapturing = false; Error = false; Invalidate(); }
            return args.Accepted;
        }
        protected override void OnGotFocus(EventArgs e) { BeginCapture(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { CancelCapture(); base.OnLostFocus(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); BeginCapture(); base.OnMouseDown(e); }
        protected override bool IsInputKey(Keys key) { return (key & Keys.KeyCode) != Keys.Tab; }
        protected override void OnKeyDown(KeyEventArgs e)
        { e.SuppressKeyPress = true; if (!IsCapturing) return; if (e.KeyCode == Keys.Escape) { CancelCapture(); return; } uint modifiers = (e.Control ? 2u : 0u) | (e.Alt ? 1u : 0u) | (e.Shift ? 4u : 0u); Propose(new KeyBinding(modifiers, e.KeyCode)); base.OnKeyDown(e); }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) CancelCapture(); base.OnVisibleChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) CancelCapture(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 9)) using (var brush = new SolidBrush(Theme.Input)) using (var pen = new Pen(Error ? Theme.Red : IsCapturing ? Theme.Accent : Theme.Border)) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            TextRenderer.DrawText(e.Graphics, IsCapturing ? "Нажми сочетание…" : Binding.ToString(), Font, new Rectangle(11, 0, Width - 22, Height), Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
