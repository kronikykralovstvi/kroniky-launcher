// =============================================================================
// Harsh Winter builder - BC7 (BPTC) decoding and a mode-6 encoder.
//
// The decoder follows the BC7 decoder of texture2ddecoder (K0lb3, MIT
// licence), itself derived from bimg (Branimir Karadzic, BSD-2) - partition
// and anchor tables copied from it. The encoder writes every block in mode 6
// (one subset, 7-bit RGBA endpoints + p-bits, 16 index levels): principal-axis
// endpoints with a least-squares refinement, which keeps painted UI art
// (the loading screens) far smoother than BC1/BC3 would.
// C# 5 only (Windows PowerShell 5.1 compiles it).
// =============================================================================
using System;
using System.Threading.Tasks;

public static class Bc7
{
    static readonly ushort[] P2 = { 0xcccc, 0x8888, 0xeeee, 0xecc8, 0xc880, 0xfeec, 0xfec8, 0xec80, 0xc800, 0xffec, 0xfe80, 0xe800, 0xffe8, 0xff00, 0xfff0, 0xf000, 0xf710, 0x008e, 0x7100, 0x08ce, 0x008c, 0x7310, 0x3100, 0x8cce, 0x088c, 0x3110, 0x6666, 0x366c, 0x17e8, 0x0ff0, 0x718e, 0x399c, 0xaaaa, 0xf0f0, 0x5a5a, 0x33cc, 0x3c3c, 0x55aa, 0x9696, 0xa55a, 0x73ce, 0x13c8, 0x324c, 0x3bdc, 0x6996, 0xc33c, 0x9966, 0x0660, 0x0272, 0x04e4, 0x4e40, 0x2720, 0xc936, 0x936c, 0x39c6, 0x639c, 0x9336, 0x9cc6, 0x817e, 0xe718, 0xccf0, 0x0fcc, 0x7744, 0xee22 };
    static readonly byte[] A2 = { 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2, 15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6, 6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2, 2, 15 };
    static readonly uint[] P3 = { 0xaa685050u, 0x6a5a5040u, 0x5a5a4200u, 0x5450a0a8u, 0xa5a50000u, 0xa0a05050u, 0x5555a0a0u, 0x5a5a5050u, 0xaa550000u, 0xaa555500u, 0xaaaa5500u, 0x90909090u, 0x94949494u, 0xa4a4a4a4u, 0xa9a59450u, 0x2a0a4250u, 0xa5945040u, 0x0a425054u, 0xa5a5a500u, 0x55a0a0a0u, 0xa8a85454u, 0x6a6a4040u, 0xa4a45000u, 0x1a1a0500u, 0x0050a4a4u, 0xaaa59090u, 0x14696914u, 0x69691400u, 0xa08585a0u, 0xaa821414u, 0x50a4a450u, 0x6a5a0200u, 0xa9a58000u, 0x5090a0a8u, 0xa8a09050u, 0x24242424u, 0x00aa5500u, 0x24924924u, 0x24499224u, 0x50a50a50u, 0x500aa550u, 0xaaaa4444u, 0x66660000u, 0xa5a0a5a0u, 0x50a050a0u, 0x69286928u, 0x44aaaa44u, 0x66666600u, 0xaa444444u, 0x54a854a8u, 0x95809580u, 0x96969600u, 0xa85454a8u, 0x80959580u, 0xaa141414u, 0x96960000u, 0xaaaa1414u, 0xa05050a0u, 0xa0a5a5a0u, 0x96000000u, 0x40804080u, 0xa9a8a9a8u, 0xaaaaaa44u, 0x2a4a5254u };
    static readonly byte[] A3a = { 3, 3, 15, 15, 8, 3, 15, 15, 8, 8, 6, 6, 6, 5, 3, 3, 3, 3, 8, 15, 3, 3, 6, 10, 5, 8, 8, 6, 8, 5, 15, 15, 8, 15, 3, 5, 6, 10, 8, 15, 15, 3, 15, 5, 15, 15, 15, 15, 3, 15, 5, 5, 5, 8, 5, 10, 5, 10, 8, 13, 15, 12, 3, 3 };
    static readonly byte[] A3b = { 15, 8, 8, 3, 15, 15, 3, 8, 15, 15, 15, 15, 15, 15, 15, 8, 15, 8, 15, 3, 15, 8, 15, 8, 3, 15, 6, 10, 15, 15, 10, 8, 15, 3, 15, 10, 10, 8, 9, 10, 6, 15, 8, 15, 3, 6, 6, 8, 15, 3, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 3, 15, 15, 8 };
    static readonly byte[][] Factors = {
        new byte[] { 0, 21, 43, 64 },
        new byte[] { 0, 9, 18, 27, 37, 46, 55, 64 },
        new byte[] { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 } };
    // numSubsets, partitionBits, rotationBits, indexSelectionBits, colorBits, alphaBits, endpointPBits, sharedPBits, indexBits0, indexBits1
    static readonly byte[,] Modes = {
        { 3, 4, 0, 0, 4, 0, 1, 0, 3, 0 }, { 2, 6, 0, 0, 6, 0, 0, 1, 3, 0 }, { 3, 6, 0, 0, 5, 0, 0, 0, 2, 0 }, { 2, 6, 0, 0, 7, 0, 1, 0, 2, 0 },
        { 1, 0, 2, 1, 5, 6, 0, 0, 2, 3 }, { 1, 0, 2, 0, 7, 8, 0, 0, 2, 2 }, { 1, 0, 0, 0, 7, 7, 1, 0, 4, 0 }, { 2, 6, 0, 0, 5, 5, 1, 0, 2, 0 } };

