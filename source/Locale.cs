using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Threading;

namespace ReplayLite
{
    static class L
    {
        static readonly Dictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal), Russian = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly KeyValuePair<string, string>[] Forward, Reverse;
        internal static event EventHandler Changed;
        internal static string Language { get; private set; }
        static L()
        {
            Language = "ru";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ReplayLite.Localization.tsv"))
            {
                if (stream == null) throw new InvalidDataException("Missing localization resource.");
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine(); if (string.IsNullOrEmpty(line)) continue; var fields = line.Split(new[] { '\t' }, 2);
                        if (fields.Length != 2) throw new InvalidDataException("Invalid localization row.");
                        string source = Regex.Unescape(fields[0]), target = Regex.Unescape(fields[1]);
                        if (English.ContainsKey(source)) throw new InvalidDataException("Duplicate localization key: " + source);
                        English.Add(source, target); if (!Russian.ContainsKey(target)) Russian.Add(target, source);
                    }
            }
            Forward = English.OrderByDescending(pair => pair.Key.Length).ToArray(); Reverse = Russian.OrderByDescending(pair => pair.Key.Length).ToArray();
        }
        internal static void SetLanguage(string value)
        { value = value == "en" ? "en" : "ru"; var culture = CultureInfo.GetCultureInfo(value == "en" ? "en-US" : "ru-RU"); Thread.CurrentThread.CurrentCulture = culture; Thread.CurrentThread.CurrentUICulture = culture; if (Language == value) return; Language = value; var changed = Changed; if (changed != null) changed(null, EventArgs.Empty); }
        internal static void InitializeFromSettings(string root)
        {
            try
            {
                string home = File.Exists(Path.Combine(root, "portable.flag")) ? Path.Combine(root, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReplayLite");
                foreach (string line in File.ReadAllLines(Path.Combine(home, "settings.txt"), Encoding.UTF8)) if (line.StartsWith("Language=", StringComparison.Ordinal)) { SetLanguage(line.Substring(9)); return; }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        internal static string T(string text) { return Convert(text, Language == "en"); }
        internal static string Canonical(string text) { return Convert(text, false); }
        static string Convert(string text, bool english)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (Regex.IsMatch(text, @"^[A-Za-z]:[\\/]|^\\\\", RegexOptions.CultureInvariant) || Regex.IsMatch(text, @"^[^\r\n]*\.(mp4|mkv)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return text;
            var dictionary = english ? English : Russian; string result;
            if (dictionary.TryGetValue(text, out result)) return result;
            var pairs = english ? Forward : Reverse;
            // Match only complete UI text, separators and explicit message fragments.
            // User paths, filenames and device identifiers are never replaced by word fragments.
            foreach (var pair in pairs)
                if ((pair.Key.EndsWith(" ", StringComparison.Ordinal) || pair.Key.EndsWith("\n", StringComparison.Ordinal)) && pair.Key.Length > 2 && text.StartsWith(pair.Key, StringComparison.Ordinal)) return pair.Value + Convert(text.Substring(pair.Key.Length), english);
            foreach (var pair in pairs)
                if (pair.Key.StartsWith(" ", StringComparison.Ordinal) && pair.Key.Length > 2 && text.EndsWith(pair.Key, StringComparison.Ordinal)) return Convert(text.Substring(0, text.Length - pair.Key.Length), english) + pair.Value;
            if (text.StartsWith("ReplayLite — ", StringComparison.Ordinal)) return "ReplayLite — " + Convert(text.Substring(13), english);
            if (text.Contains(" · ")) return string.Join(" · ", text.Split(new[] { " · " }, StringSplitOptions.None).Select(part => Convert(part, english)).ToArray());
            if (text.Contains("\n")) return string.Join("\n", text.Split('\n').Select(part => Convert(part, english)).ToArray());
            return text;
        }
        internal static bool HasTranslation(string source) { return English.ContainsKey(source); }
        internal static int Count { get { return English.Count; } }
    }
}
