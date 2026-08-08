using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Twinstall.Core;

namespace Twinstall.Platform
{
    /// <summary>
    /// Finds installed MSIX package folders without listing
    /// <c>C:\Program Files\WindowsApps</c> — because a normal application cannot.
    ///
    /// This was found the hard way. The preset lookup for Claude silently returned nothing
    /// while Slack's worked, and the difference was not the code: it was that Slack lives
    /// under %LOCALAPPDATA% and Claude under WindowsApps, whose ACL grants traverse but not
    /// read. <c>Directory.Exists</c> on the folder returns true, and
    /// <c>Directory.GetDirectories</c> on it throws <c>UnauthorizedAccessException</c>. An
    /// elevated shell can list it, which is exactly why testing from a terminal never caught
    /// this — always confirm behaviour from the application's own process.
    ///
    /// MrtCache under HKCU records the full path of every package whose resources have been
    /// loaded, including versions long since replaced, so each candidate is confirmed against
    /// the filesystem before being returned. Traversal into a known path is allowed, so that
    /// check succeeds where the enumeration would not.
    /// </summary>
    public static class PackageIndex
    {
        private const string MrtCachePath = @"Software\Classes\Local Settings\MrtCache";

        /// <summary>
        /// Package roots (…\WindowsApps\&lt;PackageFullName&gt;) that still exist on disk.
        /// Newest first, by the version segment of the full name.
        /// </summary>
        public static IList<string> InstalledPackageRoots()
        {
            var roots = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(MrtCachePath))
                {
                    if (key == null) return roots;

                    foreach (string name in key.GetSubKeyNames())
                    {
                        string decoded = PackagePaths.DecodeMrtCacheKey(name);
                        string root = PackagePaths.PackageRoot(decoded);
                        if (root == null || !seen.Add(root)) continue;

                        // MrtCache remembers uninstalled versions. Only disk decides.
                        try { if (Directory.Exists(root)) roots.Add(root); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }
            catch (System.Security.SecurityException) { return roots; }
            catch (UnauthorizedAccessException) { return roots; }
            catch (IOException) { return roots; }

            roots.Sort((a, b) => CompareFullNames(Leaf(b), Leaf(a)));
            return roots;
        }

        /// <summary>
        /// Existing package roots whose folder name matches <paramref name="pattern"/>, e.g.
        /// <c>Claude_*</c>. Newest first.
        /// </summary>
        public static IList<string> PackageRootsMatching(string pattern)
        {
            var matches = new List<string>();
            if (string.IsNullOrWhiteSpace(pattern)) return matches;

            foreach (string root in InstalledPackageRoots())
                if (PackagePaths.MatchesPattern(Leaf(root), pattern)) matches.Add(root);

            return matches;
        }

        private static string Leaf(string path)
        {
            string n = PathUtil.Normalise(path);
            string parent = PathUtil.Parent(n);
            return parent.Length == 0 ? n : n.Substring(parent.Length).Trim(PathUtil.Sep);
        }

        /// <summary>
        /// Orders two PackageFullNames by their version segment. Name_Version_Arch__Publisher,
        /// so the version is the second segment; anything malformed sorts last rather than
        /// throwing.
        /// </summary>
        private static int CompareFullNames(string a, string b)
        {
            string va = VersionSegment(a), vb = VersionSegment(b);
            if (va == null && vb == null) return 0;
            if (va == null) return -1;
            if (vb == null) return 1;
            return LauncherStub.CompareVersionFolders(va, vb);
        }

        private static string VersionSegment(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return null;
            string[] parts = fullName.Split('_');
            return parts.Length < 2 ? null : parts[1];
        }
    }
}
