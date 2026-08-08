using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Threading;
using Twinstance.Core;

namespace Twinstance.Platform
{
    /// <summary>What the launch test observed. The verdict itself is decided in Core.</summary>
    public sealed class ProbeResult
    {
        public ProbeResult(ProbeVerdict verdict, string probeDirectory, double elapsedSeconds, string detail)
        {
            Verdict = verdict;
            ProbeDirectory = probeDirectory;
            ElapsedSeconds = elapsedSeconds;
            Detail = detail;
        }

        public ProbeVerdict Verdict { get; }
        public string ProbeDirectory { get; }
        public double ElapsedSeconds { get; }
        public string Detail { get; }
    }

    /// <summary>
    /// Runs docs/DETECTION.md step 5: start the target against a throwaway profile directory
    /// and watch for it to be populated. Mechanism only — every verdict comes from
    /// <see cref="LaunchProbe"/>, so the rules stay testable without launching anything.
    ///
    /// Two things this deliberately cleans up after itself. The probe genuinely starts the
    /// user's application, so a window will appear; leaving it open would be indistinguishable
    /// from the bug we are testing for. And an app that ignores the flag re-parents itself onto
    /// the existing instance, which our process handle no longer covers — hence the sweep by
    /// probe token rather than only killing the child we started.
    /// </summary>
    public static class LaunchProbeRunner
    {
        private const int PollMilliseconds = 250;

        public static ProbeResult Run(string exePath, string tempRoot, int timeoutSeconds)
        {
            return Run(exePath, tempRoot, timeoutSeconds, null);
        }

        /// <param name="profileDirsInUse">
        /// Profile directories already holding real data. The probe refuses to run if its
        /// throwaway directory would collide with any of them.
        /// </param>
        public static ProbeResult Run(
            string exePath, string tempRoot, int timeoutSeconds, IEnumerable<string> profileDirsInUse)
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                return new ProbeResult(ProbeVerdict.Invalid, null, 0, "the executable was not found");

            string token = Guid.NewGuid().ToString("N");
            string probeDir = LaunchProbe.ProbeDirectory(tempRoot, token);
            if (probeDir.Length == 0)
                return new ProbeResult(ProbeVerdict.Invalid, null, 0, "could not derive a probe directory");

            IList<string> collisions = LaunchProbe.ValidateTarget(probeDir, profileDirsInUse);
            if (collisions.Count > 0)
                return new ProbeResult(ProbeVerdict.Invalid, probeDir, 0,
                    "probe directory collides with a profile in use: " + string.Join("; ", collisions));

            try { Directory.CreateDirectory(probeDir); }
            catch (IOException ex) { return new ProbeResult(ProbeVerdict.Invalid, probeDir, 0, ex.Message); }
            catch (UnauthorizedAccessException ex) { return new ProbeResult(ProbeVerdict.Invalid, probeDir, 0, ex.Message); }

            var psi = new ProcessStartInfo(exePath)
            {
                Arguments = LaunchProbe.Arguments(probeDir),
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty
            };

            var clock = Stopwatch.StartNew();
            ProbeVerdict verdict = ProbeVerdict.Invalid;
            string detail = null;

            try
            {
                using (Process proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        return new ProbeResult(ProbeVerdict.Invalid, probeDir, 0, "the process did not start");
                    }

                    try
                    {
                        do
                        {
                            verdict = LaunchProbe.Evaluate(
                                probeDir, File.Exists, clock.Elapsed.TotalSeconds, timeoutSeconds);

                            if (verdict != ProbeVerdict.Pending) break;
                            Thread.Sleep(PollMilliseconds);
                        }
                        while (true);
                    }
                    finally
                    {
                        KillTree(proc);
                    }
                }
            }
            catch (Win32Exception ex)
            {
                // Windows refused to start it. That is a different answer from "the app
                // ignored the flag" — we never got to ask. Measured on OpenAI Codex, whose
                // Store executable denies CreateProcess by every route while Claude's, in the
                // same folder with identical ACLs, starts normally.
                detail = LaunchProbe.Explain(ProbeVerdict.LaunchBlocked, exePath) + "  (" + ex.Message + ")";
                verdict = ProbeVerdict.LaunchBlocked;
            }
            catch (InvalidOperationException ex)
            {
                detail = "could not start the executable: " + ex.Message;
                verdict = ProbeVerdict.Invalid;
            }
            finally
            {
                clock.Stop();
                KillStragglers(token);
                Cleanup(probeDir);
            }

            return new ProbeResult(
                verdict,
                probeDir,
                Math.Round(clock.Elapsed.TotalSeconds, 2),
                detail ?? LaunchProbe.Explain(verdict, exePath));
        }

        private static void KillTree(Process proc)
        {
            try
            {
                if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { /* already gone */ }
            catch (NotSupportedException) { /* remote process; cannot happen locally */ }
            catch (Win32Exception) { /* exited between the check and the kill */ }
        }

        /// <summary>
        /// Kills anything still carrying our probe token on its command line. The token is a
        /// fresh GUID, so a match cannot be anything but a process this call started.
        /// </summary>
        private static void KillStragglers(string token)
        {
            int self = Environment.ProcessId;
            try
            {
                string q = "SELECT ProcessId FROM Win32_Process WHERE CommandLine LIKE '%" + token + "%'";
                using (var searcher = new ManagementObjectSearcher(q))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        int pid;
                        try { pid = Convert.ToInt32(mo["ProcessId"], CultureInfo.InvariantCulture); }
                        catch (FormatException) { continue; }
                        catch (InvalidCastException) { continue; }
                        catch (OverflowException) { continue; }

                        if (pid == self || pid <= 0) continue;

                        try
                        {
                            using (Process p = Process.GetProcessById(pid)) KillTree(p);
                        }
                        catch (ArgumentException) { /* exited already */ }
                    }
                }
            }
            catch (ManagementException) { /* best effort; a stray window is survivable */ }
            catch (UnauthorizedAccessException) { }
        }

        private static void Cleanup(string probeDir)
        {
            try
            {
                if (Directory.Exists(probeDir)) Directory.Delete(probeDir, recursive: true);

                // Take the shared parent with it, but only when it is both ours by name and
                // empty — a concurrent probe may still be using it. The name check matters:
                // without it a malformed probeDir would put %TEMP% itself in range.
                string parent = Path.GetDirectoryName(probeDir);
                if (!string.IsNullOrEmpty(parent)
                    && string.Equals(Path.GetFileName(parent), "Twinstance", StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(parent)
                    && Directory.GetFileSystemEntries(parent).Length == 0)
                {
                    Directory.Delete(parent);
                }
            }
            catch (IOException) { /* the app may still hold a handle; temp will age it out */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
