using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Twinstance.Core
{
    /// <summary>Who currently owns a registered URL scheme.</summary>
    public enum SchemeOwner
    {
        /// <summary>The command could not be parsed.</summary>
        Unknown = 0,

        /// <summary>Points at exactly this executable.</summary>
        Direct = 1,

        /// <summary>Points at a different executable inside the same MSIX package.</summary>
        SamePackage = 2,

        /// <summary>Points somewhere else entirely — another program handles this scheme.</summary>
        Foreign = 3
    }

    /// <summary>
    /// Detection step 4: which URL scheme belongs to this app, and who is handling it now?
    ///
    /// Two sources, because neither is sufficient alone.
    ///
    /// The registry says who handles a scheme *today*, which is not the same as whose scheme
    /// it is. Measured on a real machine: HKCU\Software\Classes\claude points at
    /// ClaudeRouter.exe — the predecessor tool — not at Claude. An app whose scheme has already
    /// been taken over is exactly the case Twinstance exists for, so matching only on "the
    /// command references this exe" finds nothing precisely when it matters most.
    ///
    /// The MSIX manifest says whose scheme it *is*, declaratively and regardless of who
    /// currently holds it. It can be read straight off disk from the package folder — no
    /// PackageManager and therefore no Windows-SDK-versioned target framework, which keeps
    /// this in Core where it can be tested.
    /// </summary>
    public static class SchemeMatcher
    {
        /// <summary>
        /// The executable out of a shell\open\command value. Handles a quoted path, a bare
        /// path, and trailing arguments — VS Code registers
        /// <c>"...\Code.exe" --open-url -- "%1"</c>, so taking the whole string would compare
        /// a command line against a path and never match.
        /// </summary>
        public static string ExtractExecutable(string openCommand)
        {
            if (string.IsNullOrWhiteSpace(openCommand)) return string.Empty;
            string s = openCommand.Trim();

            if (s[0] == '"')
            {
                // Span search rather than IndexOf: the char overload has no StringComparison
                // form to satisfy CA1307, and the string form trips CA1865. This is ordinal by
                // construction and argues with neither.
                int len = s.AsSpan(1).IndexOf('"');
                return len <= 0 ? string.Empty : PathUtil.Normalise(s.Substring(1, len));
            }

            int space = s.IndexOf(' ', StringComparison.Ordinal);
            return PathUtil.Normalise(space < 0 ? s : s.Substring(0, space));
        }

        /// <summary>
        /// Who the registered command actually points at, relative to the app we are
        /// configuring. Comparison is on exact normalised paths, never substrings.
        /// </summary>
        public static SchemeOwner Classify(string openCommand, string exePath)
        {
            string handler = ExtractExecutable(openCommand);
            if (handler.Length == 0) return SchemeOwner.Unknown;

            string target = PathUtil.Normalise(exePath);
            if (target.Length == 0) return SchemeOwner.Unknown;

            if (PathUtil.SamePath(handler, target)) return SchemeOwner.Direct;

            // A packaged app may register a helper beside its main exe. Same package is still
            // the app, so compare package families rather than paths.
            string handlerFull = PackagePaths.PackageFullNameFromExePath(handler);
            string targetFull = PackagePaths.PackageFullNameFromExePath(target);
            if (handlerFull != null && targetFull != null)
            {
                string a = PackagePaths.FamilyFromFullName(handlerFull);
                string b = PackagePaths.FamilyFromFullName(targetFull);
                if (a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                    return SchemeOwner.SamePackage;
            }

            return SchemeOwner.Foreign;
        }

        /// <summary>
        /// Scheme names declared by an MSIX package manifest, from its XML.
        ///
        /// Matched on local name so it works for uap:, uap3: and any future namespace prefix,
        /// and takes a string rather than a path so it stays testable off Windows.
        /// Malformed XML yields an empty list — a package we cannot parse is one we know
        /// nothing about, which is not the same as an error worth throwing.
        /// </summary>
        public static IList<string> ProtocolsFromManifest(string manifestXml)
        {
            var found = new List<string>();
            if (string.IsNullOrWhiteSpace(manifestXml)) return found;

            XDocument doc;
            try { doc = XDocument.Parse(manifestXml); }
            catch (System.Xml.XmlException) { return found; }

            foreach (XElement el in doc.Descendants())
            {
                if (!string.Equals(el.Name.LocalName, "Protocol", StringComparison.Ordinal)) continue;

                XAttribute nameAttr = el.Attribute("Name");
                if (nameAttr == null || string.IsNullOrWhiteSpace(nameAttr.Value)) continue;

                string scheme = nameAttr.Value.Trim();
                bool seen = false;
                foreach (string s in found)
                    if (string.Equals(s, scheme, StringComparison.OrdinalIgnoreCase)) { seen = true; break; }
                if (!seen) found.Add(scheme);
            }
            return found;
        }
    }
}
