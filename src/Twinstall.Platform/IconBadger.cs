using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace Twinstall.Platform
{
    /// <summary>
    /// Chrome-style profile badging: the app's own logo, untouched, with a small coloured disc
    /// in the corner. The badge is vector geometry so it stays crisp at every taskbar size, and
    /// the source logo is read from the user's installed copy — no third-party artwork ships.
    /// </summary>
    public static class IconBadger
    {
        public const int Size = 256;

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
            using (var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode     = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                    // The badge sits TOP-RIGHT and is deliberately large.
                    //
                    // At taskbar size the coloured disc is the only thing telling two windows
                    // of the same application apart, and a taskbar icon is 16-24px. A
                    // Chrome-style corner dot at 46% of the canvas comes out as a handful of
                    // pixels there — technically present, useless in practice. Top-right also
                    // keeps it clear of the taskbar's own running-app underline and of the
                    // overflow chevron.
                    //
                    // The logo is pushed down-left to make room rather than scaled smaller in
                    // place, so it stays as large as it can while leaving the corner free.
                    const int inset = 34;
                    int baseSize = Size - inset;
                    if (source != null)
                        g.DrawImage(source, 0, inset, baseSize, baseSize);

                    int d = (int)(Size * 0.56);
                    int x = Size - d - 2, y = 2, halo = 9;

                    using (var white = new SolidBrush(Color.White))
                    using (var fill  = new SolidBrush(ColorTranslator.FromHtml(hexColour)))
                    {
                        g.FillEllipse(white, x - halo, y - halo, d + halo * 2, d + halo * 2);
                        g.FillEllipse(fill, x, y, d, d);

                        if (!string.IsNullOrEmpty(label))
                        {
                            using (var font = new Font("Segoe UI", d * 0.60f, FontStyle.Bold, GraphicsUnit.Pixel))
                            using (var fmt = new StringFormat { Alignment = StringAlignment.Center,
                                                               LineAlignment = StringAlignment.Center })
                                g.DrawString(label.Substring(0, 1).ToUpperInvariant(), font, white,
                                             new RectangleF(x, y + 2, d, d), fmt);
                        }
                    }
                }
                WriteIco(bmp, outputIcoPath);
            }
        }

        /// <summary>Single-entry ICO wrapping a PNG. Supported since Vista.</summary>
        static void WriteIco(Bitmap bmp, string path)
        {
            byte[] png;
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                png = ms.ToArray();
            }

            if (File.Exists(path))
            {
                try { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); } catch { }
            }

            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)1);
                w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write((uint)png.Length); w.Write((uint)22);
                w.Write(png, 0, png.Length);
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
