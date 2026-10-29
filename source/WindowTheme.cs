using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReplayLite
{
    static class WindowTheme
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        static int ColorRef(Color color) { return color.R | color.G << 8 | color.B << 16; }
        internal static bool Apply(Form form)
        {
            if (!form.IsHandleCreated) return false;
            int dark = Theme.Background.GetBrightness() < .5f ? 1 : 0, caption = ColorRef(Theme.Background), text = ColorRef(Theme.Text), border = ColorRef(Theme.Border), backdrop = 2;
            try
            {
                DwmSetWindowAttribute(form.Handle, 20, ref dark, 4);
                DwmSetWindowAttribute(form.Handle, 38, ref backdrop, 4);
                int result = DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(form.Handle, 36, ref text, 4); DwmSetWindowAttribute(form.Handle, 34, ref border, 4);
                return result >= 0;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }
}
