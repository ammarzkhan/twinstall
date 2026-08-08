using System;
using System.Collections.Generic;
using System.IO;
using Twinstall.Core;
using Twinstall.Platform;

namespace Twinstall.App
{
    internal sealed class DetectionResult
    {
        internal string ExePath { get; set; }
        internal bool StubResolved { get; set; }
        internal int MarkerScore { get; set; }
        internal string MarkerDirectory { get; set; }
        internal bool IsChromium { get; set; }
        internal bool Packaged { get; set; }
        internal string ProfileRoot { get; set; }
        internal ProfileDiscoveryResult Profiles { get; set; }
        internal IList<SchemeRegistration> Schemes { get; set; }
        internal string Scheme { get; set; }
        internal ProbeVerdict Probe { get; set; }
        internal string ProbeDetail { get; set; }

        internal IList<string> ProfileDirectories
        {
            get
            {
                var dirs = new List<string>();
                if (Profiles != null)
                    foreach (RankedProfile r in Profiles.Ranked) dirs.Add(r.Candidate.Directory);
                return dirs;
            }
        }
    }

    /// <summary>
    /// Runs docs/DETECTION.md steps 0–5 in order and collects the answers. Pure wiring — every
    /// judgement belongs to Core, every observation to Platform.
    /// </summary>
    internal static class Detector
    {
        internal static IEnumerable<string> Subdirectories(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
            catch (IOException) { return Array.Empty<string>(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        internal static DetectionResult Run(string exePath, bool runProbe)
        {
            var result = new DetectionResult { ExePath = exePath, Probe = ProbeVerdict.Invalid };
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                result.ProbeDetail = "That file does not exist.";
                return result;
            }

            // 0 — a launcher stub is not the app.
            string resolved = LauncherStub.Resolve(exePath, File.Exists, Subdirectories);
            if (resolved != null)
            {
                result.ExePath = resolved;
                result.StubResolved = true;
                exePath = resolved;
            }

            // 1 — Chromium markers.
            MarkerMatch match = ChromiumDetector.LocateMarkers(exePath, File.Exists, Subdirectories);
            result.MarkerScore = match.Score;
            result.MarkerDirectory = match.Directory;
            result.IsChromium = match.Score >= 2;

            // 2 — profile root.
            result.Packaged = PackagePaths.IsPackaged(exePath);
            result.ProfileRoot = PackagePaths.ProfileRoot(
                exePath,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

            // 3 — existing profile.
            result.Profiles = ProfileScanner.Discover(exePath, result.ProfileRoot);

            // 4 — URL scheme.
            result.Schemes = SchemeScanner.Scan(exePath);
            result.Scheme = PickScheme(result.Schemes);

            // 5 — the only step that proves anything.
            if (runProbe && result.IsChromium)
            {
                ProbeResult probe = LaunchProbeRunner.Run(
                    exePath, Path.GetTempPath(), LaunchProbe.DefaultTimeoutSeconds, result.ProfileDirectories);
                result.Probe = probe.Verdict;
                result.ProbeDetail = probe.Detail;
            }
            else if (!result.IsChromium)
            {
                result.ProbeDetail = "This is not a Chromium/Electron application, so it has no "
                                   + "separate-profile support to test.";
            }

            return result;
        }

        /// <summary>
        /// A package-declared scheme wins over a merely-registered one: it is the app's own
        /// claim rather than whatever currently holds the association.
        /// </summary>
        private static string PickScheme(IList<SchemeRegistration> schemes)
        {
            if (schemes == null || schemes.Count == 0) return null;

            foreach (SchemeRegistration s in schemes)
                if (s.DeclaredByPackage) return s.Scheme;

            foreach (SchemeRegistration s in schemes)
                if (s.Owner == SchemeOwner.Direct || s.Owner == SchemeOwner.SamePackage) return s.Scheme;

            return schemes[0].Scheme;
        }
    }
}
