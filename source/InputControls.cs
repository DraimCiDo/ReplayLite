using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
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
            TextRenderer.DrawText(e.Graphics, IsCapturing ? "Нажми сочетание…" : Binding.ToString(), Font, new Rectangle(11, 0, Width - 22, Height), Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
