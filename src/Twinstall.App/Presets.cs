using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Twinstall.Core;

namespace Twinstall.App
{
    internal sealed class Preset
    {
        internal string Id { get; set; }
        internal string DisplayName { get; set; }
        internal string ExeName { get; set; }
        internal IList<string> Hints { get; } = new List<string>();
        internal bool Verified { get; set; }
    }

    /// <summary>
    /// Convenience only. Detection is fully automatic, so a preset carries nothing but a name
    /// and some places to look — no profile paths, no schemes. An app missing from the list
    /// works identically via Browse; that is the whole point of detecting rather than
    /// maintaining a table.
    /// </summary>
    internal static class Presets
    {
        internal static IList<Preset> Load()
        {
            var presets = new List<Preset>();
            try
            {
                string file = Path.Combine(AppContext.BaseDirectory, "presets", "apps.json");
                if (!File.Exists(file)) return presets;

                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file)))
                {
                    if (!doc.RootElement.TryGetProperty("apps", out JsonElement apps)) return presets;

                    foreach (JsonElement app in apps.EnumerateArray())
                    {
                        var p = new Preset
                        {
                            Id = Text(app, "id"),
                            DisplayName = Text(app, "displayName"),
                            ExeName = Text(app, "exeName"),
                            Verified = app.TryGetProperty("verified", out JsonElement v)
                                       && v.ValueKind == JsonValueKind.True
                        };
                        if (app.TryGetProperty("hints", out JsonElement hints))
                            foreach (JsonElement h in hints.EnumerateArray())
                                if (h.ValueKind == JsonValueKind.String) p.Hints.Add(h.GetString());

                        if (!string.IsNullOrWhiteSpace(p.DisplayName)) presets.Add(p);
                    }
                }
            }
            catch (JsonException ex) { Log.Write("presets unreadable: " + ex.Message); }
            catch (IOException ex) { Log.Write("presets unreadable: " + ex.Message); }
            return presets;
        }

        private static string Text(JsonElement obj, string name)
        {
            return obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() : null;
        }

        /// <summary>
        /// First hint that actually resolves to a file on this machine, or null.
        ///
        /// A hint may contain one '*' segment — '%LOCALAPPDATA%\slack\app-*' and
        /// '%ProgramFiles%\WindowsApps\Claude_*\app' both need it, for a Squirrel version
        /// folder and an MSIX package folder respectively. Matches are tried newest first,
        /// using the same numeric ordering as stub resolution so app-4.10.0 beats app-4.9.0.
        /// </summary>
        internal static string FindInstalled(Preset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.ExeName)) return null;

            foreach (string hint in preset.Hints)
            {
                foreach (string dir in ExpandHint(hint))
                {
                    try
                    {
                        string candidate = Path.Combine(dir, preset.ExeName);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException) { }
                }
            }
            return null;
        }

        private static List<string> ExpandHint(string hint)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(hint)) return results;

            string expanded;
            try { expanded = Environment.ExpandEnvironmentVariables(hint); }
            catch (ArgumentException) { return results; }

            int star = expanded.IndexOf('*', StringComparison.Ordinal);
            if (star < 0)
            {
                if (Directory.Exists(expanded)) results.Add(expanded);
                return results;
            }

            // Split into <parent>\<pattern>\<tail>
            int before = expanded.LastIndexOf('\\', star);
            int after = expanded.IndexOf('\\', star);
            if (before < 0) return results;

            string parent = expanded.Substring(0, before);
            string pattern = after < 0
                ? expanded.Substring(before + 1)
                : expanded.Substring(before + 1, after - before - 1);
            string tail = after < 0 ? string.Empty : expanded.Substring(after + 1);

            string[] matches;
            try { matches = Directory.Exists(parent) ? Directory.GetDirectories(parent, pattern) : Array.Empty<string>(); }
            catch (IOException) { return results; }
            catch (UnauthorizedAccessException) { return results; }

            Array.Sort(matches, (a, b) =>
                LauncherStub.CompareVersionFolders(Path.GetFileName(b), Path.GetFileName(a)));

            foreach (string m in matches)
                results.Add(tail.Length == 0 ? m : Path.Combine(m, tail));

            return results;
        }
    }
}
