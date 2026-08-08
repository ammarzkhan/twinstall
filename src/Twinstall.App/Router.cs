using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Twinstall.Core;
using Twinstall.Platform;

namespace Twinstall.App
{
    /// <summary>
    /// The three runtime modes: hand an incoming link to the right instance, start an
    /// instance, and keep taskbar icons badged.
    ///
    /// Every routing decision is logged, because the failure this product exists to fix — a
    /// sign-in landing on the wrong account — leaves no other evidence once the window opens.
    /// URLs are logged through <see cref="Log.SafeUrl"/> and never whole: the query string of
    /// a callback is a live OAuth code.
    /// </summary>
    internal static class Router
    {
        /// <summary>
        /// WM_SETICON only sticks if it arrives before Windows builds the taskbar button, so
        /// the launcher keeps re-applying while the app starts. Sixty seconds is generous for
        /// a cold start on a slow disk; the loop costs nothing once the icon has landed.
        /// </summary>
        private const int LaunchBadgeSeconds = 60;

        // ------------------------------------------------------------- routing --
        internal static int Route(string url)
        {
            Log.Write("url: " + Log.SafeUrl(url));

            // The self-test link exists only to make Windows show its handler chooser during
            // setup. Reaching here means the user picked Twinstall and it works — say so, and
            // do NOT forward it to the target application.
            if (url != null && url.Contains("//" + Registration.SelfTestHost, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("self-test link received — Twinstall is the handler");
                Ui.Info("That worked.\r\n\r\n"
                      + "Twinstall is now handling these links, so a sign-in will land on the "
                      + "account you started it from.\r\n\r\nYou can close this and Twinstall.");
                return 0;
            }

            AppConfig cfg = ConfigStore.Load();

            if (string.IsNullOrWhiteSpace(cfg.ExePath) || !File.Exists(cfg.ExePath))
            {
                Log.Write("ERROR: configured executable is missing: " + (cfg.ExePath ?? "(none)"));
                Ui.Error("Twinstall cannot find the application it was set up for.\r\n\r\n"
                       + "Open Twinstall and set it up again.");
                return 1;
            }

            // A missing or unreadable config must not strand the user mid sign-in. Handing the
            // link to the default profile is what would have happened without Twinstall at all.
            if (cfg.Instances.Count == 0)
            {
                Log.Write("no instances configured - handing to the default profile");
                return Start(cfg.ExePath, null, url) ? 0 : 1;
            }

            IDictionary<int, string> running = ProcessMap.Build(cfg);
            IList<int> topDown = WindowEnumerator.ProcessIdsTopDown();
            Log.Write("running instances: " + Describe(running));

            RouteResult decision = RouteDecision.Choose(cfg.Instances, running, topDown);
            string target = decision.Target;

            if (decision.Reason == RouteReason.NeedsPrompt)
            {
                // Z-order told us nothing because nothing is running. Guessing here is how a
                // link ends up in the wrong account, so ask instead.
                target = Ui.AskWhichInstance(cfg.Instances);
                if (target == null)
                {
                    Log.Write("-> cancelled by the user");
                    return 1;
                }
                Log.Write("-> " + target + "  (asked)");
            }
            else
            {
                Log.Write("-> " + (target ?? "(none)") + "  (via " + decision.Reason + ")");
            }

            Instance chosen = Find(cfg, target);
            if (chosen == null)
            {
                Log.Write("ERROR: no instance named " + (target ?? "(null)"));
                return Start(cfg.ExePath, null, url) ? 0 : 1;
            }

            return Start(cfg.ExePath, chosen.DataDir, url) ? 0 : 1;
        }

        // ------------------------------------------------------------ launching --
        internal static int Launch(string instanceName)
        {
            AppConfig cfg = ConfigStore.Load();
            Instance chosen = Find(cfg, instanceName);

            if (chosen == null)
            {
                Log.Write("ERROR: --launch for unknown instance " + (instanceName ?? "(null)"));
                Ui.Error("There is no instance called \"" + instanceName + "\".");
                return 1;
            }
            if (string.IsNullOrWhiteSpace(cfg.ExePath) || !File.Exists(cfg.ExePath))
            {
                Ui.Error("Twinstall cannot find the application it was set up for.");
                return 1;
            }

            PrepareProfile(cfg.ExePath, chosen.DataDir);

            Log.Write("launching '" + chosen.Name + "' -> " + chosen.DataDir);
            if (!Start(cfg.ExePath, chosen.DataDir, null)) return 1;

            ApplyIcons(cfg, LaunchBadgeSeconds);
            return 0;
        }

        internal static int Watch(int seconds)
        {
            // One watcher per session. Without this, every --launch and every login could add
            // another, each re-applying the same icons a second and a half apart for ever.
            if (seconds <= 0)
            {
                using (var only = new System.Threading.Mutex(true, @"Local\TwinstallIconWatcher", out bool mine))
                {
                    if (!mine)
                    {
                        Log.Write("an icon watcher is already running - not starting another");
                        return 0;
                    }

                    Log.Write("icon watcher started");
                    ApplyIcons(ConfigStore.Load(), 0);
                    return 0;
                }
            }

            Log.Write("icon refresh for " + seconds.ToString(CultureInfo.InvariantCulture) + "s");
            ApplyIcons(ConfigStore.Load(), seconds);
            return 0;
        }

        // --------------------------------------------------------------- icons --
        internal static void ApplyIcons(AppConfig cfg, int seconds)
        {
            if (cfg == null || cfg.Instances.Count == 0) return;

            var badged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Instance inst in cfg.Instances)
            {
                if (!inst.Badge) continue;
                string ico = AppPaths.IconFor(inst.Name);
                if (File.Exists(ico)) badged[inst.Name] = ico;
                else Log.Write("no icon composed for " + inst.Name);
            }
            if (badged.Count == 0) return;

            var seen = new HashSet<long>();
            DateTime deadline = seconds <= 0 ? DateTime.MaxValue : DateTime.Now.AddSeconds(seconds);
            int pause = seconds <= 0 ? 1500 : 500;

            // Which process belongs to which account changes only when an instance starts or
            // stops, but the icon needs re-applying far more often than that. Enumerating
            // windows is a couple of user32 calls; ProcessMap is a WMI query across every
            // process sharing the executable's name, which is not something to run every
            // second and a half for the life of the session.
            IDictionary<int, string> running = null;
            DateTime mapAge = DateTime.MinValue;
            TimeSpan mapTtl = TimeSpan.FromSeconds(seconds <= 0 ? 6 : 2);

            while (DateTime.Now < deadline)
            {
                if (running == null || DateTime.Now - mapAge > mapTtl)
                {
                    running = ProcessMap.Build(cfg);
                    mapAge = DateTime.Now;
                }

                if (running.Count > 0)
                {
                    foreach (KeyValuePair<string, string> pair in badged)
                    {
                        var pids = new List<int>();
                        foreach (KeyValuePair<int, string> proc in running)
                            if (string.Equals(proc.Value, pair.Key, StringComparison.OrdinalIgnoreCase))
                                pids.Add(proc.Key);

                        if (pids.Count == 0) continue;

                        foreach (IntPtr hwnd in WindowEnumerator.VisibleWindowsForProcesses(pids))
                        {
                            if (!IconBadger.Apply(hwnd, pair.Value)) continue;
                            if (seen.Add(hwnd.ToInt64()))
                                Log.Write("badged '" + pair.Key + "' on window "
                                          + hwnd.ToInt64().ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }
                Thread.Sleep(pause);
            }
        }

        /// <summary>
        /// Composes a badged icon for every instance that wants one, from the target app's own
        /// logo. Returns how many were written.
        /// </summary>
        internal static int ComposeIcons(AppConfig cfg)
        {
            if (cfg == null) return 0;
            AppPaths.EnsureHome();

            int written = 0;
            using (System.Drawing.Bitmap logo = LogoExtractor.Extract(cfg.ExePath))
            {
                foreach (Instance inst in cfg.Instances)
                {
                    if (!inst.Badge) continue;
                    try
                    {
                        // The picture stays where the user put it and is only read; nothing is
                        // copied into Twinstall's folder. If it has since been moved or
                        // deleted, fall back to the colour rather than failing the whole setup.
                        using (System.Drawing.Image avatar = LoadAvatar(inst.Image))
                            IconBadger.Compose(logo, AppPaths.IconFor(inst.Name), inst.Colour, inst.Name, avatar);
                        written++;
                    }
                    catch (Exception ex)
                    {
                        // One unwritable icon must not abandon the rest of the setup. The
                        // usual cause is the file being held by a running instance.
                        Log.Write("could not compose icon for " + inst.Name + ": " + ex.Message);
                    }
                }
            }
            return written;
        }

        private static System.Drawing.Image LoadAvatar(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try { return System.Drawing.Image.FromFile(path); }
            catch (OutOfMemoryException) { Log.Write("not a usable picture: " + path); return null; }
            catch (IOException ex) { Log.Write("could not read " + path + ": " + ex.Message); return null; }
            catch (UnauthorizedAccessException ex) { Log.Write("could not read " + path + ": " + ex.Message); return null; }
        }

        // --------------------------------------------------------------- plumbing --
        private static Instance Find(AppConfig cfg, string name)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(name)) return null;
            foreach (Instance inst in cfg.Instances)
                if (string.Equals(inst.Name, name, StringComparison.OrdinalIgnoreCase)) return inst;
            return null;
        }

        private static string Describe(IDictionary<int, string> running)
        {
            if (running == null || running.Count == 0) return "(none)";
            var parts = new List<string>();
            foreach (KeyValuePair<int, string> kv in running)
                parts.Add(kv.Key.ToString(CultureInfo.InvariantCulture) + "=" + kv.Value);
            return string.Join(", ", parts);
        }

        private static bool Start(string exePath, string dataDir, string url)
        {
            string args;
            if (string.IsNullOrEmpty(dataDir))
                args = string.IsNullOrEmpty(url) ? string.Empty : Quote(url);
            else if (string.IsNullOrEmpty(url))
                args = LaunchProbe.Arguments(dataDir);
            else
                args = LaunchProbe.Arguments(dataDir) + " " + Quote(url);

            try
            {
                var psi = new ProcessStartInfo(exePath)
                {
                    Arguments = args,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        Log.Write("ERROR: the process did not start");
                        return false;
                    }
                }
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Log.Write("ERROR: could not start: " + ex.Message);
                Ui.Error("Twinstall could not start the application.\r\n\r\n" + ex.Message);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                Log.Write("ERROR: could not start: " + ex.Message);
                Ui.Error("Twinstall could not start the application.\r\n\r\n" + ex.Message);
                return false;
            }
        }

        private static string Quote(string s)
        {
            return "\"" + s + "\"";
        }

        /// <summary>
        /// App-specific first-run guard, kept narrow on purpose.
        ///
        /// Claude's Cowork VM bundle errors on a profile that has never held one, and a
        /// zero-byte placeholder suppresses it. This is empirical, undocumented behaviour of
        /// one application — it is keyed on the executable name so it cannot leak into the
        /// generic path, and it must not be generalised into "prepare profiles" for everything.
        /// </summary>
        private static void PrepareProfile(string exePath, string dataDir)
        {
            if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(dataDir)) return;

            string exeName = Path.GetFileNameWithoutExtension(exePath);
            if (!string.Equals(exeName, "claude", StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                string bundle = Path.Combine(dataDir, "vm_bundles", "claudevm.bundle");
                string vhdx = Path.Combine(bundle, "rootfs.vhdx");
                if (File.Exists(vhdx)) return;

                Directory.CreateDirectory(bundle);
                using (File.Create(vhdx)) { }
                Log.Write("created the Cowork VM placeholder for " + dataDir);
            }
            catch (IOException ex) { Log.Write("vm bundle guard: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log.Write("vm bundle guard: " + ex.Message); }
        }
    }
}
