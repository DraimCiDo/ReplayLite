using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ReplayLite
{
    sealed class ReleaseInfo { public Version Version; public string Zip, Sums; }
    static class Updates
    {
        const string ReleaseRoot = "https://github.com/DraimCiDo/ReplayLite/releases/download/";
        public static HttpClient Client()
        { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) }; client.DefaultRequestHeaders.UserAgent.ParseAdd("ReplayLite/" + Assembly.GetExecutingAssembly().GetName().Version); client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28"); return client; }
        public static async Task<ReleaseInfo> Latest(CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var client = Client())
            {
                timeout.CancelAfter(15000);
                using (var response = await client.GetAsync("https://api.github.com/repos/DraimCiDo/ReplayLite/releases/latest", timeout.Token))
                { response.EnsureSuccessStatusCode(); return Parse(await response.Content.ReadAsStringAsync()); }
            }
        }
        public static ReleaseInfo Parse(string json)
        {
            var value = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
            Version version; string tag = (string)value["tag_name"];
            if (!tag.StartsWith("v") || !Version.TryParse(tag.Substring(1), out version) || (bool)value["draft"] || (bool)value["prerelease"]) throw new IOException("Неверный релиз.");
            var release = new ReleaseInfo { Version = new Version(version.Major, version.Minor, version.Build, 0) };
            foreach (Dictionary<string, object> asset in (IEnumerable)value["assets"])
            {
                string name = (string)asset["name"], url = (string)asset["browser_download_url"];
                if (!url.Equals(ReleaseRoot + tag + "/" + name, StringComparison.Ordinal)) continue;
                if (name == "ReplayLite-Portable.zip") release.Zip = url; if (name == "SHA256SUMS.txt") release.Sums = url;
            }
            if (release.Zip == null || release.Sums == null) throw new IOException("В релизе отсутствует пакет обновления.");
            return release;
        }
        public static void Verify(string zip, string sums)
        {
            string expected = null;
            foreach (string line in sums.Split('\n')) { string[] parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); if (parts.Length == 2 && parts[1] == "ReplayLite-Portable.zip") expected = parts[0]; }
            string actual; using (var hash = SHA256.Create()) using (var file = File.OpenRead(zip)) actual = BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "");
            if (expected == null || !actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Контрольная сумма не совпала. Файлы программы не изменены.");
        }
        public static async Task Download(HttpClient client, string url, string file, CancellationToken token, IProgress<int> progress)
        {
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode(); long total = response.Content.Headers.ContentLength ?? 0, read = 0; int previous = -1;
                if (total > 512L * 1024 * 1024) throw new IOException("Пакет слишком большой.");
                using (var input = await response.Content.ReadAsStreamAsync()) using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                { var buffer = new byte[65536]; int count; while ((count = await input.ReadAsync(buffer, 0, buffer.Length, token)) > 0) { read += count; if (read > 512L * 1024 * 1024) throw new IOException("Пакет слишком большой."); await output.WriteAsync(buffer, 0, count, token); int percent = total > 0 ? (int)(read * 100 / total) : 0; if (percent != previous) { progress.Report(percent); previous = percent; } } }
            }
        }
    }
    sealed class UpdateForm : Form
    {
        readonly Label status = new Label(); readonly ActionButton button = new ActionButton { Fill = Theme.Accent, ForeColor = Color.FromArgb(9, 37, 29), BackColor = Theme.Background }; readonly CancellationTokenSource cancel = new CancellationTokenSource();
        readonly Func<string, Task> apply; ReleaseInfo release; bool downloading;
        public UpdateForm(Func<string, Task> applyUpdate, ReleaseInfo available = null)
        {
            apply = applyUpdate; Text = "Обновление ReplayLite"; ClientSize = new Size(485, 185); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10); BackColor = Theme.Background; ForeColor = Theme.Text;
            status.SetBounds(24, 22, 437, 85); button.SetBounds(24, 123, 437, 36); button.Text = "Проверка…"; button.Enabled = false; Controls.AddRange(new Control[] { status, button });
            Shown += async delegate { if (available == null) await Check(); else { release = available; ShowRelease(); button.Enabled = true; } }; button.Click += async delegate { if (release == null) await Check(); else await Download(); }; FormClosing += delegate { cancel.Cancel(); };
        }
        async Task Check()
        {
            button.Enabled = false; status.Text = "Проверяю GitHub Releases…";
            try
            {
                release = await Updates.Latest(cancel.Token);
                if (!IsDisposed && !cancel.IsCancellationRequested) ShowRelease();
            }
            catch (Exception e) { if (!cancel.IsCancellationRequested) { release = null; status.Text = "Не удалось проверить: " + e.Message; button.Text = "Повторить проверку"; } }
            finally { if (!IsDisposed) button.Enabled = true; }
        }
        void ShowRelease()
        {
            Version current = Assembly.GetExecutingAssembly().GetName().Version;
            if (release.Version <= current) { release = null; status.Text = "Установлена актуальная версия " + current.ToString(3) + "."; button.Text = "Проверить снова"; }
            else { status.Text = "Доступна версия " + release.Version.ToString(3) + ".\nПосле скачивания буфер остановится, приложение перезапустится. Настройки и записи сохранятся."; button.Text = "Обновить до " + release.Version.ToString(3); }
        }
        async Task Download()
        {
            if (downloading) return; downloading = true; button.Enabled = false;
            string temp = Path.Combine(Path.GetTempPath(), "ReplayLite-download-" + Guid.NewGuid().ToString("N")); string stage = Path.Combine(Path.GetTempPath(), "ReplayLite-update-" + Guid.NewGuid().ToString("N")); bool launched = false;
            try
            {
                Directory.CreateDirectory(temp); string zip = Path.Combine(temp, "package.zip");
                using (var client = Updates.Client())
                {
                    string sums; using (var response = await client.GetAsync(release.Sums, cancel.Token)) { response.EnsureSuccessStatusCode(); sums = await response.Content.ReadAsStringAsync(); }
                    await Updates.Download(client, release.Zip, zip, cancel.Token, new Progress<int>(p => { if (!IsDisposed) status.Text = "Скачивание: " + p + "%"; }));
                    status.Text = "Проверка и распаковка…";
                    await Task.Run(delegate { Updates.Verify(zip, sums); using (var stream = File.OpenRead(zip)) Deployment.Extract(stream, stage); }, cancel.Token);
                }
                cancel.Token.ThrowIfCancellationRequested();
                var candidate = new Version(File.ReadAllText(Path.Combine(stage, "VERSION")).Trim() + ".0");
                if (candidate != release.Version || FileVersionInfo.GetVersionInfo(Path.Combine(stage, "ReplayLite.exe")).FileVersion != candidate.ToString()) throw new IOException("Версия пакета не соответствует релизу.");
                ControlBox = false; status.Text = "Завершаю запись…"; await apply(stage); launched = true; Close();
            }
            catch (Exception e) { if (!IsDisposed && !cancel.IsCancellationRequested) { status.Text = "Обновление не выполнено: " + e.Message; ControlBox = true; button.Enabled = true; } }
            finally { downloading = false; Deployment.Cleanup(temp); if (!launched) Deployment.Cleanup(stage); }
        }
        protected override void Dispose(bool disposing) { if (disposing) cancel.Cancel(); base.Dispose(disposing); }
    }
}
