using System;
using System.Collections.Generic;

namespace Twinstall.Core
{
    /// <summary>Outcome of the launch test described in docs/DETECTION.md step 5.</summary>
    public enum ProbeVerdict
    {
        /// <summary>Inputs were unusable, so nothing was attempted. Never treat as a result.</summary>
        Invalid = 0,

        /// <summary>Still inside the wait window with no profile yet. Keep polling.</summary>
        Pending = 1,

        /// <summary>The app created a profile where we told it to. Safe to build instances.</summary>
        Honoured = 2,

        /// <summary>The wait elapsed with nothing created. Report it; change nothing.</summary>
        NotHonoured = 3
    }

    /// <summary>
    /// Detection steps 1–4 are inference: markers on disk, a path convention, a registry
    /// sweep. This step is the proof. We start the target against a throwaway profile
    /// directory and confirm it actually populates it.
    ///
    /// It exists because every failure it catches is silent otherwise. An app with a custom
    /// userData path, or one that ignores the flag entirely, looks identical to a supported
    /// app right up until the user has two shortcuts that both open the same account. Making
    /// the answer "this app isn't supported" is the whole point — that outcome is safe, and a
    /// confidently broken instance is not.
    ///
    /// The decisions live here so they can be tested without launching anything; the process
    /// start and the polling loop are in Twinstall.Platform.
    /// </summary>
    public static class LaunchProbe
    {
        public const int DefaultTimeoutSeconds = 30;

        /// <summary>Folder name under the temp root, kept recognisable so a stray one is obvious.</summary>
        public const string ProbeFolderPrefix = "probe-";

        /// <summary>
        /// 'Local State' is written by Chromium during startup and is definitive on its own.
        /// The other two arrive later and are here only so a slow app that has visibly begun
        /// populating the directory still counts as honouring the flag.
        /// </summary>
        public static readonly string[] Markers =
        {
            "Local State",
            @"Default\Preferences",
            @"Network\Cookies"
        };

        /// <summary>
        /// Where the throwaway profile goes. The token is supplied by the caller rather than
        /// generated here so this stays a pure function — Platform passes a GUID.
        ///
        /// A token carrying a separator, a drive letter or a '..' is rejected outright. It
        /// should never happen, but this path is about to be handed to another vendor's
        /// executable as a directory to write into, so it is not somewhere to find out.
        /// </summary>
        public static string ProbeDirectory(string tempRoot, string token)
        {
            if (string.IsNullOrWhiteSpace(tempRoot) || string.IsNullOrWhiteSpace(token))
                return string.Empty;

            foreach (char c in token)
                if (c == '\\' || c == '/' || c == ':' || c == '.')
                    return string.Empty;

            return PathUtil.Join(tempRoot, "Twinstall", ProbeFolderPrefix + token);
        }

        /// <summary>
        /// The command line to launch the target with. Always quoted: the temp root runs
        /// through the user's profile name, which routinely contains a space.
        /// </summary>
        public static string Arguments(string probeDir)
        {
            string d = PathUtil.Normalise(probeDir);
            return d.Length == 0 ? string.Empty : CommandLine.Flag + "\"" + d + "\"";
        }

        /// <summary>True once any marker has appeared inside the probe directory.</summary>
        public static bool ProfileCreated(string probeDir, Func<string, bool> fileExists)
        {
            if (fileExists == null) return false;
            string d = PathUtil.Normalise(probeDir);
            if (d.Length == 0) return false;

            foreach (string m in Markers)
                if (fileExists(PathUtil.Join(d, m))) return true;
            return false;
        }

        /// <summary>
        /// Refuses a probe directory that would collide with a profile already in use.
        /// The probe is the one part of detection that writes to disk and launches the real
        /// application, so pointing it at a live profile would let a verification step
        /// contaminate the data it is meant to protect. Reuses the isolation rules rather
        /// than restating them.
        /// </summary>
        public static IList<string> ValidateTarget(string probeDir, IEnumerable<string> profileDirsInUse)
        {
            var issues = new List<string>();
            string d = PathUtil.Normalise(probeDir);
            if (d.Length == 0)
            {
                issues.Add("the probe directory is empty");
                return issues;
            }
            if (profileDirsInUse == null) return issues;

            foreach (string inUse in profileDirsInUse)
            {
                if (string.IsNullOrWhiteSpace(inUse)) continue;
                foreach (string issue in IsolationCheck.Validate(inUse, d))
                    issues.Add(issue + " (" + PathUtil.Normalise(inUse) + ")");
            }
            return issues;
        }

        public static bool IsSafeTarget(string probeDir, IEnumerable<string> profileDirsInUse)
        {
            return ValidateTarget(probeDir, profileDirsInUse).Count == 0;
        }

        /// <summary>
        /// The verdict for one poll. Creation is checked before the clock, so an app that
        /// writes its profile just as the window closes is still credited.
        /// </summary>
        public static ProbeVerdict Evaluate(
            string probeDir, Func<string, bool> fileExists, double elapsedSeconds, int timeoutSeconds)
        {
            if (fileExists == null) return ProbeVerdict.Invalid;
            if (timeoutSeconds <= 0) return ProbeVerdict.Invalid;
            string d = PathUtil.Normalise(probeDir);
            if (d.Length == 0) return ProbeVerdict.Invalid;

            if (ProfileCreated(d, fileExists)) return ProbeVerdict.Honoured;
            if (elapsedSeconds >= timeoutSeconds) return ProbeVerdict.NotHonoured;
            return ProbeVerdict.Pending;
        }

        /// <summary>Plain-language result, for logs and for the first-run UI.</summary>
        public static string Explain(ProbeVerdict verdict, string exePath)
        {
            string name = exePath == null ? "the application" : exePath;
            switch (verdict)
            {
                case ProbeVerdict.Honoured:
                    return name + " created a profile where it was told to, so separate instances will work.";
                case ProbeVerdict.NotHonoured:
                    return name + " ignored --user-data-dir, so it cannot run separate instances. Nothing was changed.";
                case ProbeVerdict.Pending:
                    return "Still waiting for " + name + " to create a profile.";
                default:
                    return "The launch test could not be run against " + name + ".";
            }
        }
    }
}
