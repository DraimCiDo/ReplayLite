using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ReplayLite
{
    static class Deployment
    {
        public static string Child(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":")) throw new IOException("Недопустимый путь в пакете.");
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(prefix, relative));
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Путь выходит за пределы папки.");
            string parent = Path.GetDirectoryName(path);
            while (parent.Length >= prefix.Length) { if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("Ссылки в папке установки не поддерживаются."); parent = Path.GetDirectoryName(parent); }
            return path;
        }
        static bool Reserved(string relative)
        { string name = relative.Replace('\\', '/'); return name.Equals("portable.flag", StringComparison.OrdinalIgnoreCase) || name.Equals("installed.flag", StringComparison.OrdinalIgnoreCase) || name.Equals("package-files.txt", StringComparison.OrdinalIgnoreCase) || name.Equals("settings.txt", StringComparison.OrdinalIgnoreCase) || name.StartsWith("data/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Replays/", StringComparison.OrdinalIgnoreCase); }
        public static string Extract(Stream stream, string stage)
        {
            Directory.CreateDirectory(stage); long total = 0; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
                foreach (var entry in zip.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith("ReplayLite/", StringComparison.Ordinal) || name.Length == 11) { if (name == "ReplayLite/") continue; throw new IOException("Неверный формат пакета."); }
                    string relative = name.Substring(11); string target = Child(stage, relative);
                    if (name.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                    if (!seen.Add(relative) || (total += entry.Length) > 1024L * 1024 * 1024) throw new IOException("Некорректный размер или дубликат в пакете.");
                    if (Reserved(relative)) { if (relative == "portable.flag") continue; throw new IOException("Пакет содержит пользовательские данные."); }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entry.Open()) using (var output = new FileStream(target, FileMode.CreateNew)) input.CopyTo(output, 65536);
                }
            if (!File.Exists(Path.Combine(stage, "ReplayLite.exe")) || !File.Exists(Path.Combine(stage, "UpdateHelper.exe")) || !File.Exists(Path.Combine(stage, "bin", "ffmpeg.exe"))) throw new IOException("Пакет неполный.");
            return stage;
        }
        public static void Apply(string stage, string root, bool portable)
        {
            if (Path.GetFullPath(stage).TrimEnd('\\').Equals(Path.GetFullPath(root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("Папки пакета и программы совпадают.");
            Directory.CreateDirectory(root);
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Установка в ссылку не поддерживается.");
            var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories).Select(p => p.Substring(stage.TrimEnd('\\').Length + 1)).Where(p => !Reserved(p)).ToList();
            files.Add("package-files.txt"); files.Add(portable ? "portable.flag" : "installed.flag");
            string backup = Path.Combine(Path.GetTempPath(), "ReplayLite-backup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(backup);
            var changed = new List<string>(); bool keepBackup = false;
            try
            {
                foreach (string relative in files)
                {
                    string destination = Child(root, relative), copy = Child(backup, relative);
                    if (File.Exists(destination)) { Directory.CreateDirectory(Path.GetDirectoryName(copy)); File.Copy(destination, copy); }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)); string pending = destination + ".new-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        if (relative == "package-files.txt") File.WriteAllLines(pending, files, new UTF8Encoding(false));
                        else if (relative.EndsWith(".flag")) File.WriteAllText(pending, "ReplayLite", new UTF8Encoding(false));
                        else File.Copy(Child(stage, relative), pending);
                        if (File.Exists(destination)) File.Replace(pending, destination, null); else File.Move(pending, destination);
                        changed.Add(relative);
                    }
                    finally { if (File.Exists(pending)) File.Delete(pending); }
                }
            }
            catch (Exception original)
            {
                foreach (string relative in changed.AsEnumerable().Reverse())
                {
                    try { string destination = Child(root, relative), copy = Child(backup, relative); if (File.Exists(copy)) File.Copy(copy, destination, true); else if (File.Exists(destination)) File.Delete(destination); }
                    catch (IOException) { keepBackup = true; } catch (UnauthorizedAccessException) { keepBackup = true; }
                }
                if (keepBackup) throw new IOException("Не удалось полностью восстановить файлы. Резервная копия: " + backup, original);
                throw;
            }
            finally { if (!keepBackup) Cleanup(backup); }
        }
        public static void Cleanup(string root)
        {
            // Delete only this application's uniquely named temporary trees.
            string full = Path.GetFullPath(root), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
            if (!full.StartsWith(temp + "ReplayLite-", StringComparison.OrdinalIgnoreCase)) return;
            try { Directory.Delete(full, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
