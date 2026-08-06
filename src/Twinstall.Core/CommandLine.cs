using System;

namespace Twinstall.Core
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
