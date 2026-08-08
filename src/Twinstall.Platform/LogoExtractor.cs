using System;
using System.Drawing;
using System.IO;

namespace Twinstall.Platform
{
    /// <summary>
    /// Reads an application's own icon out of its executable.
    ///
    /// This is a deliberate IP decision as much as a technical one. Badged taskbar icons need
    /// the app's logo, and the alternative — shipping Slack's or Discord's artwork inside
    /// Twinstall — would fail Store policy 11.2 and would be wrong anyway. The logo is read at
    /// run time from the copy already on the user's machine, composited, and written to their
    /// own profile. Nothing third-party is ever committed or redistributed.
    /// </summary>
    public static class LogoExtractor
    {
        /// <summary>Largest first: taskbar buttons scale down well and up badly.</summary>
        private static readonly int[] PreferredSizes = { 256, 128, 96, 64, 48, 32 };

        /// <summary>
        /// The best available icon from <paramref name="exePath"/>, or null. The caller owns
        /// the bitmap.
        /// </summary>
        public static Bitmap Extract(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return null;

            foreach (int size in PreferredSizes)
            {
                Bitmap bmp = TryExtract(exePath, size);
                if (bmp != null) return bmp;
            }
            return null;
        }

        private static Bitmap TryExtract(string exePath, int size)
        {
            var handles = new IntPtr[1];
            var ids = new int[1];

            int count;
            try { count = NativeMethods.PrivateExtractIcons(exePath, 0, size, size, handles, ids, 1, 0); }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }

            if (count <= 0 || handles[0] == IntPtr.Zero) return null;

            try
            {
                using (Icon icon = Icon.FromHandle(handles[0]))
                {
                    // ToBitmap copies, so the bitmap outlives the handle we are about to free.
                    return icon.ToBitmap();
                }
            }
            catch (ArgumentException) { return null; }
            finally
            {
                NativeMethods.DestroyIcon(handles[0]);
            }
        }
    }
}
