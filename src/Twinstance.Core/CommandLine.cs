using System;

namespace Twinstance.Core
{
    /// <summary>Reads --user-data-dir back out of a process command line.</summary>
    public static class CommandLine
    {
        public const string Flag = "--user-data-dir=";

        /// <summary>True for Chromium child processes, which carry --type=.</summary>
        public static bool IsChildProcess(string commandLine)
        {
            return !string.IsNullOrEmpty(commandLine)
                && commandLine.Contains("--type=", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when this command line actually belongs to the application we configured,
        /// rather than merely sharing its file name.
        ///
        /// Matching on the executable's name alone is not enough, and this was observed, not
        /// theorised: a separate command-line tool is also called claude.exe, lives somewhere else
        /// entirely, and carries no --type= or --user-data-dir. A name-only match counted it
        /// as a running desktop instance, which is enough to turn a "nothing is running, ask
        /// the user" into a confident "only one is running" and route a sign-in to an instance
        /// that was never there.
        ///
        /// A different executable inside the same MSIX package still counts — packaged apps
        /// legitimately run helpers beside the main binary.
        /// </summary>
        public static bool RefersToApp(string commandLine, string exePath)
        {
            SchemeOwner owner = SchemeMatcher.Classify(commandLine, exePath);
            return owner == SchemeOwner.Direct || owner == SchemeOwner.SamePackage;
        }

        /// <summary>
        /// The profile directory this command line selects, or <paramref name="defaultDir"/>
        /// when the flag is absent. Handles quoted and bare values.
        /// </summary>
        public static string ExtractDataDir(string commandLine, string defaultDir)
        {
            if (string.IsNullOrEmpty(commandLine)) return PathUtil.Normalise(defaultDir);
            int i = commandLine.IndexOf(Flag, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return PathUtil.Normalise(defaultDir);

            int start = i + Flag.Length;
            if (start >= commandLine.Length) return PathUtil.Normalise(defaultDir);

            if (commandLine[start] == '"')
            {
                int end = commandLine.IndexOf('"', start + 1);
                if (end <= start) return PathUtil.Normalise(defaultDir);
                return PathUtil.Normalise(commandLine.Substring(start + 1, end - start - 1));
            }

            int space = commandLine.IndexOf(' ', start);
            string value = space < 0 ? commandLine.Substring(start) : commandLine.Substring(start, space - start);
            return PathUtil.Normalise(value.Trim('"'));
        }
    }
}
