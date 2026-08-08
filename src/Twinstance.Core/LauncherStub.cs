using System;
using System.Collections.Generic;

namespace Twinstance.Core
{
    /// <summary>
    /// Resolves a launcher stub to the real executable behind it.
    ///
    /// Squirrel-packaged apps (Slack, Discord) put a small stub at the install root next to
    /// Update.exe, and the actual application in app-&lt;version&gt; beside it. The stub is the
    /// obvious thing to pick — it is what the Start menu shortcut points at, what a Browse
    /// dialog lands on, and what apps.json used to hint at — but it does not forward
    /// --user-data-dir. The launch test therefore reports, accurately and uselessly, that
    /// Slack cannot run separate instances.
    ///
    /// Measured on a real machine: %LOCALAPPDATA%\slack\slack.exe scores zero markers and
    /// fails the probe after the full 30s, while app-4.51.180\slack.exe scores five and
    /// passes in 1.7s. Same application.
    ///
    /// Note this cannot be inferred from "the root exe has no markers" alone — VS Code's root
    /// executable also has none and yet honours the flag perfectly well. The Squirrel layout
    /// has to be recognised specifically.
    /// </summary>
    public static class LauncherStub
    {
        /// <summary>Squirrel's updater always sits beside the stub. It is the layout's signature.</summary>
        public const string SquirrelUpdater = "Update.exe";

        public const string VersionFolderPrefix = "app-";

        /// <summary>
        /// The real executable behind <paramref name="exePath"/>, or null when it is not a
        /// stub or nothing better could be found. Never returns the input path.
        /// </summary>
        /// <param name="listSubdirectories">Immediate subdirectories, as full paths.</param>
        public static string Resolve(
            string exePath, Func<string, bool> fileExists, Func<string, IEnumerable<string>> listSubdirectories)
        {
            if (fileExists == null || listSubdirectories == null) return null;

            string full = PathUtil.Normalise(exePath);
            string dir = PathUtil.Parent(full);
            if (dir.Length == 0) return null;

            string fileName = full.Substring(dir.Length).Trim(PathUtil.Sep);
            if (fileName.Length == 0) return null;

            // Without Update.exe this is not a Squirrel install and we must not go guessing.
            if (!fileExists(PathUtil.Join(dir, SquirrelUpdater))) return null;

            IEnumerable<string> subs;
            try { subs = listSubdirectories(dir); }
            catch (Exception) { return null; }   // unreadable folder: leave the caller's path alone
            if (subs == null) return null;

            string bestFolder = null, bestCandidate = null;
            foreach (string sub in subs)
            {
                if (string.IsNullOrWhiteSpace(sub)) continue;
                string norm = PathUtil.Normalise(sub);
                string leaf = norm.Substring(PathUtil.Parent(norm).Length).Trim(PathUtil.Sep);
                if (!leaf.StartsWith(VersionFolderPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                string candidate = PathUtil.Join(norm, fileName);
                if (!fileExists(candidate)) continue;

                if (bestFolder == null || CompareVersionFolders(leaf, bestFolder) > 0)
                {
                    bestFolder = leaf;
                    bestCandidate = candidate;
                }
            }

            if (bestCandidate == null || PathUtil.SamePath(bestCandidate, full)) return null;
            return bestCandidate;
        }

        /// <summary>
        /// Orders app-&lt;version&gt; folder names by numeric segment, so app-4.10.0 beats
        /// app-4.9.0. An ordinal string sort gets that backwards, and Slack has shipped both
        /// shapes.
        /// </summary>
        public static int CompareVersionFolders(string a, string b)
        {
            int[] va = ParseVersion(a), vb = ParseVersion(b);
            int len = Math.Max(va.Length, vb.Length);
            for (int i = 0; i < len; i++)
            {
                int x = i < va.Length ? va[i] : 0;
                int y = i < vb.Length ? vb[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        private static int[] ParseVersion(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return Array.Empty<int>();

            string s = folderName.Trim();
            if (s.StartsWith(VersionFolderPrefix, StringComparison.OrdinalIgnoreCase))
                s = s.Substring(VersionFolderPrefix.Length);

            string[] parts = s.Split('.');
            var nums = new List<int>();
            foreach (string p in parts)
            {
                // Stop at the first non-numeric segment: '4.51.180-beta' compares as 4.51.180.
                int value = 0;
                bool any = false;
                foreach (char c in p)
                {
                    if (c < '0' || c > '9') break;
                    value = (value * 10) + (c - '0');
                    any = true;
                }
                if (!any) break;
                nums.Add(value);
            }
            return nums.ToArray();
        }
    }
}
