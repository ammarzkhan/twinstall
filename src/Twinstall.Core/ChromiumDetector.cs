using System;

namespace Twinstall.Core
{
    /// <summary>
    /// Confirms an executable is Chromium/Electron before we promise it supports profiles.
    /// WebView2 apps (new Teams) and ordinary Win32 apps fail here, which is the point:
    /// the failure mode becomes "this app isn't supported" rather than a broken instance.
    /// </summary>
    public static class ChromiumDetector
    {
        public static readonly string[] Markers =
        {
            @"resources\app.asar",
            @"resources\electron.asar",
            "icudtl.dat",
            "chrome_100_percent.pak",
            "chrome_200_percent.pak",
            "v8_context_snapshot.bin",
            "LICENSES.chromium.html"
        };

        public static int Score(string exePath, Func<string, bool> fileExists)
        {
            if (string.IsNullOrWhiteSpace(exePath) || fileExists == null) return 0;
            string dir = PathUtil.Parent(exePath);
            if (dir.Length == 0) return 0;

            int hits = 0;
            foreach (string m in Markers)
                if (fileExists(PathUtil.Join(dir, m))) hits++;
            return hits;
        }

        /// <summary>Two or more markers is treated as confident.</summary>
        public static bool IsChromiumApp(string exePath, Func<string, bool> fileExists)
        {
            return Score(exePath, fileExists) >= 2;
        }
    }
}
