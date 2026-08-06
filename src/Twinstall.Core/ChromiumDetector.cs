using System;
using System.Collections.Generic;

namespace Twinstall.Core
{
    /// <summary>Where the Chromium markers were found, and how many.</summary>
    public sealed class MarkerMatch
    {
        public MarkerMatch(string directory, int score)
        {
            Directory = directory;
            Score = score;
        }

        /// <summary>Directory the markers were counted in; empty when none were found.</summary>
        public string Directory { get; }

        public int Score { get; }
    }

    /// <summary>
    /// Confirms an executable is Chromium/Electron before we promise it supports profiles.
    /// WebView2 apps (new Teams) and ordinary Win32 apps fail here, which is the point:
    /// the failure mode becomes "this app isn't supported" rather than a broken instance.
    /// </summary>
    public static class ChromiumDetector
    {
        public static readonly string[] Markers =
        {
            @"resources\app.asar",
            @"resources\electron.asar",
            "icudtl.dat",
            "chrome_100_percent.pak",
            "chrome_200_percent.pak",
            "v8_context_snapshot.bin",
            "LICENSES.chromium.html"
        };

        /// <summary>Marker count in one specific directory.</summary>
        public static int ScoreIn(string directory, Func<string, bool> fileExists)
        {
            if (fileExists == null) return 0;
            string dir = PathUtil.Normalise(directory);
            if (dir.Length == 0) return 0;

            int hits = 0;
            foreach (string m in Markers)
                if (fileExists(PathUtil.Join(dir, m))) hits++;
            return hits;
        }

        /// <summary>
        /// Best marker score for the executable's own folder or any immediate subdirectory
        /// of it, and which directory won.
        ///
        /// The subdirectory sweep is not speculative generality. Visual Studio Code keeps
        /// nothing but Code.exe at the install root and puts every Chromium file in a
        /// commit-hash folder beside it (e.g. 'e4c7e7b1d6'), so looking only next to the
        /// executable scores zero on a plainly-Electron app and refuses it. That was found by
        /// running the probe, not by reading docs.
        ///
        /// It does cost something. An app that bundles a fixed-version WebView2 runtime in a
        /// subfolder now scores like Electron, because that runtime ships the same Chromium
        /// files. That is acceptable only because this check is not the last word: step 5
        /// launches the app and a WebView2 target ignores --user-data-dir, so it still ends up
        /// correctly reported as unsupported. Do not let this become the sole gate.
        /// </summary>
        /// <param name="listSubdirectories">
        /// Immediate subdirectories of a directory, as full paths. Null skips the sweep and
        /// restores the original folder-only behaviour.
        /// </param>
        public static MarkerMatch LocateMarkers(
            string exePath, Func<string, bool> fileExists, Func<string, IEnumerable<string>> listSubdirectories)
        {
            string dir = PathUtil.Parent(exePath);
            if (dir.Length == 0 || fileExists == null) return new MarkerMatch(string.Empty, 0);

            string bestDir = dir;
            int best = ScoreIn(dir, fileExists);

            // Nothing can beat a full house, so don't pay for the sweep when we already have one.
            if (listSubdirectories != null && best < Markers.Length)
            {
                IEnumerable<string> subs;
                try { subs = listSubdirectories(dir); }
                catch (Exception) { subs = null; }   // an unreadable folder is a miss, not a crash

                if (subs != null)
                {
                    foreach (string sub in subs)
                    {
                        if (string.IsNullOrWhiteSpace(sub)) continue;
                        int s = ScoreIn(sub, fileExists);
                        if (s > best) { best = s; bestDir = PathUtil.Normalise(sub); }
                    }
                }
            }

            return new MarkerMatch(best > 0 ? bestDir : string.Empty, best);
        }

        public static int Score(string exePath, Func<string, bool> fileExists)
        {
            return Score(exePath, fileExists, null);
        }

        public static int Score(
            string exePath, Func<string, bool> fileExists, Func<string, IEnumerable<string>> listSubdirectories)
        {
            return LocateMarkers(exePath, fileExists, listSubdirectories).Score;
        }

        /// <summary>Two or more markers is treated as confident.</summary>
        public static bool IsChromiumApp(string exePath, Func<string, bool> fileExists)
        {
            return Score(exePath, fileExists, null) >= 2;
        }

        public static bool IsChromiumApp(
            string exePath, Func<string, bool> fileExists, Func<string, IEnumerable<string>> listSubdirectories)
        {
            return Score(exePath, fileExists, listSubdirectories) >= 2;
        }
    }
}
