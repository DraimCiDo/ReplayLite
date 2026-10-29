using System;
using System.Drawing;
using System.Windows.Forms;

namespace ReplayLite
{
    public sealed partial class MainForm
    {
        System.Drawing.Icon taskbarIcon;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        void RebuildBranding()
        {
            var previousIcons = icons; var previousBrand = brand;
            var fresh = new Icon[5]; for (int i = 0; i < fresh.Length; i++) fresh[i] = AppIcons.Create((TrayState)i);
            icons = fresh; brand = AppIcons.Brand(64); appLogo.Image = brand;
            var previousTaskbar = taskbarIcon;
            using (var image = AppIcons.Brand(48)) { IntPtr handle = image.GetHicon(); try { using (var borrowed = System.Drawing.Icon.FromHandle(handle)) taskbarIcon = (System.Drawing.Icon)borrowed.Clone(); } finally { DestroyIcon(handle); } }
            Icon = taskbarIcon; ShellIntegration.WindowIcon(this, taskbarIcon);
            if (!testing) try { ShellIntegration.PinnedTheme(taskbarIcon, Theme.PaletteName); } catch (System.IO.IOException) { } catch (UnauthorizedAccessException) { } catch (System.Runtime.InteropServices.COMException) { }
            if (previousTaskbar != null) previousTaskbar.Dispose();
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
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); WindowTheme.Apply(this); ShellIntegration.WindowIcon(this, taskbarIcon); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); WindowTheme.Apply(this); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); WindowTheme.Apply(this); }
    }
}
