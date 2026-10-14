using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ReplayLite
{
    static class DeploymentTests
    {
        static void Assert(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine(name + ": PASS"); }
        static void Rejected(Action action, string name) { bool rejected = false; try { action(); } catch (IOException) { rejected = true; } Assert(rejected, name); }
        static void BadZip(string root, string name)
        { using (var memory = new MemoryStream()) { using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) { using (var writer = new StreamWriter(zip.CreateEntry("ReplayLite/" + name).Open())) writer.Write("bad"); } memory.Position = 0; Rejected(() => Deployment.Extract(memory, Path.Combine(root, "bad")), "ZIP_REJECT_" + name); } }
        static int Main(string[] args)
        {
            string temp = Path.Combine(Path.GetTempPath(), "ReplayLite-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            try
            {
                if (args.Length == 1 && args[0] == "--network")
                {
                    using (var client = Updates.Client())
                    {
                        var release = Updates.Parse(client.GetStringAsync("https://api.github.com/repos/DraimCiDo/ReplayLite/releases/latest").GetAwaiter().GetResult());
                        string zip = Path.Combine(temp, "latest.zip"), sums = client.GetStringAsync(release.Sums).GetAwaiter().GetResult();
                        Updates.Download(client, release.Zip, zip, System.Threading.CancellationToken.None, new QuietProgress()).GetAwaiter().GetResult(); Updates.Verify(zip, sums);
                        string networkStage = Path.Combine(temp, "stage"); using (var stream = File.OpenRead(zip)) Deployment.Extract(stream, networkStage);
                        Assert(FileVersionInfo.GetVersionInfo(Path.Combine(networkStage, "ReplayLite.exe")).FileVersion == release.Version.ToString(), "LIVE_RELEASE_DOWNLOAD_SHA256_VERSION"); Console.WriteLine("RELEASE_VERSION: " + release.Version); return 0;
                    }
                }
                string stage = Path.Combine(temp, "stage"), portable = Path.Combine(temp, "portable"), full = Path.Combine(temp, "full");
                using (var payload = File.OpenRead(args[1])) Deployment.Extract(payload, stage);
                Assert(FileVersionInfo.GetVersionInfo(args[0]).ProductName.Trim() == "ReplayLite", "SETUP_PRODUCT_METADATA");
                Deployment.Apply(stage, portable, true); Deployment.Apply(stage, full, false);
                Assert(File.Exists(Path.Combine(portable, "portable.flag")) && !File.Exists(Path.Combine(portable, "installed.flag")), "PORTABLE_MODE");
                Assert(File.Exists(Path.Combine(full, "installed.flag")) && !File.Exists(Path.Combine(full, "portable.flag")), "INSTALLED_MODE");
                foreach (string root in new[] { portable, full })
                {
                    Directory.CreateDirectory(Path.Combine(root, "data")); Directory.CreateDirectory(Path.Combine(root, "Replays"));
                    File.WriteAllText(Path.Combine(root, "data", "settings.txt"), "user settings"); File.WriteAllText(Path.Combine(root, "Replays", "keep.mp4"), "private clip");
                    File.WriteAllText(Path.Combine(root, "README.md"), "old version"); Deployment.Apply(stage, root, root == portable);
                    Assert(File.ReadAllText(Path.Combine(root, "data", "settings.txt")) == "user settings" && File.ReadAllText(Path.Combine(root, "Replays", "keep.mp4")) == "private clip" && File.ReadAllText(Path.Combine(root, "README.md")) != "old version", "UPDATE_PRESERVES_" + Path.GetFileName(root));
                }
                File.WriteAllText(Path.Combine(portable, "README.md"), "rollback sentinel");
                using (var locked = new FileStream(Path.Combine(portable, "bin", "ffmpeg.exe"), FileMode.Open, FileAccess.Read, FileShare.Read)) Rejected(() => Deployment.Apply(stage, portable, true), "UPDATE_LOCK_FAILURE");
                Assert(File.ReadAllText(Path.Combine(portable, "README.md")) == "rollback sentinel", "UPDATE_ROLLBACK");
                BadZip(temp, "../../escape.exe"); BadZip(temp, "data/settings.txt"); BadZip(temp, "C:/escape.exe");
                string zipFile = args[1], expected; using (var hash = SHA256.Create()) using (var file = File.OpenRead(zipFile)) expected = BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "");
                Updates.Verify(zipFile, expected + "  ReplayLite-Portable.zip"); Assert(true, "PACKAGE_SHA256"); Rejected(() => Updates.Verify(zipFile, new string('0', 64) + "  ReplayLite-Portable.zip"), "BAD_SHA256_REJECTED");
                string json = "{\"tag_name\":\"v0.3.0\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"ReplayLite-Portable.zip\",\"browser_download_url\":\"https://github.com/DraimCiDo/ReplayLite/releases/download/v0.3.0/ReplayLite-Portable.zip\"},{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"https://github.com/DraimCiDo/ReplayLite/releases/download/v0.3.0/SHA256SUMS.txt\"}]}";
                Assert(Updates.Parse(json).Version == new Version(0, 3, 0, 0), "RELEASE_API_PARSE"); Rejected(() => Updates.Parse(json.Replace("https://github.com/", "https://example.com/")), "EXTERNAL_ASSET_REJECTED");
                // Settings resolve storage from the marker in the actual installed executable.
                foreach (string root in new[] { portable, full })
                {
                    using (var domain = new SettingsDomain(root)) Assert(domain.Home == (root == portable ? Path.Combine(root, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReplayLite")), "SETTINGS_HOME_" + Path.GetFileName(root));
                }
                using (var domain = new SettingsDomain(portable)) domain.SavePortableFolder();
                Assert(File.ReadAllLines(Path.Combine(portable, "data", "settings.txt")).Contains("Folder=Replays"), "PORTABLE_RELATIVE_RECORDING_FOLDER");
                string helperStage = Path.Combine(Path.GetTempPath(), "ReplayLite-update-" + Guid.NewGuid().ToString("N")), helperRoot = Path.Combine(temp, "helper"); Directory.CreateDirectory(helperStage); Directory.CreateDirectory(helperRoot);
                try
                {
                    File.Copy(args[2], Path.Combine(helperRoot, "ReplayLite.exe")); File.Copy(args[3], Path.Combine(helperStage, "ReplayLite.exe")); File.WriteAllText(Path.Combine(helperRoot, "portable.flag"), "ReplayLite"); File.WriteAllText(Path.Combine(helperStage, "VERSION"), "2.0.0");
                    using (var old = Process.Start(new ProcessStartInfo(Path.Combine(helperRoot, "ReplayLite.exe")) { UseShellExecute = false, CreateNoWindow = true }))
                    using (var helper = Process.Start(new ProcessStartInfo(args[4], old.Id + " \"" + helperRoot + "\" \"" + helperStage + "\"") { UseShellExecute = false, CreateNoWindow = true }))
                    { Assert(helper.WaitForExit(15000) && helper.ExitCode == 0, "HELPER_WAIT_AND_APPLY"); }
                    var deadline = Stopwatch.StartNew(); while (deadline.ElapsedMilliseconds < 5000 && File.ReadAllText(Path.Combine(helperRoot, "launch.txt")) != "2.0.0.0") Thread.Sleep(50);
                    Assert(File.ReadAllText(Path.Combine(helperRoot, "launch.txt")) == "2.0.0.0", "HELPER_RESTART_NEW_VERSION");
                }
                finally { Deployment.Cleanup(helperStage); }
                Console.WriteLine("DEPLOYMENT_CHECKS_PASSED"); return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally { Deployment.Cleanup(temp); }
        }
        sealed class SettingsDomain : IDisposable
        {
            readonly AppDomain domain; readonly StorageProbe probe; readonly string appRoot; public string Home;
            public SettingsDomain(string root) { appRoot = root; domain = AppDomain.CreateDomain("settings", null, new AppDomainSetup { ApplicationBase = root }); probe = (StorageProbe)domain.CreateInstanceFromAndUnwrap(Assembly.GetExecutingAssembly().Location, typeof(StorageProbe).FullName); Home = probe.Get(root); }
            public void SavePortableFolder() { probe.SavePortableFolder(appRoot); }
            public void Dispose() { AppDomain.Unload(domain); }
        }
        sealed class QuietProgress : IProgress<int> { public void Report(int value) { } }
    }
    public sealed class StorageProbe : MarshalByRefObject
    {
        public string Get(string root) { var app = Assembly.LoadFrom(Path.Combine(root, "ReplayLite.exe")); return (string)app.GetType("ReplayLite.Settings").GetField("Home").GetValue(null); }
        public void SavePortableFolder(string root) { var type = Assembly.LoadFrom(Path.Combine(root, "ReplayLite.exe")).GetType("ReplayLite.Settings"); if (!(bool)type.GetField("Portable").GetValue(null)) throw new Exception("Test requires portable storage"); var settings = type.GetMethod("Load").Invoke(null, null); type.GetField("Folder").SetValue(settings, Path.Combine(root, "Replays")); type.GetMethod("Save").Invoke(settings, null); var loaded = type.GetMethod("Load").Invoke(null, null); if ((string)type.GetField("Folder").GetValue(loaded) != Path.Combine(root, "Replays")) throw new Exception("Relative folder did not resolve"); }
    }
}
