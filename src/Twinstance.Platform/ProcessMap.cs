using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using Twinstance.Core;

namespace Twinstance.Platform
{
    /// <summary>
    /// Maps running processes to configured instances by exact profile path. Exact, not
    /// substring: 'work' and 'work2' are different instances and must never be conflated.
    /// </summary>
    public static class ProcessMap
    {
        public static IDictionary<int, string> Build(AppConfig cfg)
        {
            var map = new Dictionary<int, string>();
            if (cfg == null || cfg.Instances.Count == 0) return map;

            string exeName = PathUtil.Normalise(cfg.ExePath);
            int slash = exeName.LastIndexOf(PathUtil.Sep);
            exeName = slash >= 0 ? exeName.Substring(slash + 1) : exeName;
            if (exeName.Length == 0) return map;

            try
            {
                string q = "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = '"
                         + exeName.Replace("'", "''", StringComparison.Ordinal) + "'";
                using (var searcher = new ManagementObjectSearcher(q))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        object cl = mo["CommandLine"];
                        if (cl == null) continue;
                        string commandLine = cl.ToString();
                        if (CommandLine.IsChildProcess(commandLine)) continue;

                        // Same file name is not the same application. Verified on a real
                        // machine: a separate command-line tool ships a claude.exe too, and without this it
                        // was being reported as a running desktop instance.
                        if (!CommandLine.RefersToApp(commandLine, cfg.ExePath)) continue;

                        string dataDir = CommandLine.ExtractDataDir(commandLine, cfg.DefaultDataDir);
                        foreach (Instance inst in cfg.Instances)
                        {
                            if (PathUtil.SamePath(dataDir, inst.DataDir))
                            {
                                // WMI hands back a boxed uint32; the invariant culture keeps
                                // the conversion from depending on the user's locale.
                                try
                                {
                                    map[Convert.ToInt32(mo["ProcessId"], CultureInfo.InvariantCulture)] = inst.Name;
                                }
                                catch (FormatException) { }
                                catch (InvalidCastException) { }
                                catch (OverflowException) { }
                                break;
                            }
                        }
                    }
                }
            }
            catch { /* enumeration is best-effort; callers degrade to the prompt */ }
            return map;
        }
    }
}
