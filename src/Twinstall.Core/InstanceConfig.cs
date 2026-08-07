using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Twinstall.Core
{
    public sealed class Instance
    {
        public string Name { get; set; }
        public string DataDir { get; set; }
        public string Colour { get; set; }
        public bool Badge { get; set; }

        /// <summary>
        /// Optional picture to use as the badge instead of a coloured disc with a letter, the
        /// way Chrome lets a profile carry an avatar. Empty means use <see cref="Colour"/>.
        /// A path, not the image itself: the file stays where the user put it, and nothing
        /// third-party is copied into Twinstall's own folder.
        /// </summary>
        public string Image { get; set; }
    }

    public sealed class AppConfig
    {
        public string ExePath { get; set; }
        public string Scheme { get; set; }
        public string DefaultDataDir { get; set; }

        // Get-only: the list is populated by Add, never replaced wholesale. A settable
        // collection property would let a caller swap it out and would earn CA2227.
        public List<Instance> Instances { get; } = new List<Instance>();
    }

    /// <summary>
    /// Deliberately boring tab-separated format. The router reads this on every claude://
    /// style callback, so it must parse with no dependencies and never throw.
    ///   EXE     &lt;path&gt;
    ///   SCHEME  &lt;scheme&gt;
    ///   DEFAULT &lt;path&gt;
    ///   &lt;name&gt; &lt;path&gt; &lt;colour&gt; &lt;badge 0|1&gt;
    /// </summary>
    public static class InstanceConfig
    {
        public static AppConfig Parse(IEnumerable<string> lines)
        {
            var cfg = new AppConfig();
            if (lines == null) return cfg;

            foreach (string raw in lines)
            {
                if (raw == null) continue;
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                string[] p = line.Split('\t');
                if (p.Length < 2) continue;

                switch (p[0])
                {
                    case "EXE":     cfg.ExePath = p[1]; break;
                    case "SCHEME":  cfg.Scheme = p[1]; break;
                    case "DEFAULT": cfg.DefaultDataDir = PathUtil.Normalise(p[1]); break;
                    default:
                        // Fields are read defensively by index so a file written by an older
                        // version — which had no picture column — still loads unchanged.
                        cfg.Instances.Add(new Instance
                        {
                            Name    = p[0],
                            DataDir = PathUtil.Normalise(p[1]),
                            Colour  = p.Length > 2 ? p[2] : "#7C3AED",
                            Badge   = p.Length > 3 && p[3] == "1",
                            Image   = p.Length > 4 && p[4].Length > 0 ? p[4] : null
                        });
                        break;
                }
            }
            return cfg;
        }

        public static string Serialise(AppConfig cfg)
        {
            var sb = new StringBuilder();
            if (cfg == null) return sb.ToString();
            if (!string.IsNullOrEmpty(cfg.ExePath))        sb.Append("EXE\t").Append(cfg.ExePath).Append("\r\n");
            if (!string.IsNullOrEmpty(cfg.Scheme))         sb.Append("SCHEME\t").Append(cfg.Scheme).Append("\r\n");
            if (!string.IsNullOrEmpty(cfg.DefaultDataDir)) sb.Append("DEFAULT\t").Append(cfg.DefaultDataDir).Append("\r\n");
            foreach (Instance i in cfg.Instances)
                sb.Append(i.Name).Append('\t').Append(i.DataDir).Append('\t')
                  .Append(i.Colour).Append('\t').Append(i.Badge ? "1" : "0").Append('\t')
                  .Append(i.Image ?? string.Empty).Append("\r\n");
            return sb.ToString();
        }

        public static AppConfig Load(string path)
        {
            try { return Parse(File.ReadAllLines(path)); }
            catch { return new AppConfig(); }
        }
    }
}