    struct Bits
    {
        public byte[] D; public int Base, Pos;
        public int Read(int n) { int v = Peek(0, n); Pos += n; return v; }
        public int Peek(int off, int n)
        {
            if (n == 0) return 0;
            int bp = Pos + off, v = 0;
            for (int i = 0; i < n; i++) { int b = bp + i; v |= ((D[Base + (b >> 3)] >> (b & 7)) & 1) << i; }
            return v;
        }
    }

    static int Expand(int v, int bits) { v = (v << (8 - bits)) & 255; return v | (v >> bits); }

    // One block into rgba[64] (bytes, RGBA order).
    public static void DecodeBlock(byte[] src, int off, byte[] rgba)
    {
        var bit = new Bits { D = src, Base = off, Pos = 0 };
        int mode = 0;
        while (mode < 8 && bit.Read(1) == 0) mode++;
        if (mode == 8) { Array.Clear(rgba, 0, 64); return; }
        int ns = Modes[mode, 0], pb = Modes[mode, 1], rb = Modes[mode, 2], isb = Modes[mode, 3], cb = Modes[mode, 4], ab = Modes[mode, 5];
        int epb = Modes[mode, 6], spb = Modes[mode, 7], ib0 = Modes[mode, 8], ib1 = Modes[mode, 9];
        int mpb = epb != 0 ? epb : spb;
        int part = bit.Read(pb), rot = bit.Read(rb), isel = bit.Read(isb);
        var ep = new int[4, 6];
        for (int c = 0; c < 3; c++) for (int i = 0; i < ns * 2; i++) ep[c, i] = bit.Read(cb) << mpb;
        if (ab != 0) for (int i = 0; i < ns * 2; i++) ep[3, i] = bit.Read(ab) << mpb;
        else for (int i = 0; i < 6; i++) ep[3, i] = 255;
        if (mpb != 0)
            for (int s = 0; s < ns; s++)
            {
                int pa = bit.Read(mpb), pbv = spb == 0 ? bit.Read(mpb) : pa;
                for (int c = 0; c < 4; c++) { if (c == 3 && ab == 0) continue; ep[c, s * 2] |= pa; ep[c, s * 2 + 1] |= pbv; }
            }
        for (int c = 0; c < 3; c++) for (int i = 0; i < ns * 2; i++) ep[c, i] = Expand(ep[c, i], cb + mpb);
        if (ab != 0) for (int i = 0; i < ns * 2; i++) ep[3, i] = Expand(ep[3, i], ab + mpb);
        bool has1 = ib1 != 0;
        byte[] f0 = Factors[ib0 - 2], f1 = has1 ? Factors[ib1 - 2] : f0;
        int o0 = 0, o1 = ns * (16 * ib0 - 1);
        for (int idx = 0; idx < 16; idx++)
        {
            int sub = 0, anchor = 0;
            if (ns == 2) { sub = (P2[part] >> idx) & 1; anchor = sub != 0 ? A2[part] : 0; }
            else if (ns == 3) { sub = (int)((P3[part] >> (2 * idx)) & 3); anchor = sub != 0 ? (sub == 1 ? A3a[part] : A3b[part]) : 0; }
            int isAnchor = idx == anchor ? 1 : 0;
            int n0 = ib0 - isAnchor, n1 = has1 ? ib1 - isAnchor : 0;
            int i0 = bit.Peek(o0, n0), i1 = has1 ? bit.Peek(o1, n1) : i0;
            o0 += n0; o1 += n1;
            int fc = isel == 0 ? f0[i0] : f1[i1];
            int fa = isel == 0 ? f1[i1] : f0[i0];
            int s2 = sub * 2;
            int r = (ep[0, s2] * (64 - fc) + ep[0, s2 + 1] * fc + 32) >> 6;
            int g = (ep[1, s2] * (64 - fc) + ep[1, s2 + 1] * fc + 32) >> 6;
            int b = (ep[2, s2] * (64 - fc) + ep[2, s2 + 1] * fc + 32) >> 6;
            int a = (ep[3, s2] * (64 - fa) + ep[3, s2 + 1] * fa + 32) >> 6;
            int t;
            if (rot == 1) { t = a; a = r; r = t; } else if (rot == 2) { t = a; a = g; g = t; } else if (rot == 3) { t = a; a = b; b = t; }
            rgba[idx * 4] = (byte)r; rgba[idx * 4 + 1] = (byte)g; rgba[idx * 4 + 2] = (byte)b; rgba[idx * 4 + 3] = (byte)a;
        }
    }

