using System;
using System.Collections.Generic;

namespace Twinstance.Core
{
    /// <summary>
    /// Works out where a Chromium app keeps its profiles, purely from the executable path.
    /// MSIX-packaged apps have %APPDATA% virtualised into the package container; unpackaged
    /// ones do not. Deriving this removes the need for per-app path tables.
    /// </summary>
    public static class PackagePaths
    {
        public const string WindowsAppsSegment = @"\WindowsApps\";

        /// <summary>Claude_1.24012.11.0_x64__pzs8sxrjxfjjc -> Claude_pzs8sxrjxfjjc</summary>
        public static string FamilyFromFullName(string packageFullName)
        {
            if (string.IsNullOrWhiteSpace(packageFullName)) return null;
            string[] parts = packageFullName.Split('_');
            if (parts.Length < 2) return null;
            string name = parts[0], publisherId = parts[parts.Length - 1];
            if (name.Length == 0 || publisherId.Length == 0) return null;
            return name + "_" + publisherId;
        }

        /// <summary>The PackageFullName segment of a WindowsApps path, or null if unpackaged.</summary>
        public static string PackageFullNameFromExePath(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return null;
            string p = exePath.Replace('/', PathUtil.Sep);
            int i = p.IndexOf(WindowsAppsSegment, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int start = i + WindowsAppsSegment.Length;
            int end = p.IndexOf(PathUtil.Sep, start);
            if (end <= start) return null;
            return p.Substring(start, end - start);
        }

        public static bool IsPackaged(string exePath)
        {
            return PackageFullNameFromExePath(exePath) != null;
        }

        /// <summary>
        /// Decodes a Windows MrtCache registry key name back into a path.
        ///
        /// Why this exists: **a normal application cannot list
        /// `C:\Program Files\WindowsApps`.** The ACL grants traverse but not read, so
        /// `Directory.GetDirectories` throws `UnauthorizedAccessException` there — which means
        /// a packaged app's install folder cannot be *found* by enumeration, even though a
        /// known path inside it can be opened perfectly well.
        ///
        /// MrtCache is the way out. It lives under HKCU, is readable without elevation, and
        /// records the full path of every package whose resources have been loaded, with
        /// backslashes escaped as %5C:
        ///
        ///   C:%5CProgram Files%5CWindowsApps%5CClaude_1.25927.0.0_x64__pzs8sxrjxfjjc%5Cresources.pri
        ///
        /// It lists historical versions too — nine for Claude on the development machine — so
        /// callers must confirm each candidate still exists on disk. Traversal is permitted,
        /// so that check works even though the enumeration that would have found it does not.
        /// </summary>
        public static string DecodeMrtCacheKey(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return null;
            return keyName.Replace("%5C", "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Simple glob for a single '*', which is all package and version folder patterns
        /// need ('Claude_*', 'app-*'). Not a general matcher, deliberately.
        /// </summary>
        public static bool MatchesPattern(string name, string pattern)
        {
            if (name == null || pattern == null) return false;

            int star = pattern.IndexOf('*', StringComparison.Ordinal);
            if (star < 0) return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);

            string prefix = pattern.Substring(0, star);
            string suffix = pattern.Substring(star + 1);
            if (name.Length < prefix.Length + suffix.Length) return false;

            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The package's own directory (…\WindowsApps\&lt;PackageFullName&gt;), or null when
        /// unpackaged. AppxManifest.xml sits directly inside it, and reading that file is how
        /// scheme discovery learns which protocols a package declares without needing WinRT.
        /// </summary>
        public static string PackageRoot(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return null;
            string p = exePath.Replace('/', PathUtil.Sep);
            int i = p.IndexOf(WindowsAppsSegment, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;

            int start = i + WindowsAppsSegment.Length;
            int end = p.IndexOf(PathUtil.Sep, start);
            if (end <= start) return null;
            return PathUtil.Normalise(p.Substring(0, end));
        }

        /// <summary>
        /// Re-points a packaged executable path at the version currently installed, or null if
        /// no such package is present.
        ///
        /// Why this exists: **an MSIX install path carries the version in it**, so
        /// <c>Claude_1.25927.0.0_x64__pzs8sxrjxfjjc</c> becomes
        /// <c>Claude_1.26832.0.0_x64__pzs8sxrjxfjjc</c> the moment the Store updates the app,
        /// and the old folder is gone. A configuration that stored the absolute path is
        /// therefore born with an expiry date: every Store update stranded the user with
        /// "cannot find the application it was set up for" and a full re-setup, having done
        /// nothing wrong. Observed on 8 August 2026 when Claude updated underneath a working
        /// install.
        ///
        /// The package *family* (<c>Claude_pzs8sxrjxfjjc</c>) is stable across versions, which
        /// is what makes the old path enough to find the new one: same family, same layout
        /// below the package root, so the tail of the path carries over verbatim.
        ///
        /// Unpackaged executables return null. A missing <c>slack.exe</c> under %LOCALAPPDATA%
        /// means the app was uninstalled or moved — there is no second candidate to try, and
        /// guessing at one would be worse than reporting it.
        /// </summary>
        /// <param name="packageRoots">
        /// Existing package roots, newest first — see <c>PackageIndex.InstalledPackageRoots</c>.
        /// </param>
        /// <param name="fileExists">
        /// Confirms a candidate. Required: the caller owns the filesystem, and a root existing
        /// says nothing about the executable inside it still being there.
        /// </param>
        public static string RepointToInstalledPackage(
            string exePath, IEnumerable<string> packageRoots, Func<string, bool> fileExists)
        {
            if (packageRoots == null || fileExists == null) return null;

            string family = FamilyFromFullName(PackageFullNameFromExePath(exePath));
            if (family == null) return null;

            string oldRoot = PackageRoot(exePath);
            if (string.IsNullOrEmpty(oldRoot)) return null;

            string normalised = PathUtil.Normalise(exePath);
            if (!normalised.StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase)) return null;

            // "app\Claude.exe" — everything below the package root, which the new version lays
            // out identically.
            string relative = normalised.Substring(oldRoot.Length).Trim(PathUtil.Sep);
            if (relative.Length == 0) return null;

            foreach (string root in packageRoots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;

                string candidate = PathUtil.Join(root, relative);
                string candidateFamily = FamilyFromFullName(PackageFullNameFromExePath(candidate));
                if (!string.Equals(candidateFamily, family, StringComparison.OrdinalIgnoreCase)) continue;

                if (fileExists(candidate)) return candidate;
            }

            return null;
        }

        /// <summary>
        /// Root directory holding the app's profile folders.
        /// Packaged:   &lt;localAppData&gt;\Packages\&lt;family&gt;\LocalCache\Roaming
        /// Unpackaged: &lt;roamingAppData&gt;
        /// </summary>
        public static string ProfileRoot(string exePath, string localAppData, string roamingAppData)
        {
            string full = PackageFullNameFromExePath(exePath);
            if (full == null) return PathUtil.Normalise(roamingAppData);
            string family = FamilyFromFullName(full);
            if (family == null) return PathUtil.Normalise(roamingAppData);
            return PathUtil.Join(localAppData, "Packages", family, "LocalCache", "Roaming");
        }
    }
}
