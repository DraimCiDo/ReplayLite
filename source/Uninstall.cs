using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ReplayLite
{
    // This manifest is also updated by UpdateHelper, so uninstall knows about
    // files introduced by updates after the first installation.
    static class UninstallProgram
    {
        const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ReplayLite";
        [STAThread] static int Main()
        {
            L.InitializeFromSettings(AppDomain.CurrentDomain.BaseDirectory); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                string root = AppDomain.CurrentDomain.BaseDirectory;
                if (!File.Exists(Path.Combine(root, "installed.flag"))) throw new IOException(L.T("Portable удаляется вместе с папкой. Сначала сохрани нужные записи из Replays/."));
                if (MessageBox.Show(L.T("Удалить ReplayLite? Настройки и записи останутся."), "ReplayLite", MessageBoxButtons.OKCancel) != DialogResult.OK) return 0;
                if (Process.GetProcessesByName("ReplayLite").Length != 0) throw new IOException(L.T("Закрой ReplayLite через меню трея."));
                string manifest = Path.Combine(root, "package-files.txt");
                if (!File.Exists(manifest)) throw new IOException(L.T("Список файлов установки отсутствует."));
                StartupRegistration.SetEnabled(Path.Combine(root, "ReplayLite.exe"), false);
                foreach (string relative in File.ReadAllLines(manifest)) { string path = Deployment.Child(root, relative); if (path != Assembly.GetExecutingAssembly().Location && File.Exists(path)) File.Delete(path); }
                bool registered; using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath)) registered = key != null && string.Equals((string)key.GetValue("InstallLocation"), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                if (registered) { Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false); foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) }) { string shortcut = Path.Combine(folder, "ReplayLite.lnk"); if (File.Exists(shortcut)) File.Delete(shortcut); } }
                MessageBox.Show(L.T("ReplayLite удалён. Оставшуюся папку с Uninstall.exe можно удалить вручную."), "ReplayLite");
                return 0;
            }
            catch (Exception e) { MessageBox.Show(e.Message, "ReplayLite", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
    }
}
