using System;
using System.Drawing;
using System.Windows.Forms;

namespace ReplayLite
{
    public sealed partial class MainForm
    {
        void RebuildBranding()
        {
            var previousIcons = icons; var previousBrand = brand;
            var fresh = new Icon[5]; for (int i = 0; i < fresh.Length; i++) fresh[i] = AppIcons.Create((TrayState)i);
            icons = fresh; brand = AppIcons.Brand(64); appLogo.Image = brand; Icon = icons[(int)TrayState.Recording];
            RefreshTrayIcon();
            var menu = tray.ContextMenuStrip as RoundedMenu; if (menu != null) menu.RefreshIconColors();
            if (previousIcons != null) foreach (var icon in previousIcons) icon.Dispose(); if (previousBrand != null) previousBrand.Dispose();
        }
        void RefreshTrayIcon()
        {
            if (icons == null) return;
            TrayState iconState = trayState == TrayState.Error || trayState == TrayState.Saving ? trayState : availableRelease != null ? TrayState.Update : trayState;
            tray.Icon = icons[(int)iconState];
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); WindowTheme.Apply(this); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); WindowTheme.Apply(this); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); WindowTheme.Apply(this); }
    }
}
