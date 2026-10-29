using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReplayLite
{
    public sealed partial class MainForm
    {
        [StructLayout(LayoutKind.Sequential)] struct WindowPosition { public IntPtr Window, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
        bool windowMoving;
        long nextMoveBoundsSync;
        internal long DeferredMoveNotifications { get; private set; }
        internal static bool PureWindowMove(uint flags) { return (flags & 0x0005) == 0x0005 && (flags & 0x00e2) == 0; }
        bool HandleWindowMotion(ref Message message)
        {
            if (message.Msg == 0x0231)
            {
                windowMoving = true; nextMoveBoundsSync = 0; hotkey.CancelCapture(); CloseSettingsPopups(); tips.Hide(this); StopAnimations();
            }
            else if (message.Msg == 0x0232)
            {
                windowMoving = false;
                // Reconcile the final native position before ResizeEnd and any user interaction.
                if (IsHandleCreated && !closing && !released) UpdateBounds();
            }
            else if (message.Msg == 0x0047 && windowMoving && message.LParam != IntPtr.Zero)
            {
                var position = (WindowPosition)Marshal.PtrToStructure(message.LParam, typeof(WindowPosition));
                if (PureWindowMove(position.Flags))
                {
                    long now = Stopwatch.GetTimestamp();
                    if (now < nextMoveBoundsSync)
                    {
                        // The native move has already happened. Defer only redundant WinForms bounds
                        // notifications; do not send WM_MOVE/WM_SIZE again through DefWindowProc.
                        DeferredMoveNotifications++; message.Result = IntPtr.Zero; return true;
                    }
                    nextMoveBoundsSync = now + Math.Max(1, Stopwatch.Frequency / 60);
                }
            }
            return false;
        }
    }
}
