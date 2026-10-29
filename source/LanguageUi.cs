using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ReplayLite
{
    public sealed partial class MainForm
    {
        readonly Dictionary<Control, string> localizedTips = new Dictionary<Control, string>();
        string trayCaption = "ReplayLite";
        void Tip(Control control, string text)
        { localizedTips[control] = L.Canonical(text); tips.SetToolTip(control, control is PathDisplay || control == folder || control == openFolder ? text : L.T(text)); }
        void SetTrayCaption(string text)
        { trayCaption = L.Canonical(text); string translated = L.T(trayCaption); tray.Text = translated.Substring(0, Math.Min(63, translated.Length)); }
        void LanguageChanged(object sender, EventArgs e)
        {
            if (released || closing) return;
            settings.Language = L.Language;
            foreach (var pair in localizedTips) if (!pair.Key.IsDisposed) tips.SetToolTip(pair.Key, pair.Key is PathDisplay || pair.Key == folder || pair.Key == openFolder ? pair.Value : L.T(pair.Value));
            foreach (var select in new[] { duration, fps, sound, display, microphoneDevice, containerChoice, codecChoice, resolutionChoice, controlChoice, themeChoice, toneChoice, languageChoice }) if (select != null) select.SelectedIndex = select.SelectedIndex;
            badge.Set(L.T(L.Canonical(badge.Text)), trayState == TrayState.Recording ? Theme.Accent : trayState == TrayState.Saving ? Theme.Blue : trayState == TrayState.Error ? Theme.Red : Theme.Muted);
            SetTrayCaption(trayCaption); UpdateOptionsSummary(); SetAvailableRelease(availableRelease);
            if (advancedOptions != null) SelectSettingsTab(settingsTab);
        }
    }
}
