using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace ReplayLite
{
    static class UpdateHelper
    {
        [STAThread] static int Main(string[] args)
        {
            string root = null, stage = null;
            try
            {
                if (args.Length != 3) throw new IOException("Неверные параметры обновления.");
                int pid = int.Parse(args[0]); root = Path.GetFullPath(args[1]); stage = Path.GetFullPath(args[2]);
                if (!File.Exists(Path.Combine(root, "ReplayLite.exe")) || !stage.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\ReplayLite-update-", StringComparison.OrdinalIgnoreCase)) throw new IOException("Неверная папка обновления.");
                try { using (var process = Process.GetProcessById(pid)) { if (!string.Equals(process.MainModule.FileName, Path.Combine(root, "ReplayLite.exe"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Обновляется другой процесс."); if (!process.WaitForExit(60000)) throw new IOException("ReplayLite не завершился. Повтори обновление после выхода."); } } catch (ArgumentException) { }
                Deployment.Apply(stage, root, File.Exists(Path.Combine(root, "portable.flag")));
                // Installed version metadata must follow the updated executable.
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ReplayLite", true)) if (key != null && string.Equals((string)key.GetValue("InstallLocation"), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) key.SetValue("DisplayVersion", File.ReadAllText(Path.Combine(root, "VERSION")).Trim());
                Deployment.Cleanup(stage); Process.Start(Path.Combine(root, "ReplayLite.exe")); return 0;
            }
            catch (Exception e) { MessageBox.Show("Обновление не завершено: " + e.Message + "\nНастройки и записи сохранены.", "ReplayLite", MessageBoxButtons.OK, MessageBoxIcon.Error); if (root != null && File.Exists(Path.Combine(root, "ReplayLite.exe"))) Process.Start(Path.Combine(root, "ReplayLite.exe")); return 1; }
        }
    }
}
