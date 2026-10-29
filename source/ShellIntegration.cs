using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReplayLite
{
    static class ShellIntegration
    {
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr value, IntPtr data);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern void SHChangeNotify(uint action, uint flags, string item, IntPtr other);
        internal static void WindowIcon(Form form, System.Drawing.Icon icon)
        { if (!form.IsHandleCreated || icon == null) return; SendMessage(form.Handle, 0x80, IntPtr.Zero, icon.Handle); SendMessage(form.Handle, 0x80, new IntPtr(1), icon.Handle); }
        internal static int UpdatePinnedIcons(string executable, string directory, string icon)
        {
            if (!Directory.Exists(directory)) return 0; int changed = 0; object shell = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell"); if (type == null) return 0; shell = Activator.CreateInstance(type);
                foreach (string path in Directory.EnumerateFiles(directory, "*.lnk", SearchOption.TopDirectoryOnly))
                {
                    object link = null;
                    try
                    {
                        link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path }); var linkType = link.GetType();
                        string target = Convert.ToString(linkType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null), CultureInfo.InvariantCulture);
                        if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase)) continue;
                        string location = icon + ",0"; string existing = Convert.ToString(linkType.InvokeMember("IconLocation", BindingFlags.GetProperty, null, link, null), CultureInfo.InvariantCulture);
                        if (string.Equals(location, existing, StringComparison.OrdinalIgnoreCase)) continue;
                        linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { location }); linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                        SHChangeNotify(0x2000, 0x0005, path, IntPtr.Zero); changed++;
                    }
                    catch (ArgumentException) { } catch (TargetInvocationException) { } catch (COMException) { }
                    finally { if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link); }
                }
            }
            finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
            return changed;
        }
        internal static void PinnedTheme(System.Drawing.Icon icon, string palette)
        {
            string home = Path.Combine(Settings.Home, "taskbar-icons"); Directory.CreateDirectory(home);
            string path = Path.Combine(home, "ReplayLite-" + palette + ".ico");
            using (var memory = new MemoryStream()) { icon.Save(memory); byte[] bytes = memory.ToArray(); bool same = File.Exists(path) && Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(bytes); if (!same) File.WriteAllBytes(path, bytes); }
            string pinned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            UpdatePinnedIcons(Application.ExecutablePath, pinned, path);
        }
        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
            void GetParent(out ShellItem parent); void GetDisplayName(uint kind, out IntPtr name); void GetAttributes(uint mask, out uint attributes); void Compare(ShellItem other, uint hint, out int order);
        }
        [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface FileOperation
        {
            void Advise(IntPtr sink, out uint cookie); void Unadvise(uint cookie); void SetOperationFlags(uint flags);
            void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string text); void SetProgressDialog(IntPtr dialog); void SetProperties(IntPtr properties); void SetOwnerWindow(IntPtr window);
            void ApplyPropertiesToItem(IntPtr item); void ApplyPropertiesToItems(IntPtr items); void RenameItem(IntPtr item, IntPtr name, IntPtr sink); void RenameItems(IntPtr items, IntPtr name);
            void MoveItem(IntPtr item, IntPtr destination, IntPtr name, IntPtr sink); void MoveItems(IntPtr items, IntPtr destination); void CopyItem(IntPtr item, IntPtr destination, IntPtr name, IntPtr sink); void CopyItems(IntPtr items, IntPtr destination);
            void DeleteItem(ShellItem item, IntPtr sink); void DeleteItems(IntPtr items); void NewItem(IntPtr destination, uint attributes, IntPtr name, IntPtr template, IntPtr sink);
            void PerformOperations(); void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] static extern void SHCreateItemFromParsingName(string path, IntPtr binding, ref Guid id, out ShellItem item);
        internal static void Recycle(string path, IntPtr owner)
        {
            if (!File.Exists(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(path)).DriveType != DriveType.Fixed) throw new IOException(L.T("Этот файл нельзя безопасно отправить в корзину."));
            ShellItem item = null; FileOperation operation = null;
            try
            {
                var id = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"); SHCreateItemFromParsingName(path, IntPtr.Zero, ref id, out item);
                operation = (FileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));
                operation.SetOwnerWindow(owner); operation.SetOperationFlags(0x00080000 | 0x20000000 | 0x00100000 | 0x00000400 | 0x00000010 | 0x00000004);
                operation.DeleteItem(item, IntPtr.Zero); operation.PerformOperations(); bool aborted; operation.GetAnyOperationsAborted(out aborted); if (aborted || File.Exists(path)) throw new IOException(L.T("Не удалось отправить клип в корзину."));
            }
            finally { if (operation != null) Marshal.FinalReleaseComObject(operation); if (item != null) Marshal.FinalReleaseComObject(item); }
        }
    }
    public sealed partial class MainForm
    {
        void DeleteClip(ClipEntry entry, Func<bool> confirm = null, Action<string> recycle = null)
        {
            if (confirm == null) confirm = () => MessageBox.Show(this, L.T("Отправить клип в корзину?\n") + entry.Name, "ReplayLite", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            if (!confirm()) return;
            ClipAction(entry, () => { if (recycle != null) recycle(entry.Path); else ShellIntegration.Recycle(entry.Path, Handle); libraryLoading = ReloadLibrary(); });
        }
    }
}
