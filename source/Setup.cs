using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ReplayLite
{
    static class SetupProgram
    {
        const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ReplayLite";
        [STAThread] static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
#if UNINSTALL
                string root = AppDomain.CurrentDomain.BaseDirectory;
                if (!File.Exists(Path.Combine(root, "installed.flag"))) throw new IOException("Portable удаляется вместе с папкой. Сначала сохрани нужные записи из Replays/.");
                if (MessageBox.Show("Удалить ReplayLite? Настройки и записи останутся.", "ReplayLite", MessageBoxButtons.OKCancel) != DialogResult.OK) return 0;
                if (Process.GetProcessesByName("ReplayLite").Length != 0) throw new IOException("Закрой ReplayLite через меню трея.");
                string manifest = Path.Combine(root, "package-files.txt");
                if (!File.Exists(manifest)) throw new IOException("Список файлов установки отсутствует.");
                foreach (string relative in File.ReadAllLines(manifest)) { string path = Deployment.Child(root, relative); if (path != Assembly.GetExecutingAssembly().Location && File.Exists(path)) File.Delete(path); }
                bool registered; using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath)) registered = key != null && string.Equals((string)key.GetValue("InstallLocation"), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                if (registered) { Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false); foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) }) { string shortcut = Path.Combine(folder, "ReplayLite.lnk"); if (File.Exists(shortcut)) File.Delete(shortcut); } }
                MessageBox.Show("ReplayLite удалён. Оставшуюся папку с Uninstall.exe можно удалить вручную.", "ReplayLite");
#else
                if (args.Length == 2 && args[0] == "--screenshot") { using (var form = new SetupForm()) { form.PreviewForDocs(); form.Show(); Application.DoEvents(); using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(args[1]); } } } else Application.Run(new SetupForm());
#endif
                return 0;
            }
            catch (Exception e) { MessageBox.Show(e.Message, "ReplayLite", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
        public static void Register(string root, bool desktop)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            { key.SetValue("DisplayName", "ReplayLite"); key.SetValue("DisplayVersion", File.ReadAllText(Path.Combine(root, "VERSION")).Trim()); key.SetValue("Publisher", "DraimCiDo"); key.SetValue("InstallLocation", root); key.SetValue("DisplayIcon", Path.Combine(root, "ReplayLite.exe")); key.SetValue("UninstallString", "\"" + Path.Combine(root, "Uninstall.exe") + "\""); key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1); }
            Shortcut(root, Environment.GetFolderPath(Environment.SpecialFolder.Programs)); if (desktop) Shortcut(root, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        }
        static void Shortcut(string root, string folder)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell"); dynamic shell = Activator.CreateInstance(type); dynamic link = shell.CreateShortcut(Path.Combine(folder, "ReplayLite.lnk"));
            try { link.TargetPath = Path.Combine(root, "ReplayLite.exe"); link.WorkingDirectory = root; link.IconLocation = link.TargetPath; link.Save(); }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
    }
#if !UNINSTALL
    sealed class SetupForm : Form
    {
        readonly RadioButton full = new RadioButton { Text = "Установить для текущего пользователя", Checked = true, AutoSize = true }, portable = new RadioButton { Text = "Portable — всё в выбранной папке", AutoSize = true };
        readonly TextBox path = new TextBox(); readonly CheckBox desktop = new CheckBox { Text = "Ярлык на рабочем столе", Checked = true, AutoSize = true };
        readonly ActionButton install = new ActionButton { Text = "Установить", Fill = Theme.Accent, ForeColor = Color.FromArgb(9, 37, 29), BackColor = Theme.Background }, browse = new ActionButton { Text = "Обзор…", BackColor = Theme.Background }; readonly Label status = new Label();
        public SetupForm()
        {
            Text = "Установка ReplayLite"; ClientSize = new Size(580, 325); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10); BackColor = Theme.Background; ForeColor = Theme.Text;
            var title = new Label { Text = "ReplayLite", Font = new Font("Segoe UI", 22, FontStyle.Bold) }; title.SetBounds(24, 15, 500, 48); Controls.Add(title);
            full.SetBounds(26, 76, 520, 28); portable.SetBounds(26, 111, 520, 28); Controls.Add(full); Controls.Add(portable);
            var pathShell = new CardPanel(); pathShell.SetBounds(26, 153, 427, 40); Controls.Add(pathShell); pathShell.Controls.Add(path);
            path.BorderStyle = BorderStyle.None; path.BackColor = Theme.Card; path.ForeColor = Theme.Text; path.SetBounds(12, 10, 400, 25);
            browse.SetBounds(465, 153, 88, 40); desktop.SetBounds(26, 203, 450, 25); install.SetBounds(380, 266, 173, 36); status.SetBounds(26, 243, 335, 70); status.ForeColor = Theme.Muted;
            Controls.AddRange(new Control[] { browse, desktop, install, status });
            path.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ReplayLite");
            portable.CheckedChanged += delegate { desktop.Enabled = !portable.Checked; install.Text = portable.Checked ? "Распаковать" : "Установить"; path.Text = portable.Checked ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ReplayLite") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ReplayLite"); };
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog { SelectedPath = path.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) path.Text = dialog.SelectedPath; };
            install.Click += async delegate { await Install(); };
        }
        async Task Install()
        {
            string stage = Path.Combine(Path.GetTempPath(), "ReplayLite-install-" + Guid.NewGuid().ToString("N"));
            try
            {
                string root = Path.GetFullPath(path.Text).TrimEnd('\\'); bool mode = portable.Checked, createDesktop = desktop.Checked;
                if (root == Path.GetPathRoot(root).TrimEnd('\\')) throw new IOException("Выбери отдельную папку ReplayLite.");
                if (Directory.Exists(root) && Directory.GetFileSystemEntries(root).Length != 0) throw new IOException("Для первой установки выбери пустую папку. Обновление доступно из приложения.");
                install.Enabled = browse.Enabled = full.Enabled = portable.Enabled = path.Enabled = desktop.Enabled = false; status.Text = "Распаковка…"; ControlBox = false;
                await Task.Run(delegate { using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")) { if (payload == null) throw new IOException("Пакет установки отсутствует."); Deployment.Extract(payload, stage); } Deployment.Apply(stage, root, mode); if (!mode) SetupProgram.Register(root, createDesktop); });
                status.Text = "Готово"; Process.Start(Path.Combine(root, "ReplayLite.exe")); Close();
            }
            catch (Exception e) { status.Text = e.Message; ControlBox = true; install.Enabled = browse.Enabled = full.Enabled = portable.Enabled = path.Enabled = true; desktop.Enabled = !portable.Checked; }
            finally { Deployment.Cleanup(stage); }
        }
        internal void PreviewForDocs() { path.Text = @"%LOCALAPPDATA%\Programs\ReplayLite"; }
    }
#endif
}
