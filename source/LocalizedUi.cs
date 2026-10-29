using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ReplayLite
{
    sealed class LocalizedUi : IDisposable
    {
        readonly Dictionary<Control, string> texts = new Dictionary<Control, string>(), names = new Dictionary<Control, string>();
        readonly Dictionary<ToolStripItem, string> menus = new Dictionary<ToolStripItem, string>();
        bool updating;
        internal static void Attach(Form form) { var instance = new LocalizedUi(); instance.Add(form); L.Changed += instance.Refresh; form.Disposed += delegate { instance.Dispose(); }; }
        internal static void Attach(Control root, Form lifetime) { var instance = new LocalizedUi(); instance.Add(root); L.Changed += instance.Refresh; lifetime.Disposed += delegate { instance.Dispose(); }; }
        bool Translatable(Control control) { return control is Label || control is Button || control is ToggleSwitch || control is SelectBox || control is Form; }
        void Add(Control control)
        {
            if (Translatable(control) && !texts.ContainsKey(control)) { texts.Add(control, L.Canonical(control.Text)); control.TextChanged += TextChanged; }
            if (!names.ContainsKey(control)) names.Add(control, L.Canonical(control.AccessibleName));
            control.ControlAdded += delegate(object sender, ControlEventArgs e) { Add(e.Control); Refresh(null, EventArgs.Empty); };
            var strip = control as ToolStrip; if (strip != null) { foreach (ToolStripItem item in strip.Items) AddMenu(item); strip.ItemAdded += delegate(object sender, ToolStripItemEventArgs e) { AddMenu(e.Item); }; }
            foreach (Control child in control.Controls) Add(child); Refresh(null, EventArgs.Empty);
        }
        void AddMenu(ToolStripItem item) { if (menus.ContainsKey(item)) return; menus[item] = L.Canonical(item.Text); item.TextChanged += delegate { if (!updating) { menus[item] = L.Canonical(item.Text); Refresh(null, EventArgs.Empty); } }; Refresh(null, EventArgs.Empty); }
        void TextChanged(object sender, EventArgs e) { if (updating) return; var control = (Control)sender; texts[control] = L.Canonical(control.Text); RefreshOne(control); }
        void RefreshOne(Control control) { updating = true; try { string text = L.T(texts[control]); if (control.Text != text) control.Text = text; } finally { updating = false; } }
        void Refresh(object sender, EventArgs e)
        {
            if (updating) return; updating = true;
            try
            {
                foreach (var pair in texts) if (!pair.Key.IsDisposed) { var select = pair.Key as SelectBox; if (select != null) select.ClosePopup(); string text = L.T(pair.Value); if (pair.Key.Text != text) pair.Key.Text = text; }
                foreach (var pair in names) if (!pair.Key.IsDisposed && pair.Value != null) pair.Key.AccessibleName = L.T(pair.Value);
                foreach (var pair in menus) if (!pair.Key.IsDisposed) { string text = L.T(pair.Value); if (pair.Key.Text != text) pair.Key.Text = text; }
            }
            finally { updating = false; }
        }
        public void Dispose() { L.Changed -= Refresh; foreach (var pair in texts) pair.Key.TextChanged -= TextChanged; texts.Clear(); names.Clear(); menus.Clear(); }
    }
}
