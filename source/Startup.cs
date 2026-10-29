using System;
using System.IO;
using Microsoft.Win32;

namespace ReplayLite
{
    static class StartupRegistration
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal static string Command(string executable)
        {
            string command = "\"" + Path.GetFullPath(executable) + "\" --startup";
            if (command.Length > 260 || command.IndexOf('\r') >= 0 || command.IndexOf('\n') >= 0) throw new IOException(L.T("Путь программы слишком длинный для автозапуска Windows."));
            return command;
        }
        internal static bool Enabled(string executable, string registryPath = RunKey)
        { using (var key = Registry.CurrentUser.OpenSubKey(registryPath)) return key != null && string.Equals(key.GetValue("ReplayLite") as string, Command(executable), StringComparison.OrdinalIgnoreCase); }
        internal static void SetEnabled(string executable, bool enabled, string registryPath = RunKey)
        {
            string command = Command(executable);
            using (var key = enabled ? Registry.CurrentUser.CreateSubKey(registryPath) : Registry.CurrentUser.OpenSubKey(registryPath, true))
            {
                if (key == null) return;
                if (enabled) key.SetValue("ReplayLite", command, RegistryValueKind.String);
                else if (string.Equals(key.GetValue("ReplayLite") as string, command, StringComparison.OrdinalIgnoreCase)) key.DeleteValue("ReplayLite", false);
            }
        }
    }
}
