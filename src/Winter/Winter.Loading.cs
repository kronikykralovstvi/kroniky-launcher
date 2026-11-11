// =============================================================================
// Harsh Winter builder - winter loading screens.
//
// KCD2's loading screens are big painted artworks, Libs/UI/Textures/Video/
// artwork<N>.dds, shown by Libs/UI/LoadingScreen.gfx. The builder takes every
// artwork it finds - the game's own and those of a loading-screen mod you have
// installed (e.g. "Extra Loading Screens Royal Edition", artwork19-37) - and
// paints winter over it, on your PC, from your own files:
//   * a cold, grey-blue, overcast grade (less colour, bluish shadows);
//   * snow on vegetation and on the open ground below the horizon, a little on
//     roofs and lit top edges; stone walls stay stone; the sky goes overcast;
//   * a faint haze over the distance and a light fall of small snowflakes;
//   * frost creeping in from the edges of the picture (indoor scenes get only
//     the cold grade and the frost, like a frozen window).
// The result keeps the original's size, format (BC7/BC3/BC1) and number of
// mips, at the same path, in Data\harsh_winter_world_loading.pak - so when it is
// there the game shows the winter versions; delete that pak (or build with
// "noloading") and the originals come back. The paintings are recognisably the
// same scenes, but this is a filter, not a repaint: see the README.
// C# 5 only (Windows PowerShell 5.1 compiles it).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public static class WinterLoading
{
    public static int LastCount = 0;
    public static bool ScanMods = false;
    public static bool RequireMods = false;  // true: nothing to do unless a loading-screen mod is installed   // true only when making the shipped pak from the extra screens
    public static List<string> LastSources = new List<string>();
    // artwork numbers not to build (the shipped winter art already has them)
    public static HashSet<int> Skip = new HashSet<int>();
    public static List<string> Conflicts = new List<string>();

    static float S(float e0, float e1, float x) { float t = (x - e0) / (e1 - e0); t = t < 0 ? 0 : (t > 1 ? 1 : t); return t * t * (3 - 2 * t); }
    static float Cl(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

    // Gaussian-like blur of sigma (pixels) with the builder's two-pass box blur.
    static float[] G(float[] a, int w, int h, float sigma)
    {
        int r = (int)Math.Round((Math.Sqrt(1 + 6 * sigma * sigma) - 1) / 2);
        if (r < 1) r = 1;
        return Bc.Blur(a, w, h, r);
    }

    static void Hsv(float r, float g, float b, out float hh, out float ss, out float vv)
    {
        float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn + 1e-6f;
        if (mx == r) { hh = ((g - b) / d) % 6; if (hh < 0) hh += 6; }
        else if (mx == g) hh = (b - r) / d + 2;
        else hh = (r - g) / d + 4;
        hh *= 60; ss = mx > 1e-6f ? d / (mx + 1e-6f) : 0; vv = mx;
    }

    // The winter painting (values are the texture's own, sRGB-encoded).
    public static Img Winterise(Img src, int seed, out bool interior)
    {
        int W = src.W, H = src.H, N = W * H;
        float k = H / 720f;                                // tuned at 2560x720
        var lum = new float[N]; var hu = new float[N]; var sa = new float[N];
        Parallel.For(0, H, y => { for (int x = 0, i = y * W; x < W; x++, i++) {
            lum[i] = src.R[i] * 0.299f + src.G[i] * 0.587f + src.B[i] * 0.114f;
            float hh, ss, vv; Hsv(src.R[i], src.G[i], src.B[i], out hh, out ss, out vv); hu[i] = hh; sa[i] = ss; } });
        // the art is centred with black bars
        int x0 = 0, x1 = W - 1;
        for (int x = 0; x < W; x++) { bool any = false; for (int y = 0; y < H; y += 4) { int i = y * W + x; if (Math.Max(src.R[i], Math.Max(src.G[i], src.B[i])) > 0.08f) { any = true; break; } } if (any) { x0 = x; break; } }
        for (int x = W - 1; x >= 0; x--) { bool any = false; for (int y = 0; y < H; y += 4) { int i = y * W + x; if (Math.Max(src.R[i], Math.Max(src.G[i], src.B[i])) > 0.08f) { any = true; break; } } if (any) { x1 = x; break; } }
        if (x1 <= x0) { x0 = 0; x1 = W - 1; }
        // interior: almost no blue sky in the upper part, and dark
        long skyN = 0, cnt = 0, greenN = 0; double lumSum = 0;
        for (int y = 0; y < H; y += 2) for (int x = x0; x <= x1; x += 2)
        {
            int i = y * W + x; float yy = y / (float)H;
            lumSum += lum[i];
            if (yy < 0.45f) { cnt++; if (hu[i] > 165 && hu[i] < 255 && sa[i] > 0.1f && lum[i] > 0.25f) skyN++; }
            if (hu[i] > 60 && hu[i] < 160 && sa[i] > 0.18f) greenN++;
        }
        double skyFrac = cnt > 0 ? skyN / (double)cnt : 0;
        double meanLum = lumSum / Math.Max(1, ((H + 1) / 2) * ((x1 - x0) / 2 + 1));
        double greenFrac = greenN / (double)Math.Max(1, ((H + 1) / 2) * ((x1 - x0) / 2 + 1));
        interior = skyFrac < 0.06 && meanLum < 0.38 && greenFrac < 0.10;
        bool inside = interior;

        var o = new Img(W, H);
        // cold grade
        float satK = inside ? 0.8f : 0.62f;
        Parallel.For(0, H, y => { for (int x = 0, i = y * W; x < W; x++, i++) {
            float g = lum[i];
            float r = g + (src.R[i] - g) * satK, gg = g + (src.G[i] - g) * satK, b = g + (src.B[i] - g) * satK;
            r = r * 0.93f + 0.02f * (1 - g); gg = gg * 0.98f + 0.03f * (1 - g); b = b * 1.06f + 0.05f * (1 - g);
            o.R[i] = r; o.G[i] = gg; o.B[i] = b; o.A[i] = src.A[i]; } });

        if (!inside)
        {
            var skyl = new float[N];
            Parallel.For(0, H, y => { float yy = y / (float)H; for (int x = 0, i = y * W; x < W; x++, i++) {
                float v = S(165, 185, hu[i]) * (1 - S(250, 265, hu[i])) * S(0.06f, 0.15f, sa[i]) + (1 - S(0.08f, 0.2f, sa[i])) * S(0.5f, 0.7f, lum[i]);
                skyl[i] = Cl(v) * (1 - S(0.55f, 0.8f, yy)); } });
            float hz = 0.5f;
            for (int y = (int)(H * 0.15f); y < (int)(H * 0.85f); y++)
            {
                double s = 0; int n = 0;
                for (int x = x0; x <= x1; x += 3) { s += skyl[y * W + x]; n++; }
                if (s / Math.Max(1, n) < 0.25) { hz = y / (float)H; break; }
            }
            var lb = G(lum, W, H, 6 * k);
            var tex = G(lum, W, H, 1.2f * k);
            int dy = Math.Max(1, (int)(10 * k));
            var Sm = new float[N];
            Parallel.For(0, H, y => { float yy = y / (float)H; float below = S(hz - 0.02f, hz + 0.06f, yy);
                for (int x = 0, i = y * W; x < W; x++, i++) {
                    float h = hu[i], s = sa[i], l = lum[i];
                    float foliage = S(0.12f, 0.3f, s) * S(55, 70, h) * (1 - S(150, 168, h));
                    float earth = S(8, 15, h) * (1 - S(55, 65, h)) * S(0.06f, 0.15f, s) * (1 - S(0.28f, 0.4f, s)) * below;
                    float roof = ((1 - S(24, 32, h)) + S(335, 345, h)) * S(0.2f, 0.35f, s) * (1 - S(0.6f, 0.8f, l)) * (1 - below * 0.5f);
                    float wall = S(24, 30, h) * (1 - S(48, 55, h)) * (1 - S(0.12f, 0.3f, s)) * S(0.3f, 0.5f, l);
                    float down = lb[((y + dy) % H) * W + x];
                    float tops = S(0, 0.08f, lb[i] - down) * S(0.25f, 0.5f, l) * below;
                    float v = Cl(0.85f * foliage * (0.55f + 0.6f * S(0.15f, 0.6f, l)) + 0.7f * earth + 0.25f * roof * S(0.12f, 0.3f, l) + 0.3f * tops);
                    v = v * (1 - 0.65f * wall) * (1 - Cl(skyl[i] * (1 - below)));
                    Sm[i] = v; } });
            Sm = G(Sm, W, H, 1.6f * k);
            Parallel.For(0, H, y => { float yy = y / (float)H; float below = S(hz - 0.02f, hz + 0.06f, yy);
                float band = S(hz - 0.25f, hz, yy) * (1 - S(hz, hz + 0.2f, yy)) * 0.10f;
                for (int x = 0, i = y * W; x < W; x++, i++) {
                    float shade = 0.42f + 0.78f * S(0.04f, 0.75f, tex[i]);
                    float sr = 0.86f * shade - 0.05f * (1 - shade), sg = 0.90f * shade - 0.02f * (1 - shade), sb = 0.97f * shade + 0.03f * (1 - shade);
                    float m = Sm[i];
                    float r = o.R[i] * (1 - m) + sr * m, g = o.G[i] * (1 - m) + sg * m, b = o.B[i] * (1 - m) + sb * m;
                    float sk = skyl[i] * (1 - below), gray = lum[i];
                    r = r * (1 - 0.55f * sk) + (gray * 0.84f + 0.04f) * 0.55f * sk;
                    g = g * (1 - 0.55f * sk) + (gray * 0.88f + 0.04f) * 0.55f * sk;
                    b = b * (1 - 0.55f * sk) + (gray * 0.94f + 0.04f) * 0.55f * sk;
                    float hb = band * (1 - S(0.5f, 0.85f, lum[i]));
                    o.R[i] = r * (1 - hb) + 0.74f * hb; o.G[i] = g * (1 - hb) + 0.78f * hb; o.B[i] = b * (1 - hb) + 0.85f * hb; } });
            // falling snow: many tiny flakes, fewer small ones, a few near ones
            var fl = new float[N];
            var rnd = new Random(seed * 7919 + 17);
            int n0 = (int)((x1 - x0) * (double)H / (9000 * k * k));
            int[] counts = { n0, n0 / 3, Math.Max(1, n0 / 25) };
            float[] alphas = { 0.35f, 0.45f, 0.5f };
            float[] sizes = { 0.6f, 1.2f, 2.4f };
            for (int layer = 0; layer < 3; layer++)
                for (int c = 0; c < counts[layer]; c++)
                {
                    int fx = x0 + rnd.Next(Math.Max(1, x1 - x0)), fy = rnd.Next(H);
                    int rr = Math.Max(0, (int)Math.Round(sizes[layer] * k * 0.5f));
                    for (int yy = -rr; yy <= rr; yy++) for (int xx = -rr; xx <= rr; xx++)
                    {
                        int px = fx + xx, py = fy + yy; if (px < 0 || py < 0 || px >= W || py >= H) continue;
                        int i = py * W + px; if (fl[i] < alphas[layer]) fl[i] = alphas[layer];
                    }
                }
            fl = G(fl, W, H, 1.0f * k);
            Parallel.For(0, N, i => { float f = Math.Min(0.75f, fl[i] * 2.2f); o.R[i] = o.R[i] * (1 - f) + 0.93f * f; o.G[i] = o.G[i] * (1 - f) + 0.95f * f; o.B[i] = o.B[i] * (1 - f) + 1.0f * f; });
        }
        // frost creeping in from the edges of the picture
        var fr = new float[N];
        float aspect = (x1 - x0) / (float)H;
        Parallel.For(0, H, y => { float yy = y / (float)H; for (int x = 0, i = y * W; x < W; x++, i++) {
            float bx = Cl((x - x0) / (float)Math.Max(1, x1 - x0));
            float ed = Math.Min(Math.Min(bx, 1 - bx) * aspect, Math.Min(yy, 1 - yy));
            float nz = Bc.Fbm(x / (float)W, yy, 16, seed + 101, 5) - 0.5f;
            float v = Cl(S(0.16f, 0.0f, ed) * 1.15f - 0.55f + nz * 0.9f) * S(0, 0.02f, ed + 0.005f);
            fr[i] = v; } });
        fr = G(fr, W, H, 0.8f * k);
        float frK = inside ? 0.55f : 0.4f;
        // keep the black bars black (soft edge)
        var bar = new float[N];
        Parallel.For(0, H, y => { for (int x = x0; x <= x1; x++) bar[y * W + x] = 1; });
        bar = G(bar, W, H, 12 * k);
        Parallel.For(0, N, i => {
            float f = fr[i] * frK;
            float r = o.R[i] * (1 - f) + 0.88f * f, g = o.G[i] * (1 - f) + 0.93f * f, b = o.B[i] * (1 - f) + 1.0f * f;
            float e = bar[i];
            o.R[i] = Cl(r * e + src.R[i] * (1 - e)); o.G[i] = Cl(g * e + src.G[i] * (1 - e)); o.B[i] = Cl(b * e + src.B[i] * (1 - e)); });
        return o;
    }

    // ------------------------------------------------------------------ DDS in/out
    static Img DecodeDds(byte[] d, out int fmt, out int hdr, out int mips)
    {
        fmt = -1; hdr = 128; mips = Math.Max(1, BitConverter.ToInt32(d, 28));
        int h = BitConverter.ToInt32(d, 12), w = BitConverter.ToInt32(d, 16);
        string cc = Encoding.ASCII.GetString(d, 84, 4);
        if (cc == "DX10")
        {
            hdr = 148; uint dx = BitConverter.ToUInt32(d, 128);
            if (dx == 98 || dx == 99) { fmt = 7; return Bc7.Decode(d, hdr, w, h); }
            if (dx >= 70 && dx <= 72) fmt = Bc.BC1; else if (dx >= 76 && dx <= 78) fmt = Bc.BC3; else return null;
        }
        else if (cc == "DXT1") fmt = Bc.BC1;
        else if (cc == "DXT5" || cc == "DXT4") fmt = Bc.BC3;
        else return null;
        return Bc.Decode(d, hdr, fmt, w, h);
    }

    static byte[] EncodeDds(byte[] orig, int hdr, int fmt, int mips, Img im)
    {
        var ms = new MemoryStream();
        ms.Write(orig, 0, hdr);
        var cur = im;
        for (int m = 0; m < mips; m++)
        {
            byte[] data = fmt == 7 ? Bc7.Encode(cur) : Bc.Encode(cur, fmt, false);
            ms.Write(data, 0, data.Length);
            if (m + 1 < mips) cur = Bc.Down(cur);
        }
        // anything after the mips (CryEngine attaches extra chunks) is kept as is
        long expect = hdr; var t = im;
        for (int m = 0; m < mips; m++) { expect += fmt == 7 ? (long)((t.W + 3) / 4) * ((t.H + 3) / 4) * 16 : Bc.MipBytes(fmt, t.W, t.H); if (m + 1 < mips) t = new Img(Math.Max(1, t.W / 2), Math.Max(1, t.H / 2)); }
        if (orig.Length > expect) ms.Write(orig, (int)expect, (int)(orig.Length - expect));
        return ms.ToArray();
    }

    static readonly Regex Art = new Regex("^libs/ui/textures/video/artwork(\\d+)\\.dds$", RegexOptions.IgnoreCase);

    // The artwork numbers inside the shipped winter paks.
    public static HashSet<int> ArtworksIn(IEnumerable<string> paks)
    {
        var res = new HashSet<int>();
        foreach (var p in paks)
            try
            {
                using (var z = ZipFile.OpenRead(p))
                    foreach (var e in z.Entries) { var m = Art.Match(e.FullName.Replace('\\', '/')); if (m.Success) res.Add(int.Parse(m.Groups[1].Value)); }
            }
            catch (Exception) { }
        return res;
    }

    // The game's mod order: Mods\mod_order.txt if there is one, else alphabetical.
    static List<string> ModOrder(string modsDir)
    {
        var dirs = new List<string>();
        foreach (var d in Directory.GetDirectories(modsDir)) dirs.Add(Path.GetFileName(d));
        var order = Path.Combine(modsDir, "mod_order.txt");
        if (File.Exists(order))
        {
            var res = new List<string>();
            foreach (var line in File.ReadAllLines(order))
            {
                var t = line.Trim();
                foreach (var d in dirs) if (d.Equals(t, StringComparison.OrdinalIgnoreCase) && !res.Contains(d)) res.Add(d);
            }
            return res;
        }
        dirs.Sort(delegate (string a, string b) { return string.CompareOrdinal(a.ToLowerInvariant(), b.ToLowerInvariant()); });
        return dirs;
    }

    // Other mods that would cover the winter screens: one with its own loading-screen
    // movie loading after harsh_winter (its movie wins: only the game's 18 screens
    // can show), or with artworks loading after harsh_winter_world (theirs win).
    public static void FindConflicts(string modsDir)
    {
        Conflicts.Clear();
        if (!Directory.Exists(modsDir)) return;
        var order = ModOrder(modsDir);
        // does mod i load after mod "target"? (by position; alphabetically when target isn't in the list)
        Func<int, string, bool> after = delegate (int i, string target)
        {
            int t = order.FindIndex(delegate (string d) { return d.Equals(target, StringComparison.OrdinalIgnoreCase); });
            if (t >= 0) return i > t;
            return string.CompareOrdinal(order[i].ToLowerInvariant(), target) > 0;
        };
        for (int i = 0; i < order.Count; i++)
        {
            var mn = order[i];
            if (mn.StartsWith("harsh_winter", StringComparison.OrdinalIgnoreCase)) continue;
            var data = Path.Combine(modsDir, mn, "Data");
            if (!Directory.Exists(data)) continue;
            bool movie = false; int arts = 0;
            foreach (var pak in Directory.GetFiles(data, "*.pak", SearchOption.TopDirectoryOnly))
                try
                {
                    using (var z = ZipFile.OpenRead(pak))
                        foreach (var e in z.Entries)
                        {
                            var n = e.FullName.Replace('\\', '/');
                            if (n.Equals("Libs/UI/LoadingScreen.gfx", StringComparison.OrdinalIgnoreCase)) movie = true;
                            else if (Art.IsMatch(n)) arts++;
                        }
                }
                catch (Exception) { }
            if (movie && after(i, "harsh_winter"))
                Conflicts.Add(mn + " has its own loading-screen movie (Libs/UI/LoadingScreen.gfx) and loads after harsh_winter, so its movie is used and the winter screens 19-37 never show. Remove it, or use Mods\\mod_order.txt with harsh_winter after it.");
            else if (movie)
                Conflicts.Add(mn + " has its own loading-screen movie but loads before harsh_winter - Harsh Winter's is used (you can remove " + mn + ").");
            if (arts > 0 && after(i, WinterTextures.ModId))
                Conflicts.Add(mn + " has " + arts + " loading-screen picture(s) and loads after harsh_winter_world, so its pictures cover the winter ones. Remove it, or put it before harsh_winter in Mods\\mod_order.txt.");
        }
    }

    // Artworks: the game's, then those of loading-screen mods in Mods\ (they win).
    public static int Build(Dictionary<string, WinterTextures.Entry> index, Func<string, byte[]> read, string modsDir, string outPakPath, bool list)
    {
        var log = WinterTextures.Log;
        var found = new SortedDictionary<int, KeyValuePair<string, Func<byte[]>>>();
        foreach (var kv in index)
        {
            var m = Art.Match(kv.Key);
            if (!m.Success) continue;
            string key = kv.Key;
            found[int.Parse(m.Groups[1].Value)] = new KeyValuePair<string, Func<byte[]>>("game", delegate { return read(key); });
        }
        foreach (var n in Skip) found.Remove(n);
        LastSources.Clear();
        // Only the game's own artworks: Harsh Winter ships its winter versions of
        // the extra loading screens itself (Data\harsh_winter_loading.pak).
        if (ScanMods && Directory.Exists(modsDir))
            foreach (var mod in Directory.GetDirectories(modsDir))
            {
                string mn = Path.GetFileName(mod);
                if (mn.StartsWith("harsh_winter", StringComparison.OrdinalIgnoreCase)) continue;
                var data = Path.Combine(mod, "Data");
                if (!Directory.Exists(data)) continue;
                foreach (var pak in Directory.GetFiles(data, "*.pak", SearchOption.AllDirectories))
                {
                    try
                    {
                        bool any = false;
                        using (var z = ZipFile.OpenRead(pak))
                            foreach (var e in z.Entries)
                            {
                                var m = Art.Match(e.FullName.Replace('\\', '/'));
                                if (!m.Success || Skip.Contains(int.Parse(m.Groups[1].Value))) continue;
                                string p = pak, en = e.FullName;
                                found[int.Parse(m.Groups[1].Value)] = new KeyValuePair<string, Func<byte[]>>(mn, delegate {
                                    using (var zz = ZipFile.OpenRead(p)) using (var s = zz.GetEntry(en).Open()) using (var o = new MemoryStream()) { s.CopyTo(o); return o.ToArray(); } });
                                any = true;
                            }
                        if (any) LastSources.Add(mn);
                    }
                    catch (Exception) { }
                }
            }
        log(string.Format("Loading screens: {0} artworks found{1}.", found.Count, LastSources.Count > 0 ? " (incl. " + string.Join(", ", LastSources.ToArray()) + ")" : ""));
        LastCount = 0;
        if (RequireMods && LastSources.Count == 0) { found.Clear(); log("  no loading-screen mods installed"); }
        if (list || found.Count == 0) return 0;
        using (var pak = new PakWriter(outPakPath + ".tmp"))
        {
            foreach (var kv in found)
            {
                byte[] d;
                try { d = kv.Value.Value(); } catch (Exception ex) { log("  artwork" + kv.Key + ": unreadable (" + ex.Message + ")"); continue; }
                int fmt = -1, hdr = 128, mips = 1;
                Img im = null;
                if (d.Length > 148) im = DecodeDds(d, out fmt, out hdr, out mips);
                if (im == null) { log("  artwork" + kv.Key + ": format not handled, original kept"); continue; }
                bool inside;
                var w = Winterise(im, kv.Key, out inside);
                pak.Add("Libs/UI/Textures/Video/artwork" + kv.Key + ".dds", EncodeDds(d, hdr, fmt, mips, w));
                LastCount++;
                log(string.Format("  artwork{0} ({1}, {2}x{3}, {4}): {5}", kv.Key, kv.Value.Key, im.W, im.H, fmt == 7 ? "BC7" : (fmt == Bc.BC1 ? "BC1" : "BC3"), inside ? "indoors - cold grade and frost" : "winter"));
            }
        }
        if (File.Exists(outPakPath)) File.Delete(outPakPath);
        File.Move(outPakPath + ".tmp", outPakPath);
        return 0;
    }
}
