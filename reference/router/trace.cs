// Contour tracing helpers used by the icon builder. Added to the same Add-Type block as
// the P/Invoke declarations, so the icon script can turn the user's own installed logo
// into vector geometry at install time instead of upscaling a small bitmap.
using System;
using System.Collections.Generic;

public static class Trace
{
    // Extract a 1-bit silhouette from 32bpp ARGB pixel data, padded by one pixel on every
    // side so a shape touching the edge still has a traceable border.
    public static byte[] AlphaMask(byte[] argb, int stride, int w, int h, byte threshold)
    {
        int pw = w + 2, ph = h + 2;
        byte[] mask = new byte[pw * ph];
        for (int y = 0; y < h; y++)
        {
            int row = y * stride;
            for (int x = 0; x < w; x++)
            {
                if (argb[row + x * 4 + 3] > threshold) mask[(y + 1) * pw + (x + 1)] = 1;
            }
        }
        return mask;
    }

    static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    static readonly int[] DY = { 0, 1, 1, 1, 0, -1, -1, -1 };

    // Moore-neighbour border tracing with Jacob's stopping criterion.
    // Returns contours flattened as: [n0, x,y, x,y, ...][n1, ...] terminated by a 0.
    public static int[] TraceContours(byte[] mask, int w, int h, int minPoints)
    {
        var result = new List<int>();
        bool[] used = new bool[w * h];

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                if (mask[i] == 0 || used[i]) continue;
                if (mask[i - 1] != 0) continue;              // only start on a left edge

                var pts = new List<int>();
                int cx = x, cy = y, bx = x - 1, by = y;
                int startX = cx, startY = cy;
                int guard = 0, limit = w * h * 4;

                while (true)
                {
                    pts.Add(cx); pts.Add(cy);
                    used[cy * w + cx] = true;

                    int d = 0;
                    for (int k = 0; k < 8; k++)
                        if (cx + DX[k] == bx && cy + DY[k] == by) { d = k; break; }

                    int nx = -1, ny = -1, nbx = bx, nby = by;
                    for (int k = 1; k <= 8; k++)
                    {
                        int kk = (d + k) & 7;
                        int tx = cx + DX[kk], ty = cy + DY[kk];
                        if (tx < 0 || ty < 0 || tx >= w || ty >= h) { nbx = tx; nby = ty; continue; }
                        if (mask[ty * w + tx] != 0) { nx = tx; ny = ty; break; }
                        nbx = tx; nby = ty;
                    }

                    if (nx < 0) break;                        // isolated pixel
                    bx = nbx; by = nby; cx = nx; cy = ny;
                    if (++guard > limit) break;
                    if (cx == startX && cy == startY) break;  // closed the loop
                }

                if (pts.Count / 2 >= minPoints)
                {
                    result.Add(pts.Count / 2);
                    result.AddRange(pts);
                }
            }
        }
        result.Add(0);
        return result.ToArray();
    }
}
