using System;
using System.Globalization;
using System.IO;

namespace Twinstance.App
{
    /// <summary>
    /// Append-only log with a size cap. Every routing decision goes here, because the
    /// failure mode this product exists to fix — a link opening the wrong account — leaves
    /// no other trace once the window is up.
    /// </summary>
    internal static class Log
    {
        private const long MaxBytes = 512 * 1024;

        internal static void Write(string message)
        {
            try
            {
                AppPaths.EnsureHome();
                string path = AppPaths.LogFile;

                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    // Keep one generation. Losing the older half beats an unbounded file.
                    string old = path + ".1";
                    try { if (File.Exists(old)) File.Delete(old); File.Move(path, old); }
                    catch (IOException) { try { File.Delete(path); } catch (IOException) { } }
                }

                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + "  " + message + Environment.NewLine);
            }
            catch (IOException) { /* logging must never be the thing that fails */ }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// A URL with its query string removed.
        ///
        /// This is not tidiness. A sign-in callback carries a live OAuth authorization code
        /// in its query, and this log is a plain file in the user's profile with no special
        /// permissions. Logging the whole URL would write a working credential to disk.
        /// Every path that logs a URL must go through here.
        /// </summary>
        internal static string SafeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "(none)";

            int cut = url.IndexOf('?', StringComparison.Ordinal);
            if (cut < 0) cut = url.IndexOf('#', StringComparison.Ordinal);
            return cut < 0 ? url : string.Concat(url.AsSpan(0, cut), "?<redacted>");
        }
    }
}