    public static Img Decode(byte[] src, long off, int w, int h)
    {
        var im = new Img(w, h);
        int bw = (w + 3) / 4, bh = (h + 3) / 4;
        Parallel.For(0, bh, by =>
        {
            var px = new byte[64];
            for (int bx = 0; bx < bw; bx++)
            {
                DecodeBlock(src, (int)(off + ((long)by * bw + bx) * 16), px);
                for (int k = 0; k < 16; k++)
                {
                    int x = bx * 4 + (k & 3), y = by * 4 + (k >> 2);
                    if (x >= w || y >= h) continue;
                    int i = y * w + x;
                    im.R[i] = px[k * 4] / 255f; im.G[i] = px[k * 4 + 1] / 255f; im.B[i] = px[k * 4 + 2] / 255f; im.A[i] = px[k * 4 + 3] / 255f;
                }
            }
        });
        return im;
    }

    // ------------------------------------------------------------------ mode 6 encoder
    static void PutBits(byte[] d, int off, ref int pos, int v, int n)
    {
        for (int i = 0; i < n; i++) { if (((v >> i) & 1) != 0) d[off + ((pos + i) >> 3)] |= (byte)(1 << ((pos + i) & 7)); }
        pos += n;
    }

    // Quantise an 8-bit-range endpoint to 7 bits + p-bit; returns the reconstructed values.
    static void Quant(float[] e, int p, int[] q7, float[] rec)
    {
        for (int c = 0; c < 4; c++)
        {
            int v = (int)Math.Round((e[c] * 255f - p) / 2f);
            v = v < 0 ? 0 : (v > 127 ? 127 : v);
            q7[c] = v; rec[c] = ((v << 1) | p) / 255f;
        }
    }

    static float Fit(float[] px, float[] e0, float[] e1, int[] idx)
    {
        float err = 0;
        var f = Factors[2];
        for (int k = 0; k < 16; k++)
        {
            float best = float.MaxValue; int bi = 0;
            for (int j = 0; j < 16; j++)
            {
                float t = f[j] / 64f, d = 0;
                for (int c = 0; c < 4; c++) { float v = e0[c] + (e1[c] - e0[c]) * t - px[k * 4 + c]; d += v * v * (c == 3 ? 0.5f : 1f); }
                if (d < best) { best = d; bi = j; }
            }
            idx[k] = bi; err += best;
        }
        return err;
    }

