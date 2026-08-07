using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Twinstall.App
{
    /// <summary>
    /// Colours, fonts and window chrome, taken from Windows rather than invented.
    ///
    /// Two things this deliberately does not do. It does not hard-code a brand colour where the
    /// user has already told Windows their accent, and it does not assume a light background —
    /// an app that stays white when the desktop is dark looks broken, not branded.
    /// </summary>
    internal static class Theme
    {
        // Chrome, not observation, so it lives with the UI rather than in Twinstall.Platform.
        [DllImport("dwmapi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int UseImmersiveDarkMode = 20;
        private const int UseImmersiveDarkModeBefore20H1 = 19;

        internal static bool IsDark { get; private set; }

        // --- palette -----------------------------------------------------------
        internal static Color Background { get; private set; }
        internal static Color Surface { get; private set; }
        internal static Color SurfaceAlt { get; private set; }
        internal static Color Border { get; private set; }
        internal static Color Text { get; private set; }
        internal static Color TextMuted { get; private set; }
        internal static Color Accent { get; private set; }
        internal static Color AccentText { get; private set; }
        internal static Color Good { get; private set; }
        internal static Color Bad { get; private set; }

        // --- type --------------------------------------------------------------
        internal static Font Title { get; private set; }
        internal static Font Heading { get; private set; }
        internal static Font Body { get; private set; }
        internal static Font BodyStrong { get; private set; }
        internal static Font Small { get; private set; }
        internal static Font Mono { get; private set; }

        static Theme()
        {
            Load();
        }

        internal static void Load()
        {
            IsDark = SystemPrefersDark();

            if (IsDark)
            {
                Background = FromHex("#1F1F1F");
                Surface    = FromHex("#2A2A2A");
                SurfaceAlt = FromHex("#333333");
                Border     = FromHex("#3D3D3D");
                Text       = FromHex("#F2F2F2");
                TextMuted  = FromHex("#A5A5A5");
                Good       = FromHex("#4ADE80");
                Bad        = FromHex("#F87171");
            }
            else
            {
                Background = FromHex("#F5F5F7");
                Surface    = FromHex("#FFFFFF");
                SurfaceAlt = FromHex("#F0F0F3");
                Border     = FromHex("#E2E2E6");
                Text       = FromHex("#141414");
                TextMuted  = FromHex("#5F5F66");
                Good       = FromHex("#15803D");
                Bad        = FromHex("#B91C1C");
            }

            Accent = SystemAccent();
            AccentText = Luminance(Accent) > 0.55 ? FromHex("#141414") : Color.White;

            // Segoe UI Variable is the Windows 11 face; Segoe UI is the fallback everywhere else.
            string display = HasFont("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
            string text = HasFont("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

            Title      = new Font(display, 19f, FontStyle.Regular, GraphicsUnit.Point);
            Heading    = new Font(display, 12.5f, FontStyle.Regular, GraphicsUnit.Point);
            Body       = new Font(text, 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            BodyStrong = new Font(text, 9.75f, FontStyle.Bold, GraphicsUnit.Point);
            Small      = new Font(text, 8.75f, FontStyle.Regular, GraphicsUnit.Point);
            Mono       = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);
        }

        private static bool HasFont(string family)
        {
            try
            {
                using (var f = new FontFamily(family)) return true;
            }
            catch (ArgumentException) { return false; }
        }

        private static bool SystemPrefersDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = key?.GetValue("AppsUseLightTheme");
                    if (v != null) return Convert.ToInt32(v, CultureInfo.InvariantCulture) == 0;
                }
            }
            catch (System.Security.SecurityException) { }
            catch (FormatException) { }
            catch (InvalidCastException) { }
            return false;
        }

        /// <summary>
        /// The user's chosen accent colour. Stored ABGR, and often too dark or too light to read
        /// against our own surfaces, so it is nudged into a usable range rather than used raw.
        /// </summary>
        private static Color SystemAccent()
        {
            Color fallback = FromHex("#7C3AED");
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    object v = key?.GetValue("AccentColor");
                    if (v == null) return fallback;

                    uint abgr = unchecked((uint)Convert.ToInt32(v, CultureInfo.InvariantCulture));
                    var c = Color.FromArgb((int)(abgr & 0xFF), (int)((abgr >> 8) & 0xFF), (int)((abgr >> 16) & 0xFF));

                    double lum = Luminance(c);
                    if (IsDark && lum < 0.30) return Lighten(c, 0.35f);
                    if (!IsDark && lum > 0.70) return Darken(c, 0.30f);
                    return c;
                }
            }
            catch (System.Security.SecurityException) { return fallback; }
            catch (FormatException) { return fallback; }
            catch (InvalidCastException) { return fallback; }
        }

        internal static double Luminance(Color c)
        {
            return ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255.0;
        }

        internal static Color Lighten(Color c, float amount)
        {
            return Color.FromArgb(
                c.R + (int)((255 - c.R) * amount),
                c.G + (int)((255 - c.G) * amount),
                c.B + (int)((255 - c.B) * amount));
        }

        internal static Color Darken(Color c, float amount)
        {
            return Color.FromArgb(
                (int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));
        }

        internal static Color FromHex(string hex)
        {
            try { return ColorTranslator.FromHtml(hex); }
            catch (ArgumentException) { return Color.Gray; }
            catch (FormatException) { return Color.Gray; }
        }

        /// <summary>Makes the title bar match, so a dark app doesn't wear a white hat.</summary>
        internal static void ApplyWindowChrome(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            int on = IsDark ? 1 : 0;
            try
            {
                // Attribute 20 on current builds, 19 before 20H1. Neither working just means
                // the title bar stays light, which is cosmetic, so the result is checked but
                // not escalated.
                if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                {
                    int legacy = DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref on, sizeof(int));
                    if (legacy != 0) Log.Write("dark title bar unavailable (hr=" + legacy + ")");
                }
            }
            catch (DllNotFoundException) { /* pre-Vista shells; the app still works */ }
            catch (EntryPointNotFoundException) { }
        }

        /// <summary>Rounded rectangle path — Windows 11's corner radius is 8.</summary>
        internal static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0 || d > r.Width || d > r.Height) { path.AddRectangle(r); return path; }

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
