using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace ReplayLite
{
    static class UpdateHelper
    {
        // The parent can exit between opening its PID and reading MainModule.
        internal static void WaitForTargetExit(Process process, string expectedPath, Func<string> executablePath)
        {
            try
            {
                if (process.HasExited) return;
                string actualPath = executablePath();
                if (actualPath == null)
                {
                    if (process.HasExited) return;
                    throw new IOException("Не удалось определить путь работающего ReplayLite. Закрой приложение и повтори обновление.");
                }
                if (!string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Обновляется другой процесс.");
                if (!process.WaitForExit(60000)) throw new IOException("ReplayLite не завершился. Повтори обновление после выхода.");
            }
            catch (InvalidOperationException) { if (!process.HasExited) throw; }
            catch (Win32Exception) { if (!process.HasExited) throw; }
        }

        [STAThread] static int Main(string[] args)
        {
            string root = null, stage = null;
            try
            {
                if (args.Length != 3) throw new IOException("Неверные параметры обновления.");
                int pid = int.Parse(args[0]); root = Path.GetFullPath(args[1]); stage = Path.GetFullPath(args[2]);
                if (!File.Exists(Path.Combine(root, "ReplayLite.exe")) || !stage.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\ReplayLite-update-", StringComparison.OrdinalIgnoreCase)) throw new IOException("Неверная папка обновления.");
                Process target;
                try { target = Process.GetProcessById(pid); } catch (ArgumentException) { target = null; }
                if (target != null)
                    using (target) WaitForTargetExit(target, Path.Combine(root, "ReplayLite.exe"), delegate { var module = target.MainModule; return module == null ? null : module.FileName; });
                Deployment.Apply(stage, root, File.Exists(Path.Combine(root, "portable.flag")));
                // Installed version metadata must follow the updated executable.
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ReplayLite", true)) if (key != null && string.Equals((string)key.GetValue("InstallLocation"), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) key.SetValue("DisplayVersion", File.ReadAllText(Path.Combine(root, "VERSION")).Trim());
                Deployment.Cleanup(stage); Process.Start(Path.Combine(root, "ReplayLite.exe")); return 0;
            }
            catch (Exception e) { MessageBox.Show("Обновление не завершено: " + e.Message + "\nНастройки и записи сохранены.", "ReplayLite", MessageBoxButtons.OK, MessageBoxIcon.Error); if (root != null && File.Exists(Path.Combine(root, "ReplayLite.exe"))) Process.Start(Path.Combine(root, "ReplayLite.exe")); return 1; }
        }
    }
}
