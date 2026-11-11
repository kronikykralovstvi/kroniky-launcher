// =============================================================================
// Harsh Winter builder - BC1/BC3/BC5 codec, images, noise.
//
// Textures are decoded to floating-point pixels, edited per pixel, and encoded
// again with a principal-axis + least-squares BC1 encoder and an exhaustive
// two-mode BC3 alpha encoder (the same family of methods as the well-known
// stb_dxt / squish encoders). Editing pixels instead of compressed blocks is
// what removes the 4x4 "blockiness" of simple endpoint recolouring.
// C# 5 only (Windows PowerShell 5.1 compiles it).
// =============================================================================
using System;
using System.Threading.Tasks;

// Linear float image, one array per channel, values 0..1.
public class Img
{
    public int W, H;
    public float[] R, G, B, A;
    public Img(int w, int h)
    {
        W = w; H = h;
        R = new float[w * h]; G = new float[w * h]; B = new float[w * h]; A = new float[w * h];
        for (int i = 0; i < A.Length; i++) A[i] = 1f;
    }
    public Img Clone()
    {
        var c = new Img(W, H);
        Array.Copy(R, c.R, R.Length); Array.Copy(G, c.G, G.Length); Array.Copy(B, c.B, B.Length); Array.Copy(A, c.A, A.Length);
        return c;
    }
}

public static class Bc
{
    public const int BC1 = 1, BC3 = 3, BC5 = 5, OTHER = 0;

    public static int BlockBytes(int fmt) { return fmt == BC1 ? 8 : 16; }
    public static long MipBytes(int fmt, int w, int h)
    {
        long bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
        return bw * bh * BlockBytes(fmt);
    }

