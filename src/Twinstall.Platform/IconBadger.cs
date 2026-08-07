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
            Compose(source, outputIcoPath, hexColour, label, null);
        }

        /// <param name="avatar">
        /// Optional picture to fill the badge with instead of a colour and a letter, the way a
        /// Chrome profile carries an avatar. Cover-fitted and clipped to the circle.
        /// </param>
        public static void Compose(Image source, string outputIcoPath, string hexColour, string label, Image avatar)
        {
            var frames = new List<Bitmap>();
            try
            {
                foreach (int size in IconSizes) frames.Add(Render(source, size, hexColour, label, avatar));
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

                // The application's logo is drawn at FULL size and the badge overlaps its
                // top-right corner. The badge used to be given room by shrinking the logo,
                // which threw away resolution to make space for something drawn on top of the
                // space anyway — the logo looked soft for no gain. Overlapping costs nothing.
                if (source != null)
                    g.DrawImage(source, 0, 0, size, size);

                float ring = Math.Max(1f, size * 0.055f);
                float d = size * 0.60f;
                float x = size - d - (ring / 2f);      // the ring strokes centred on the edge,
                float y = ring / 2f;                   // so half of it sits outside the circle
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

                // One stroked ellipse, not two filled ones. Filling a larger white circle and a
                // smaller coloured circle over it leaves two independently anti-aliased edges
                // meeting in the middle, which is what made the ring look ragged at desktop
                // size. A single stroke has one edge on each side and stays smooth.
                using (var pen = new Pen(Color.White, ring)) g.DrawEllipse(pen, x, y, d, d);
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
