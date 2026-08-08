using System;
using System.Collections.Generic;

namespace Twinstance.Core
{
    /// <summary>
    /// Windows path semantics implemented explicitly rather than via System.IO.Path.
    ///
    /// Two reasons. First, Path.GetFullPath resolves anything that isn't rooted against the
    /// current working directory, which silently turns a malformed config value into a real
    /// path somewhere unexpected — the last thing you want underneath an isolation check.
    /// Second, these rules are then identical on any host, so the logic is testable off
    /// Windows instead of only in CI.
    /// </summary>
    public static class PathUtil
    {
        public const char Sep = '\\';

        /// <summary>Canonical form for comparison: backslashes, no '.'/'..', no trailing separator.</summary>
        public static string Normalise(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            string p = path.Trim().Trim('"').Replace('/', Sep);

            string prefix = string.Empty;
            if (p.StartsWith(@"\\", StringComparison.Ordinal)) { prefix = @"\\"; p = p.Substring(2); }
            else if (p.Length > 0 && p[0] == Sep)              { prefix = @"\";  p = p.Substring(1); }

            var stack = new List<string>();
            foreach (string seg in p.Split(Sep))
            {
                if (seg.Length == 0 || seg == ".") continue;
                if (seg == "..")
                {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                stack.Add(seg);
            }
            return prefix + string.Join(Sep.ToString(), stack.ToArray());
        }

        public static string Join(params string[] parts)
        {
            if (parts == null) return string.Empty;
            var kept = new List<string>();
            foreach (string s in parts)
                if (!string.IsNullOrWhiteSpace(s)) kept.Add(s.Trim().Trim(Sep));
            return Normalise(string.Join(Sep.ToString(), kept.ToArray()));
        }

        /// <summary>Directory containing <paramref name="path"/>, or empty.</summary>
        public static string Parent(string path)
        {
            string n = Normalise(path);
            int i = n.LastIndexOf(Sep);
            return i <= 0 ? string.Empty : n.Substring(0, i);
        }

        public static bool SamePath(string a, string b)
        {
            return string.Equals(Normalise(a), Normalise(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when <paramref name="child"/> sits inside <paramref name="parent"/>.
        /// Whole segments only, so C:\x\work2 is NOT inside C:\x\work.
        /// </summary>
        public static bool IsInside(string child, string parent)
        {
            string c = Normalise(child), p = Normalise(parent);
            if (c.Length == 0 || p.Length == 0) return false;
            if (string.Equals(c, p, StringComparison.OrdinalIgnoreCase)) return false;
            return c.StartsWith(p + Sep, StringComparison.OrdinalIgnoreCase);
        }
    }
}
