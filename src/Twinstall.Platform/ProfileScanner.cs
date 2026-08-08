using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Twinstall.Core;

namespace Twinstall.Platform
{
    /// <summary>
    /// Observes the profile root for detection step 3. Enumerates, stats and reads version
    /// resources; the ranking that turns those facts into an answer lives in
    /// <see cref="ProfileDiscovery"/>.
    /// </summary>
    public static class ProfileScanner
    {
        /// <summary>Immediate subdirectories of the root that look like Chromium profiles.</summary>
        public static IList<ProfileCandidate> Scan(string profileRoot)
        {
            var found = new List<ProfileCandidate>();
            if (string.IsNullOrWhiteSpace(profileRoot) || !Directory.Exists(profileRoot)) return found;

            string[] dirs;
            try { dirs = Directory.GetDirectories(profileRoot); }
            catch (IOException) { return found; }
            catch (UnauthorizedAccessException) { return found; }

            foreach (string dir in dirs)
            {
                int score = 0;
                bool localState = false;

                foreach (string marker in ProfileDiscovery.Markers)
                {
                    string path;
                    try { path = Path.Combine(dir, marker); }
                    catch (ArgumentException) { continue; }

                    bool exists;
                    try { exists = File.Exists(path); }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }

                    if (!exists) continue;
                    score++;
                    if (string.Equals(marker, "Local State", StringComparison.OrdinalIgnoreCase)) localState = true;
                }

                if (score == 0) continue;

                DateTimeOffset written = DateTimeOffset.MinValue;
                try { written = new DateTimeOffset(Directory.GetLastWriteTimeUtc(dir), TimeSpan.Zero); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }

                found.Add(new ProfileCandidate(dir, score, localState, written));
            }
            return found;
        }

        /// <summary>
        /// The app's own names, from the executable's version resource plus its file name.
        /// A missing or unreadable resource is normal for some builds and is not an error —
        /// the file name alone is often the match that counts.
        /// </summary>
        public static IList<string> IdentityNames(string exePath)
        {
            string product = null, description = null, internalName = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
                    product = info.ProductName;
                    description = info.FileDescription;
                    internalName = info.InternalName;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return ProfileDiscovery.IdentityNames(exePath, product, description, internalName);
        }

        /// <summary>Wiring only: scan, read the names, hand both to Core to decide.</summary>
        public static ProfileDiscoveryResult Discover(string exePath, string profileRoot)
        {
            if (string.IsNullOrWhiteSpace(profileRoot) || !Directory.Exists(profileRoot))
                return new ProfileDiscoveryResult(ProfileDiscoveryOutcome.NoRoot, null);

            return ProfileDiscovery.Rank(Scan(profileRoot), IdentityNames(exePath));
        }
    }
}
