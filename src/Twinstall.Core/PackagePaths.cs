using System;

namespace Twinstall.Core
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
