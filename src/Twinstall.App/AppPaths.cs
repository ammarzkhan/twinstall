using System;
using System.IO;

namespace Twinstall.App
{
    /// <summary>
    /// Everything Twinstall writes lives under one folder, so uninstalling is a delete.
    /// Per-user throughout: no admin prompt, and nothing outside the profile.
    /// </summary>
    internal static class AppPaths
    {
        internal const string ProductName = "Twinstall";

        internal static string LocalAppData
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        }

        internal static string Home
        {
            get { return Path.Combine(LocalAppData, ProductName); }
        }

        internal static string ConfigFile
        {
            get { return Path.Combine(Home, "config.tsv"); }
        }

        internal static string LogFile
        {
            get { return Path.Combine(Home, "twinstall.log"); }
        }

        internal static string IconsDir
        {
            get { return Path.Combine(Home, "icons"); }
        }

        internal static string IconFor(string instanceName)
        {
            return Path.Combine(IconsDir, Sanitise(instanceName) + ".ico");
        }

        /// <summary>The running Twinstall.exe. This is what gets registered as the handler.</summary>
        internal static string SelfExe
        {
            get
            {
                string path = Environment.ProcessPath;
                return string.IsNullOrEmpty(path) ? "Twinstall.exe" : path;
            }
        }

        internal static void EnsureHome()
        {
            Directory.CreateDirectory(Home);
            Directory.CreateDirectory(IconsDir);
        }

        /// <summary>
        /// Instance names reach the filesystem as icon names and shortcut names, so strip
        /// anything that would let a name escape the folder it belongs in.
        /// </summary>
        internal static string Sanitise(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "instance";

            var chars = name.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                       || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == ' ';
                if (!ok) chars[i] = '_';
            }
            string s = new string(chars).Trim();
            return s.Length == 0 ? "instance" : s;
        }
    }
}
