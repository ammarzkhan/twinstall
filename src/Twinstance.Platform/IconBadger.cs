using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace Twinstance.Platform
{
    /// <summary>
    /// Chrome-style profile badging: the app's own logo with a large coloured disc in the
    /// corner. The source logo is read from the user's installed copy — no third-party artwork
    /// ships with Twinstance.
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
            Compose(source, outputIcoPath, hexColour, label, null);
        }

        /// <param name="avatar">
        /// Optional picture to fill the badge with instead of a colour and a letter, the way a
        /// Chrome profile carries an avatar. Cover-fitted and clipped to the circle.
        /// </param>
        public static void Compose(Image source, string outputIcoPath, string hexColour, string label, Image avatar)
        {
            var frames = new List<Bitmap>();
            Bitmap trimmed = null;
            try
            {
                trimmed = Trim(source);
                Image logo = trimmed ?? source;
                foreach (int size in IconSizes) frames.Add(Render(logo, size, hexColour, label, avatar));
                WriteIco(frames, outputIcoPath);
            }
            finally
            {
                foreach (Bitmap b in frames) b.Dispose();
                trimmed?.Dispose();
            }
        }

        /// <summary>
        /// Crops a logo to its visible pixels, or returns null when there is nothing to trim.
        ///
        /// This is what makes the badge work for applications other than the one it was tuned
        /// on. Icons are not all edge-to-edge squares: plenty are a circle, a rounded square, or
        /// a wordmark sitting in a transparent canvas with generous padding. Without trimming,
        /// the badge is positioned against the *canvas* corner, so for a padded logo it drifts
        /// out into empty space and looks detached — the padding is invisible, so the result
        /// just looks like a mistake.
        ///
        /// Measured against the visible artwork instead, the disc overlaps the logo's corner
        /// whatever shape it is, and the logo also gets to fill more of the icon.
        /// </summary>
        private static Bitmap Trim(Image source)
        {
            if (source == null) return null;

            Bitmap owned = null;
            try
            {
                var bmp = source as Bitmap;
                if (bmp == null || bmp.PixelFormat != PixelFormat.Format32bppArgb)
                {
                    owned = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(owned)) g.DrawImage(source, 0, 0, source.Width, source.Height);
                    bmp = owned;
                }

                Rectangle ink = OpaqueBounds(bmp);
                if (ink.Width <= 0 || ink.Height <= 0) return null;

                // Nothing meaningful to gain from a crop of a few pixels.
                if (ink.Width >= bmp.Width - 2 && ink.Height >= bmp.Height - 2) return null;

                var cropped = new Bitmap(ink.Width, ink.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(cropped))
                {
                    g.DrawImage(bmp, new Rectangle(0, 0, ink.Width, ink.Height), ink, GraphicsUnit.Pixel);
                }
                return cropped;
            }
            catch (ArgumentException) { return null; }
            finally { owned?.Dispose(); }
        }

        /// <summary>Bounding box of pixels that are not effectively transparent.</summary>
        private static Rectangle OpaqueBounds(Bitmap bmp)
        {
            const byte Threshold = 12;   // ignore near-invisible antialiasing fringe
            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                           ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                // Copied out rather than read through a pointer, so the project does not need
                // AllowUnsafeBlocks for one loop over at most 256x256 pixels.
                var buffer = new byte[Math.Abs(data.Stride) * bmp.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

                for (int y = 0; y < bmp.Height; y++)
                {
                    int row = y * data.Stride;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        if (buffer[row + (x * 4) + 3] <= Threshold) continue;   // BGRA, alpha last
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            finally { bmp.UnlockBits(data); }

            if (maxX < minX || maxY < minY) return Rectangle.Empty;
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>
        /// One frame, composed at its final size. All geometry is a fraction of that size, so
        /// the badge occupies the same proportion whether it is 16px or 256px.
        /// </summary>
        private static Bitmap Render(Image source, int size, string hexColour, string label, Image avatar)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode     = SmoothingMode.AntiAlias;
                g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                // The logo is anchored bottom-LEFT and inset only along the top and right, so
                // the badge can overhang the corner of its square rather than sit inside it.
                //
                // A disc contained entirely within the app's own rounded square reads as a
                // sticker placed on the icon; one that breaks the outline reads as part of it.
                // Insetting only two edges keeps the logo as large as that allows — it is not
                // scaled down uniformly, which is what cost resolution before.
                float margin = size * 0.13f;
                float box = size - margin;

                if (source != null)
                {
                    // Preserve aspect and align to the box's TOP-RIGHT. Extracted app icons are
                    // square, so for almost every app this fills the box and nothing moves. It
                    // matters for the ones that aren't — a wordmark or a wide logo — where
                    // centring would drop the artwork away from the corner and leave the badge
                    // overlapping nothing.
                    float scale = Math.Min(box / source.Width, box / source.Height);
                    float w = source.Width * scale, h = source.Height * scale;
                    g.DrawImage(source, box - w, margin, w, h);
                }

                // Flush to the canvas corner, so it clears the logo's square on two sides.
                float d = size * 0.62f;
                float x = size - d;
                float y = 0f;
                float cx = x + (d / 2f), cy = y + (d / 2f);

                Color colour = ParseColour(hexColour);

                if (avatar != null)
                {
                    // Cover-fit inside the circle: fill it completely, crop the overflow, never
                    // distort. A squashed face is worse than a cropped one.
                    using (var clip = new GraphicsPath())
                    {
                        clip.AddEllipse(x, y, d, d);
                        GraphicsState state = g.Save();
                        g.SetClip(clip);

                        float scale = Math.Max(d / avatar.Width, d / avatar.Height);
                        float w = avatar.Width * scale, h = avatar.Height * scale;
                        g.DrawImage(avatar, x + ((d - w) / 2f), y + ((d - h) / 2f), w, h);

                        g.Restore(state);
                    }
                }
                else
                {
                    using (var fill = new SolidBrush(colour)) g.FillEllipse(fill, x, y, d, d);
                    if (size >= SmallestWithLabel && !string.IsNullOrEmpty(label))
                        DrawCentredGlyph(g, label.Substring(0, 1).ToUpperInvariant(), d, cx, cy);
                }

                // No white ring. It existed to separate the disc from the artwork underneath,
                // which stopped being a problem once the badge started overhanging the logo's
                // square — most of its edge now meets transparency, not the icon.
            }
            return bmp;
        }

        /// <summary>
        /// Centres a letter on the disc by its ink, not its line box.
        ///
        /// StringFormat centring positions the font's full line box — ascent and descent
        /// included, most of which a capital letter does not occupy — so the glyph sits
        /// visibly high inside a circle. Measuring the outline's actual bounds and centring
        /// those puts it where the eye expects.
        /// </summary>
        private static void DrawCentredGlyph(Graphics g, string glyph, float diameter, float cx, float cy)
        {
            using (var family = new FontFamily("Segoe UI"))
            using (var path = new GraphicsPath())
            {
                path.AddString(glyph, family, (int)FontStyle.Bold, diameter * 0.58f,
                               PointF.Empty, StringFormat.GenericTypographic);

                RectangleF b = path.GetBounds();
                if (b.Width <= 0 || b.Height <= 0) return;

                using (var move = new Matrix())
                {
                    move.Translate(cx - (b.X + (b.Width / 2f)), cy - (b.Y + (b.Height / 2f)));
                    path.Transform(move);
                }

                using (var white = new SolidBrush(Color.White)) g.FillPath(white, path);
            }
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
        private static void WriteIco(List<Bitmap> frames, string path)
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
