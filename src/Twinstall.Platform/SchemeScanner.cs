using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Twinstall.Core;

namespace Twinstall.Platform
{
    /// <summary>One URL scheme found registered on this machine.</summary>
    public sealed class SchemeRegistration
    {
        public SchemeRegistration(string scheme, string hive, string command, SchemeOwner owner, bool declaredByPackage)
        {
            Scheme = scheme;
            Hive = hive;
            Command = command;
            Owner = owner;
            DeclaredByPackage = declaredByPackage;
        }

        public string Scheme { get; }

        /// <summary>HKCU or HKLM. HKCU wins on this machine, as it does for Windows.</summary>
        public string Hive { get; }

        public string Command { get; }

        public SchemeOwner Owner { get; }

        /// <summary>The app's own MSIX manifest declares this scheme, whoever holds it now.</summary>
        public bool DeclaredByPackage { get; }
    }

    /// <summary>
    /// Observes the registry and the package manifest for detection step 4. Every judgement
    /// about what a registration means belongs to <see cref="SchemeMatcher"/>.
    /// </summary>
    public static class SchemeScanner
    {
        private const string ClassesPath = @"Software\Classes";
        private const string UrlProtocolValue = "URL Protocol";

        /// <summary>
        /// Schemes the app's own MSIX package declares, read straight from AppxManifest.xml in
        /// the package folder. Empty for unpackaged apps.
        /// </summary>
        public static IList<string> DeclaredProtocols(string exePath)
        {
            string root = PackagePaths.PackageRoot(exePath);
            if (root == null) return new List<string>();

            string manifest = Path.Combine(root, "AppxManifest.xml");
            try
            {
                if (!File.Exists(manifest)) return new List<string>();
                return SchemeMatcher.ProtocolsFromManifest(File.ReadAllText(manifest));
            }
            catch (IOException) { return new List<string>(); }
            catch (UnauthorizedAccessException) { return new List<string>(); }
        }

        /// <summary>
        /// Every scheme on this machine that plausibly belongs to <paramref name="exePath"/>:
        /// registrations whose command points at the app, plus anything its package declares
        /// even when another program currently holds it.
        ///
        /// That second case is not a corner. Measured here: claude:// is declared by the Claude
        /// package and registered to ClaudeRouter.exe. Requiring the command to reference the
        /// app would find nothing for exactly the app Twinstall was built to fix.
        /// </summary>
        public static IList<SchemeRegistration> Scan(string exePath)
        {
            var results = new List<SchemeRegistration>();
            if (string.IsNullOrWhiteSpace(exePath)) return results;

            IList<string> declared = DeclaredProtocols(exePath);

            ScanHive(RegistryHive.CurrentUser, "HKCU", exePath, declared, results);
            ScanHive(RegistryHive.LocalMachine, "HKLM", exePath, declared, results);

            // A declared scheme with no registration at all is still worth reporting: it means
            // nothing currently handles it, which is a clean slate rather than a conflict.
            foreach (string scheme in declared)
            {
                bool seen = false;
                foreach (SchemeRegistration r in results)
                    if (string.Equals(r.Scheme, scheme, StringComparison.OrdinalIgnoreCase)) { seen = true; break; }

                if (!seen)
                    results.Add(new SchemeRegistration(scheme, null, null, SchemeOwner.Unknown, true));
            }

            return results;
        }

        private static void ScanHive(
            RegistryHive hive, string label, string exePath, IList<string> declared, List<SchemeRegistration> into)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                using (RegistryKey classes = baseKey.OpenSubKey(ClassesPath))
                {
                    if (classes == null) return;

                    foreach (string name in classes.GetSubKeyNames())
                    {
                        // Schemes never contain a dot; skipping file extensions and ProgIds up
                        // front avoids opening several thousand keys that cannot match.
                        if (name.Length == 0 || name.Contains('.', StringComparison.Ordinal)) continue;

                        using (RegistryKey key = SafeOpen(classes, name))
                        {
                            if (key == null) continue;
                            if (key.GetValue(UrlProtocolValue) == null) continue;

                            string command = null;
                            using (RegistryKey cmd = SafeOpen(key, @"shell\open\command"))
                                if (cmd != null) command = cmd.GetValue(null) as string;

                            SchemeOwner owner = SchemeMatcher.Classify(command, exePath);

                            bool isDeclared = false;
                            foreach (string d in declared)
                                if (string.Equals(d, name, StringComparison.OrdinalIgnoreCase)) { isDeclared = true; break; }

                            if (owner == SchemeOwner.Foreign && !isDeclared) continue;
                            if (owner == SchemeOwner.Unknown && !isDeclared) continue;

                            into.Add(new SchemeRegistration(name, label, command, owner, isDeclared));
                        }
                    }
                }
            }
            catch (System.Security.SecurityException) { /* a hive we cannot read is not a failure */ }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        private static RegistryKey SafeOpen(RegistryKey parent, string name)
        {
            try { return parent.OpenSubKey(name); }
            catch (System.Security.SecurityException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (IOException) { return null; }
        }
    }
}
