using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace Twinstall.Platform
{
    /// <summary>
    /// Chrome-style profile badging: the app's own logo with a large coloured disc in the
    /// corner. The source logo is read from the user's installed copy — no third-party artwork
    /// ships with Twinstall.
    ///
    /// Every size in the icon is drawn at that size. That sounds obvious and was not what this
    /// did: it used to write a single 256px frame and leave Windows to shrink it to 16px, which
    /// Windows does badly — the result on a taskbar was visibly blocky, with a ragged disc and
    /// a smeared logo. A 16px frame composed at 16px is a different image from a 256px frame
    /// resampled, and only the first one looks like an icon.
    /// </summary>
    public static class IconBadger
    {
        public const int Size = 256;

        /// <summary>
        /// The frames an .ico carries. Windows picks per context: 16 in the taskbar and title
        /// bar, 32 for Alt-Tab and medium list views, 48 for the desktop, 256 for large tiles.
        /// Anything missing gets scaled from a neighbour, which is what caused the blockiness.
        /// </summary>
        public static readonly int[] IconSizes = { 256, 128, 64, 48, 40, 32, 24, 20, 16 };

        /// <summary>Below this the letter is a smudge, so the disc carries the meaning alone.</summary>
        private const int SmallestWithLabel = 24;

        public static void Compose(string sourceImagePath, string outputIcoPath, string hexColour, string label)
        {
            if (!string.IsNullOrEmpty(sourceImagePath) && File.Exists(sourceImagePath))
            {
                using (var src = Image.FromFile(sourceImagePath))
                    Compose(src, outputIcoPath, hexColour, label);
            }
            else
            {
                Compose((Image)null, outputIcoPath, hexColour, label);
            }
        }

        /// <summary>
        /// Overload taking an already-loaded image, so a logo read straight out of an
        /// executable never has to be written to disk just to be read back.
        /// </summary>
        public static void Compose(Image source, string outputIcoPath, string hexColour, string label)
        {
            var frames = new List<Bitmap>();
            try
            {
                foreach (int size in IconSizes) frames.Add(Render(source, size, hexColour, label));
                WriteIco(frames, outputIcoPath);
            }
            finally
            {
                foreach (Bitmap b in frames) b.Dispose();
            }
        }

        /// <summary>
        /// One frame, composed at its final size. All geometry is a fraction of that size, so
        /// the badge occupies the same proportion whether it is 16px or 256px.
        /// </summary>
        private static Bitmap Render(Image source, int size, string hexColour, string label)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode     = SmoothingMode.AntiAlias;
                g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                // The disc is 66% of the icon and sits top-right. It is the only thing telling
                // two windows of the same application apart, and a taskbar button is 16px — a
                // tasteful corner dot there is a few pixels of nothing. Top-right also stays
                // clear of the taskbar's own running-app underline along the bottom edge.
                float d = size * 0.66f;
                float inset = size * 0.16f;
                float logo = size - inset;

                if (source != null)
                    g.DrawImage(source, 0, inset, logo, logo);

                float x = size - d - (size * 0.012f);
                float y = size * 0.012f;
                float halo = Math.Max(1f, size * 0.030f);

                using (var white = new SolidBrush(Color.White))
                using (var fill = new SolidBrush(ParseColour(hexColour)))
                {
                    // The white ring separates the disc from whatever the logo does underneath,
                    // so the colour reads cleanly against dark and light artwork alike.
                    g.FillEllipse(white, x - halo, y - halo, d + (halo * 2), d + (halo * 2));
                    g.FillEllipse(fill, x, y, d, d);

                    if (size >= SmallestWithLabel && !string.IsNullOrEmpty(label))
                    {
                        using (var font = new Font("Segoe UI", d * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var fmt = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        })
                        {
                            g.DrawString(label.Substring(0, 1).ToUpperInvariant(), font, white,
                                         new RectangleF(x, y + (size * 0.012f), d, d), fmt);
                        }
                    }
                }
            }
            return bmp;
        }

        private static Color ParseColour(string hex)
        {
            try { return ColorTranslator.FromHtml(hex); }
            catch (ArgumentException) { return Color.MediumPurple; }
            catch (FormatException) { return Color.MediumPurple; }
        }

        /// <summary>
        /// Multi-frame ICO with PNG payloads, which Vista and later accept for every size and
        /// which keeps the 256px frame small.
        /// </summary>
        private static void WriteIco(IList<Bitmap> frames, string path)
        {
            var payloads = new List<byte[]>();
            foreach (Bitmap frame in frames)
            {
                using (var ms = new MemoryStream())
                {
                    frame.Save(ms, ImageFormat.Png);
                    payloads.Add(ms.ToArray());
                }
            }

            if (File.Exists(path))
            {
                try { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
                catch (IOException) { /* held by a running instance; the write below will say so */ }
                catch (UnauthorizedAccessException) { }
            }

            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((ushort)0);                    // reserved
                w.Write((ushort)1);                    // type: icon
                w.Write((ushort)payloads.Count);

                int offset = 6 + (16 * payloads.Count);
                for (int i = 0; i < payloads.Count; i++)
                {
                    int dim = frames[i].Width >= 256 ? 0 : frames[i].Width;   // 0 means 256
                    w.Write((byte)dim);
                    w.Write((byte)dim);
                    w.Write((byte)0);                  // palette
                    w.Write((byte)0);                  // reserved
                    w.Write((ushort)1);                // colour planes
                    w.Write((ushort)32);               // bits per pixel
                    w.Write((uint)payloads[i].Length);
                    w.Write((uint)offset);
                    offset += payloads[i].Length;
                }

                // Write(byte[], int, int), never Write(object[]) — the latter compiles and
                // silently emits one byte. There is a byte-level test for exactly this.
                foreach (byte[] png in payloads) w.Write(png, 0, png.Length);
            }
        }

        /// <summary>
        /// Applies an icon to a window. WM_SETICON only sticks if it lands before Windows
        /// builds the taskbar button, so callers apply repeatedly while the app starts.
        /// </summary>
        public static bool Apply(IntPtr hWnd, string icoPath)
        {
            IntPtr small = NativeMethods.LoadImage(IntPtr.Zero, icoPath, NativeMethods.IMAGE_ICON, 16, 16,
                                                   NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_SHARED);
            IntPtr big   = NativeMethods.LoadImage(IntPtr.Zero, icoPath, NativeMethods.IMAGE_ICON, 32, 32,
                                                   NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_SHARED);
            if (small == IntPtr.Zero || big == IntPtr.Zero) return false;

            NativeMethods.SendMessage(hWnd, NativeMethods.WM_SETICON, (IntPtr)NativeMethods.ICON_SMALL, small);
            NativeMethods.SendMessage(hWnd, NativeMethods.WM_SETICON, (IntPtr)NativeMethods.ICON_BIG, big);
            return true;
        }
    }
}