    // ------------------------------------------------------------------ sRGB
    public static float SrgbToLinear(float c) { c = c < 0 ? 0 : (c > 1 ? 1 : c); return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)); }
    public static float LinearToSrgb(float c) { c = c < 0 ? 0 : (c > 1 ? 1 : c); return (float)(c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055); }
    public static void ToLinear(Img im)
    {
        Parallel.For(0, im.H, y => { for (int x = 0, i = y * im.W; x < im.W; x++, i++) { im.R[i] = SrgbToLinear(im.R[i]); im.G[i] = SrgbToLinear(im.G[i]); im.B[i] = SrgbToLinear(im.B[i]); } });
    }
    public static void ToSrgb(Img im)
    {
        Parallel.For(0, im.H, y => { for (int x = 0, i = y * im.W; x < im.W; x++, i++) { im.R[i] = LinearToSrgb(im.R[i]); im.G[i] = LinearToSrgb(im.G[i]); im.B[i] = LinearToSrgb(im.B[i]); } });
    }

    // ------------------------------------------------------------------ decode
    static void Rgb565(int c, out float r, out float g, out float b)
    {
        int r5 = (c >> 11) & 31, g6 = (c >> 5) & 63, b5 = c & 31;
        r = ((r5 << 3) | (r5 >> 2)) / 255f; g = ((g6 << 2) | (g6 >> 4)) / 255f; b = ((b5 << 3) | (b5 >> 2)) / 255f;
    }

    // Decodes one mip level at src[off..] into an image of w x h.
    public static Img Decode(byte[] src, long off, int fmt, int w, int h)
    {
        var im = new Img(w, h);
        int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
        int bb = BlockBytes(fmt);
        Parallel.For(0, bh, by =>
        {
            var pr = new float[4]; var pg = new float[4]; var pb = new float[4]; var pa = new float[4];
            var alpha = new float[16];
            for (int bx = 0; bx < bw; bx++)
            {
                long p = off + ((long)by * bw + bx) * bb;
                if (fmt == BC5)
                {
                    DecodeAlpha(src, p, alpha);
                    var g2 = new float[16]; DecodeAlpha(src, p + 8, g2);
                    for (int k = 0; k < 16; k++)
                    {
                        int x = bx * 4 + (k & 3), y = by * 4 + (k >> 2);
                        if (x >= w || y >= h) continue;
                        int i = y * w + x;
                        float nx = alpha[k] * 2 - 1, ny = g2[k] * 2 - 1;
                        float nz = (float)Math.Sqrt(Math.Max(0, 1 - nx * nx - ny * ny));
                        im.R[i] = alpha[k]; im.G[i] = g2[k]; im.B[i] = nz * 0.5f + 0.5f; im.A[i] = 1;
                    }
                    continue;
                }
                long cp = fmt == BC3 ? p + 8 : p;
                int c0 = src[cp] | (src[cp + 1] << 8), c1 = src[cp + 2] | (src[cp + 3] << 8);
                uint idx = (uint)(src[cp + 4] | (src[cp + 5] << 8) | (src[cp + 6] << 16) | (src[cp + 7] << 24));
                Rgb565(c0, out pr[0], out pg[0], out pb[0]); Rgb565(c1, out pr[1], out pg[1], out pb[1]);
                pa[0] = pa[1] = pa[2] = pa[3] = 1;
                if (fmt == BC3 || c0 > c1)
                {
                    pr[2] = (2 * pr[0] + pr[1]) / 3; pg[2] = (2 * pg[0] + pg[1]) / 3; pb[2] = (2 * pb[0] + pb[1]) / 3;
                    pr[3] = (pr[0] + 2 * pr[1]) / 3; pg[3] = (pg[0] + 2 * pg[1]) / 3; pb[3] = (pb[0] + 2 * pb[1]) / 3;
                }
                else
                {
                    pr[2] = (pr[0] + pr[1]) / 2; pg[2] = (pg[0] + pg[1]) / 2; pb[2] = (pb[0] + pb[1]) / 2;
                    pr[3] = pg[3] = pb[3] = 0; pa[3] = 0;
                }
                if (fmt == BC3) DecodeAlpha(src, p, alpha);
                for (int k = 0; k < 16; k++)
                {
                    int x = bx * 4 + (k & 3), y = by * 4 + (k >> 2);
                    if (x >= w || y >= h) continue;
                    int s = (int)((idx >> (2 * k)) & 3);
                    int i = y * w + x;
                    im.R[i] = pr[s]; im.G[i] = pg[s]; im.B[i] = pb[s];
                    im.A[i] = fmt == BC3 ? alpha[k] : pa[s];
                }
            }
        });
        return im;
    }

    static void DecodeAlpha(byte[] src, long p, float[] outA)
    {
        int a0 = src[p], a1 = src[p + 1];
        var pal = new float[8];
        pal[0] = a0; pal[1] = a1;
        if (a0 > a1) for (int i = 2; i < 8; i++) pal[i] = ((8 - i) * a0 + (i - 1) * a1) / 7f;
        else { for (int i = 2; i < 6; i++) pal[i] = ((6 - i) * a0 + (i - 1) * a1) / 5f; pal[6] = 0; pal[7] = 255; }
        ulong bits = 0;
        for (int i = 0; i < 6; i++) bits |= (ulong)src[p + 2 + i] << (8 * i);
        for (int k = 0; k < 16; k++) outA[k] = pal[(int)((bits >> (3 * k)) & 7)] / 255f;
    }

    // ------------------------------------------------------------------ encode
    // Encodes one image as one mip level. punch: BC1 with 1-bit alpha (alpha < 0.5 -> transparent).
    public static byte[] Encode(Img im, int fmt, bool punch)
    {
        int w = im.W, h = im.H;
        int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
        int bb = BlockBytes(fmt);
        var dst = new byte[(long)bw * bh * bb];
        Parallel.For(0, bh, by =>
        {
            var px = new float[48]; var al = new float[16]; var tr = new bool[16];
            for (int bx = 0; bx < bw; bx++)
            {
                for (int k = 0; k < 16; k++)
                {
                    int x = Math.Min(w - 1, bx * 4 + (k & 3)), y = Math.Min(h - 1, by * 4 + (k >> 2));   // clamp: edge texels repeat
                    int i = y * w + x;
                    px[k * 3] = Clamp(im.R[i]); px[k * 3 + 1] = Clamp(im.G[i]); px[k * 3 + 2] = Clamp(im.B[i]);
                    al[k] = Clamp(im.A[i]); tr[k] = punch && al[k] < 0.5f;
                }
                int off = (by * bw + bx) * bb;
                if (fmt == BC3) { EncodeAlpha(al, dst, off); EncodeColour(px, null, false, dst, off + 8); }
                else EncodeColour(px, tr, punch, dst, off);
            }
        });
        return dst;
    }

    static float Clamp(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

    static readonly float WR = 0.299f * 3, WG = 0.587f * 3, WB = 0.114f * 3;   // perceptual error weights

    static int Quant(float r, float g, float b)
    {
        int r5 = (int)Math.Round(Clamp(r) * 31), g6 = (int)Math.Round(Clamp(g) * 63), b5 = (int)Math.Round(Clamp(b) * 31);
        return (r5 << 11) | (g6 << 5) | b5;
    }

    // Colour block: principal axis -> endpoints -> two least-squares refinements.
    static void EncodeColour(float[] px, bool[] tr, bool punchMode, byte[] dst, int off)
    {
        int n = 0;
        float mr = 0, mg = 0, mb = 0;
        bool anyTr = false;
        for (int k = 0; k < 16; k++)
        {
            if (tr != null && tr[k]) { anyTr = true; continue; }
            mr += px[k * 3]; mg += px[k * 3 + 1]; mb += px[k * 3 + 2]; n++;
        }
        if (n == 0) { dst[off] = 0; dst[off + 1] = 0; dst[off + 2] = 0; dst[off + 3] = 0; dst[off + 4] = dst[off + 5] = dst[off + 6] = dst[off + 7] = 0xFF; return; }
        mr /= n; mg /= n; mb /= n;
        bool three = punchMode && anyTr;          // 3-colour + transparent mode
        // Covariance and principal axis (power iteration).
        float cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
        for (int k = 0; k < 16; k++)
        {
            if (tr != null && tr[k]) continue;
            float dr = px[k * 3] - mr, dg = px[k * 3 + 1] - mg, db = px[k * 3 + 2] - mb;
            cxx += dr * dr; cxy += dr * dg; cxz += dr * db; cyy += dg * dg; cyz += dg * db; czz += db * db;
        }
        float ax = 0.577f, ay = 0.577f, az = 0.577f;
        for (int it = 0; it < 6; it++)
        {
            float nx = cxx * ax + cxy * ay + cxz * az, ny = cxy * ax + cyy * ay + cyz * az, nz = cxz * ax + cyz * ay + czz * az;
            float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (l < 1e-9f) break;
            ax = nx / l; ay = ny / l; az = nz / l;
        }
        float tmin = 1e9f, tmax = -1e9f;
        for (int k = 0; k < 16; k++)
        {
            if (tr != null && tr[k]) continue;
            float t = (px[k * 3] - mr) * ax + (px[k * 3 + 1] - mg) * ay + (px[k * 3 + 2] - mb) * az;
            if (t < tmin) tmin = t; if (t > tmax) tmax = t;
        }
        float[] e0 = { mr + ax * tmax, mg + ay * tmax, mb + az * tmax };
        float[] e1 = { mr + ax * tmin, mg + ay * tmin, mb + az * tmin };
        int bestC0 = 0, bestC1 = 0; uint bestIdx = 0; float bestErr = float.MaxValue;
        var idx = new int[16];
        for (int pass = 0; pass < 3; pass++)
        {
            int q0 = Quant(e0[0], e0[1], e0[2]), q1 = Quant(e1[0], e1[1], e1[2]);
            float err = Fit(px, tr, three, q0, q1, idx);
            if (err < bestErr)
            {
                bestErr = err; bestC0 = q0; bestC1 = q1; bestIdx = 0;
                for (int k = 0; k < 16; k++) bestIdx |= (uint)idx[k] << (2 * k);
            }
            if (pass == 2) break;
            // Least squares for the endpoints given the indices.
            float aa = 0, ab = 0, bb2 = 0;
            float axr = 0, axg = 0, axb = 0, bxr = 0, bxg = 0, bxb = 0;
            for (int k = 0; k < 16; k++)
            {
                if (tr != null && tr[k]) continue;
                float wgt;
                int s = idx[k];
                if (three) wgt = s == 0 ? 1f : (s == 1 ? 0f : 0.5f);
                else wgt = s == 0 ? 1f : (s == 1 ? 0f : (s == 2 ? 2f / 3 : 1f / 3));
                float iw = 1 - wgt;
                aa += wgt * wgt; ab += wgt * iw; bb2 += iw * iw;
                axr += wgt * px[k * 3]; axg += wgt * px[k * 3 + 1]; axb += wgt * px[k * 3 + 2];
                bxr += iw * px[k * 3]; bxg += iw * px[k * 3 + 1]; bxb += iw * px[k * 3 + 2];
            }
            float det = aa * bb2 - ab * ab;
            if (Math.Abs(det) < 1e-8f) break;
            float f = 1 / det;
            e0 = new[] { (axr * bb2 - bxr * ab) * f, (axg * bb2 - bxg * ab) * f, (axb * bb2 - bxb * ab) * f };
            e1 = new[] { (bxr * aa - axr * ab) * f, (bxg * aa - axg * ab) * f, (bxb * aa - axb * ab) * f };
        }
        // Put the endpoints in the order the mode needs.
        int c0 = bestC0, c1 = bestC1; uint ix = bestIdx;
        if (three)
        {
            if (c0 > c1) { int t = c0; c0 = c1; c1 = t; ix = SwapIdx01(ix); }
        }
        else
        {
            if (c0 < c1) { int t = c0; c0 = c1; c1 = t; ix ^= 0x55555555u; }
            else if (c0 == c1) ix = 0;
        }
        dst[off] = (byte)c0; dst[off + 1] = (byte)(c0 >> 8); dst[off + 2] = (byte)c1; dst[off + 3] = (byte)(c1 >> 8);
        dst[off + 4] = (byte)ix; dst[off + 5] = (byte)(ix >> 8); dst[off + 6] = (byte)(ix >> 16); dst[off + 7] = (byte)(ix >> 24);
    }

    static uint SwapIdx01(uint ix)
    {
        uint o = 0;
        for (int k = 0; k < 16; k++) { uint v = (ix >> (2 * k)) & 3; if (v < 2) v ^= 1; o |= v << (2 * k); }
        return o;
    }

    // Best index per texel for endpoints q0/q1 in the given mode; returns the weighted error.
    static float Fit(float[] px, bool[] tr, bool three, int q0, int q1, int[] idx)
    {
        float r0, g0, b0, r1, g1, b1;
        Rgb565(q0, out r0, out g0, out b0); Rgb565(q1, out r1, out g1, out b1);
        var pr = new float[4]; var pg = new float[4]; var pb = new float[4];
        pr[0] = r0; pg[0] = g0; pb[0] = b0; pr[1] = r1; pg[1] = g1; pb[1] = b1;
        int count;
        if (three) { pr[2] = (r0 + r1) / 2; pg[2] = (g0 + g1) / 2; pb[2] = (b0 + b1) / 2; count = 3; }
        else
        {
            pr[2] = (2 * r0 + r1) / 3; pg[2] = (2 * g0 + g1) / 3; pb[2] = (2 * b0 + b1) / 3;
            pr[3] = (r0 + 2 * r1) / 3; pg[3] = (g0 + 2 * g1) / 3; pb[3] = (b0 + 2 * b1) / 3; count = 4;
        }
        float err = 0;
        for (int k = 0; k < 16; k++)
        {
            if (tr != null && tr[k]) { idx[k] = 3; continue; }
            float best = float.MaxValue; int bi = 0;
            for (int s = 0; s < count; s++)
            {
                float dr = px[k * 3] - pr[s], dg = px[k * 3 + 1] - pg[s], db = px[k * 3 + 2] - pb[s];
                float e = WR * dr * dr + WG * dg * dg + WB * db * db;
                if (e < best) { best = e; bi = s; }
            }
            idx[k] = bi; err += best;
        }
        return err;
    }

    // BC3/BC4 alpha block: tries the 8-value and the 6-value (+0/255) mode.
    public static void EncodeAlpha(float[] a, byte[] dst, int off)
    {
        var v = new int[16];
        int mn = 255, mx = 0, mn6 = 255, mx6 = 0; bool has0or255 = false;
        for (int k = 0; k < 16; k++)
        {
            v[k] = (int)Math.Round(Clamp(a[k]) * 255);
            if (v[k] < mn) mn = v[k]; if (v[k] > mx) mx = v[k];
            if (v[k] == 0 || v[k] == 255) has0or255 = true;
            else { if (v[k] < mn6) mn6 = v[k]; if (v[k] > mx6) mx6 = v[k]; }
        }
        ulong bits; int a0, a1;
        if (mn == mx) { a0 = mx; a1 = mn; bits = 0; }
        else
        {
            int e8; ulong b8 = AlphaFit(v, mx, mn, true, out e8);
            a0 = mx; a1 = mn; bits = b8;
            if (has0or255)
            {
                int lo = mn6 > mx6 ? 0 : mn6, hi = mn6 > mx6 ? 255 : mx6;
                int e6; ulong b6 = AlphaFit(v, lo, hi, false, out e6);
                if (e6 < e8) { a0 = lo; a1 = hi; bits = b6; }
            }
        }
        dst[off] = (byte)a0; dst[off + 1] = (byte)a1;
        for (int i = 0; i < 6; i++) dst[off + 2 + i] = (byte)(bits >> (8 * i));
    }

    static ulong AlphaFit(int[] v, int a0, int a1, bool eight, out int err)
    {
        var pal = new int[8];
        pal[0] = a0; pal[1] = a1;
        if (eight) for (int i = 2; i < 8; i++) pal[i] = ((8 - i) * a0 + (i - 1) * a1) / 7;
        else { for (int i = 2; i < 6; i++) pal[i] = ((6 - i) * a0 + (i - 1) * a1) / 5; pal[6] = 0; pal[7] = 255; }
        ulong bits = 0; err = 0;
        for (int k = 0; k < 16; k++)
        {
            int best = int.MaxValue, bi = 0;
            for (int s = 0; s < 8; s++) { int d = Math.Abs(v[k] - pal[s]); if (d < best) { best = d; bi = s; } }
            bits |= (ulong)bi << (3 * k); err += best * best;
        }
        return bits;
    }

    // ------------------------------------------------------------------ resampling
    // Halves an image (box filter). Colour must already be linear.
    public static Img Down(Img s)
    {
        int w = Math.Max(1, s.W / 2), h = Math.Max(1, s.H / 2);
        var d = new Img(w, h);
        Parallel.For(0, h, y =>
        {
            int y0 = Math.Min(s.H - 1, y * 2), y1 = Math.Min(s.H - 1, y * 2 + 1);
            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Min(s.W - 1, x * 2), x1 = Math.Min(s.W - 1, x * 2 + 1);
                int a = y0 * s.W + x0, b = y0 * s.W + x1, c = y1 * s.W + x0, e = y1 * s.W + x1, i = y * w + x;
                // Alpha-weighted colour so transparent texels don't bleed dark fringes into leaves.
                float wa = s.A[a] + 1e-4f, wb = s.A[b] + 1e-4f, wc = s.A[c] + 1e-4f, we = s.A[e] + 1e-4f, ws = wa + wb + wc + we;
                d.R[i] = (s.R[a] * wa + s.R[b] * wb + s.R[c] * wc + s.R[e] * we) / ws;
                d.G[i] = (s.G[a] * wa + s.G[b] * wb + s.G[c] * wc + s.G[e] * we) / ws;
                d.B[i] = (s.B[a] * wa + s.B[b] * wb + s.B[c] * wc + s.B[e] * we) / ws;
                d.A[i] = (s.A[a] + s.A[b] + s.A[c] + s.A[e]) * 0.25f;
            }
        });
        return d;
    }

    static float Cubic(float a, float b, float c, float d, float t)   // Catmull-Rom
    {
        return b + 0.5f * t * (c - a + t * (2 * a - 5 * b + 4 * c - d + t * (3 * (b - c) + d - a)));
    }

    // Doubles an image with a tiling Catmull-Rom filter (textures repeat).
    public static Img Up2(Img s)
    {
        int w = s.W * 2, h = s.H * 2;
        var d = new Img(w, h);
        Parallel.For(0, h, y =>
        {
            float sy = (y + 0.5f) / 2 - 0.5f; int iy = (int)Math.Floor(sy); float fy = sy - iy;
            var rows = new int[4];
            for (int k = 0; k < 4; k++) rows[k] = (((iy - 1 + k) % s.H) + s.H) % s.H;
            for (int x = 0; x < w; x++)
            {
                float sx = (x + 0.5f) / 2 - 0.5f; int ix = (int)Math.Floor(sx); float fx = sx - ix;
                int i = y * w + x;
                d.R[i] = Sample(s.R, s, rows, ix, fx, fy); d.G[i] = Sample(s.G, s, rows, ix, fx, fy);
                d.B[i] = Sample(s.B, s, rows, ix, fx, fy); d.A[i] = Clamp(Sample(s.A, s, rows, ix, fx, fy));
            }
        });
        return d;
    }

    static float Sample(float[] c, Img s, int[] rows, int ix, float fx, float fy)
    {
        int x0 = (((ix - 1) % s.W) + s.W) % s.W, x1 = ((ix % s.W) + s.W) % s.W, x2 = (((ix + 1) % s.W) + s.W) % s.W, x3 = (((ix + 2) % s.W) + s.W) % s.W;
        int o0 = rows[0] * s.W, o1 = rows[1] * s.W, o2 = rows[2] * s.W, o3 = rows[3] * s.W;
        float c0 = Cubic(c[o0 + x0], c[o0 + x1], c[o0 + x2], c[o0 + x3], fx);
        float c1 = Cubic(c[o1 + x0], c[o1 + x1], c[o1 + x2], c[o1 + x3], fx);
        float c2 = Cubic(c[o2 + x0], c[o2 + x1], c[o2 + x2], c[o2 + x3], fx);
        float c3 = Cubic(c[o3 + x0], c[o3 + x1], c[o3 + x2], c[o3 + x3], fx);
        return Cubic(c0, c1, c2, c3, fy);
    }

    // Separable box blur (two passes ~ Gaussian), tiling.
    public static float[] Blur(float[] src, int w, int h, int radius)
    {
        var a = (float[])src.Clone();
        var b = new float[a.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            BoxH(a, b, w, h, radius); BoxV(b, a, w, h, radius);
        }
        return a;
    }
    static void BoxH(float[] s, float[] d, int w, int h, int r)
    {
        Parallel.For(0, h, y =>
        {
            int o = y * w; float sum = 0; int n = 2 * r + 1;
            for (int k = -r; k <= r; k++) sum += s[o + ((k % w) + w) % w];
            for (int x = 0; x < w; x++)
            {
                d[o + x] = sum / n;
                sum += s[o + ((x + r + 1) % w)] - s[o + (((x - r) % w) + w) % w];
            }
        });
    }
    static void BoxV(float[] s, float[] d, int w, int h, int r)
    {
        Parallel.For(0, w, x =>
        {
            float sum = 0; int n = 2 * r + 1;
            for (int k = -r; k <= r; k++) sum += s[(((k % h) + h) % h) * w + x];
            for (int y = 0; y < h; y++)
            {
                d[y * w + x] = sum / n;
                sum += s[((y + r + 1) % h) * w + x] - s[(((y - r) % h) + h) % h * w + x];
            }
        });
    }

    // ------------------------------------------------------------------ noise (tiling)
    public static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
    public static float Noise(float u, float v, int freq, int seed)
    {
        float x = u * freq, y = v * freq;
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int x0 = ((xi % freq) + freq) % freq, y0 = ((yi % freq) + freq) % freq;
        int x1 = (x0 + 1) % freq, y1 = (y0 + 1) % freq;
        float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
        return a + (b - a) * fx + (c + (d - c) * fx - (a + (b - a) * fx)) * fy;
    }
    public static float Fbm(float u, float v, int freq, int seed, int octaves)
    {
        float s = 0, amp = 0.55f, tot = 0;
        for (int o = 0; o < octaves; o++) { s += amp * Noise(u, v, freq << o, seed + o * 31); tot += amp; amp *= 0.5f; }
        return s / tot;
    }
}
