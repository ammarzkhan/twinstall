using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace Twinstance.App
{
    /// <summary>
    /// Twinstance's own mark, loaded from the embedded icon.
    ///
    /// Embedded rather than read off disk so it cannot go missing, and loaded as a whole icon
    /// rather than through ExtractAssociatedIcon so every frame survives — the latter returns a
    /// single 32px image, which looks soft on a high-DPI taskbar.
    ///
    /// Note the asymmetry with <see cref="Twinstance.Platform.LogoExtractor"/>, and that it is
    /// deliberate: our artwork ships with us, and other applications' artwork is only ever read
    /// at run time from the copy already installed on the user's machine.
    /// </summary>
    internal static class Branding
    {
        private const string ResourceName = "Twinstance.App.logo.ico";
        private static Icon _cached;

        internal static Icon AppIcon()
        {
            if (_cached != null) return _cached;
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (s != null) _cached = new Icon(s);
                }
            }
            catch (ArgumentException) { }
            catch (IOException) { }

            return _cached;
        }
    }
}