    public static void EncodeBlock(float[] px, byte[] dst, int off)
    {
        // Principal axis through the block's colours (RGBA).
        var mean = new float[4];
        for (int k = 0; k < 16; k++) for (int c = 0; c < 4; c++) mean[c] += px[k * 4 + c] / 16f;
        var cov = new float[4, 4];
        for (int k = 0; k < 16; k++) for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++) cov[a, b] += (px[k * 4 + a] - mean[a]) * (px[k * 4 + b] - mean[b]);
        var axis = new float[] { 0.58f, 0.58f, 0.58f, 0.1f };
        for (int it = 0; it < 8; it++)
        {
            var n = new float[4]; float len = 0;
            for (int a = 0; a < 4; a++) { for (int b = 0; b < 4; b++) n[a] += cov[a, b] * axis[b]; len += n[a] * n[a]; }
            len = (float)Math.Sqrt(len); if (len < 1e-8f) break;
            for (int a = 0; a < 4; a++) axis[a] = n[a] / len;
        }
        float lo = float.MaxValue, hi = float.MinValue;
        for (int k = 0; k < 16; k++)
        {
            float t = 0; for (int c = 0; c < 4; c++) t += (px[k * 4 + c] - mean[c]) * axis[c];
            if (t < lo) lo = t; if (t > hi) hi = t;
        }
        var e0 = new float[4]; var e1 = new float[4];
        for (int c = 0; c < 4; c++) { e0[c] = Clamp(mean[c] + axis[c] * lo); e1[c] = Clamp(mean[c] + axis[c] * hi); }
        int[] bestIdx = new int[16], idx = new int[16]; int[] bq0 = new int[4], bq1 = new int[4]; int bp0 = 0, bp1 = 0;
        float bestErr = float.MaxValue;
        var q0 = new int[4]; var q1 = new int[4]; var r0 = new float[4]; var r1 = new float[4];
        for (int round = 0; round < 2; round++)
        {
            for (int p0 = 0; p0 < 2; p0++)
                for (int p1 = 0; p1 < 2; p1++)
                {
                    Quant(e0, p0, q0, r0); Quant(e1, p1, q1, r1);
                    float err = Fit(px, r0, r1, idx);
                    if (err < bestErr) { bestErr = err; Array.Copy(idx, bestIdx, 16); Array.Copy(q0, bq0, 4); Array.Copy(q1, bq1, 4); bp0 = p0; bp1 = p1; }
                }
            // least-squares refinement of the endpoints for the chosen indices
            float aa = 0, bb = 0, ab = 0; var ax = new float[4]; var bx = new float[4];
            for (int k = 0; k < 16; k++)
            {
                float t = Factors[2][bestIdx[k]] / 64f, s = 1 - t;
                aa += s * s; bb += t * t; ab += s * t;
                for (int c = 0; c < 4; c++) { ax[c] += s * px[k * 4 + c]; bx[c] += t * px[k * 4 + c]; }
            }
            float det = aa * bb - ab * ab;
            if (Math.Abs(det) < 1e-6f) break;
            for (int c = 0; c < 4; c++) { e0[c] = Clamp((ax[c] * bb - bx[c] * ab) / det); e1[c] = Clamp((bx[c] * aa - ax[c] * ab) / det); }
        }
        // Anchor rule: index 0's top bit must be 0, else swap the endpoints.
        if (bestIdx[0] >= 8)
        {
            var t = bq0; bq0 = bq1; bq1 = t; int tp = bp0; bp0 = bp1; bp1 = tp;
            for (int k = 0; k < 16; k++) bestIdx[k] = 15 - bestIdx[k];
        }
        Array.Clear(dst, off, 16);
        int pos = 0;
        PutBits(dst, off, ref pos, 1 << 6, 7);           // mode 6: six 0 bits then a 1
        for (int c = 0; c < 4; c++) { PutBits(dst, off, ref pos, bq0[c], 7); PutBits(dst, off, ref pos, bq1[c], 7); }
        PutBits(dst, off, ref pos, bp0, 1); PutBits(dst, off, ref pos, bp1, 1);
        for (int k = 0; k < 16; k++) PutBits(dst, off, ref pos, bestIdx[k], k == 0 ? 3 : 4);
    }

    static float Clamp(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

    public static byte[] Encode(Img im)
    {
        int w = im.W, h = im.H, bw = (w + 3) / 4, bh = (h + 3) / 4;
        var dst = new byte[(long)bw * bh * 16];
        Parallel.For(0, bh, by =>
        {
            var px = new float[64];
            for (int bx = 0; bx < bw; bx++)
            {
                for (int k = 0; k < 16; k++)
                {
                    int x = Math.Min(w - 1, bx * 4 + (k & 3)), y = Math.Min(h - 1, by * 4 + (k >> 2)), i = y * w + x;
                    px[k * 4] = Clamp(im.R[i]); px[k * 4 + 1] = Clamp(im.G[i]); px[k * 4 + 2] = Clamp(im.B[i]); px[k * 4 + 3] = Clamp(im.A[i]);
                }
                EncodeBlock(px, dst, (by * bw + bx) * 16);
            }
        });
        return dst;
    }
}
