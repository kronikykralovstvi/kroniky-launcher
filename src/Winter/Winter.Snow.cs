// =============================================================================
// Harsh Winter builder - the snow look, per pixel.
//
// Every texture is decoded, edited pixel by pixel with a small physical model
// of snow, given a fresh mip chain and encoded again:
//
//   * Snow fills hollows first; only raised detail pokes through, and only in
//     wind-scoured patches (large tiling noise). Shading of the snow surface
//     comes from the texture's own blurred relief, so the ground keeps gentle
//     undulations instead of the grass pattern, with cold blue shadows, a fine
//     grain and rare sparkling crystals.
//   * Roads become packed, dirty snow with slush in the ruts, roofs a thick
//     cover with thinner lines along shingle rows, rocks a dusting on their
//     upper faces, props and windows hoar frost.
//   * Trees are winter trees: broadleaf foliage of any colour (green, yellow,
//     orange, red) is cut out so the tree stands bare, and snow lies only along
//     the upper edge of every twig, branch and needle spray and in the forks -
//     never splashed at random. Conifers carry snow on each needle layer.
//   * A real photographed snow surface (helper/snow_detail.bin, made seamless
//     by tools/make_snow_detail.py) adds its grain, sparkle and soft wind
//     ripples to every snowed ground, road and roof, at about one detail texel
//     per texture texel - so close up it reads as snow, not as a white fill.
//   * Normal maps of snowed-under surfaces are flattened, because snow hides
//     the relief of the ground below it, and get the snow's own micro-surface
//     (from the detail's height) instead.
//   * Bark gets hoar frost only; wood (fences, planks, carts) gets patchy snow
//     settled in its grain with hoar frost elsewhere.
//   * Optionally the tiling surfaces (ground, roads, roofs, rocks) are rebuilt
//     at twice the resolution with the snow detail added at the new
//     resolution (crisper close up).
// =============================================================================
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class Snow
{
    // ------------------------------------------------------------------ photo detail
    // Detail mips (1024, 512, 256, 128): albedo (about -1..1) and micro-normal x/y.
    static float[][] DA, DNX, DNY;
    static int[] DSize;
    public static bool HasDetail { get { return DA != null; } }

    public static bool LoadDetail(string path)
    {
        if (!System.IO.File.Exists(path)) return false;
        var b = System.IO.File.ReadAllBytes(path);
        const int N = 1024;
        if (b.Length != N * N * 2) return false;
        var a = new float[N * N]; var h = new float[N * N];
        for (int i = 0; i < N * N; i++) { a[i] = (b[2 * i] - 128) / 120f; h[i] = b[2 * i + 1] / 255f; }
        DA = new float[4][]; DNX = new float[4][]; DNY = new float[4][]; DSize = new int[4];
        int n = N;
        for (int lv = 0; lv < 4; lv++)
        {
            DA[lv] = a; DSize[lv] = n;
            // Micro-normals from the height (wrapping), DirectX green convention.
            var nx = new float[n * n]; var ny = new float[n * n];
            float k = 2.2f * n / 1024f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float gx = h[y * n + (x + 1) % n] - h[y * n + (x + n - 1) % n];
                    float gy = h[((y + 1) % n) * n + x] - h[((y + n - 1) % n) * n + x];
                    nx[y * n + x] = -gx * k; ny[y * n + x] = -gy * k;
                }
            DNX[lv] = nx; DNY[lv] = ny;
            if (lv == 3) break;
            int m = n / 2; var a2 = new float[m * m]; var h2 = new float[m * m];
            for (int y = 0; y < m; y++)
                for (int x = 0; x < m; x++)
                {
                    int i0 = (2 * y) * n + 2 * x;
                    a2[y * m + x] = (a[i0] + a[i0 + 1] + a[i0 + n] + a[i0 + n + 1]) * 0.25f * 0.9f;   // fine sparkle fades with distance
                    h2[y * m + x] = (h[i0] + h[i0 + 1] + h[i0 + n] + h[i0 + n + 1]) * 0.25f;
                }
            a = a2; h = h2; n = m;
        }
        return true;
    }

    // Detail level for a texture w texels wide: one detail texel per texture texel
    // up to 1024; smaller textures read a smaller (averaged) detail level.
    static int DetailLevel(int w) { int lv = 0; int n = 1024; while (lv < 3 && w < n) { n /= 2; lv++; } return lv; }

    static float DetailA(int lv, int x, int y)
    {
        int n = DSize[lv];
        return DA[lv][((y % n) + n) % n * n + ((x % n) + n) % n];
    }

    public static void DetailN(int lv, int x, int y, out float nx, out float ny)
    {
        int n = DSize[lv];
        int i = ((y % n) + n) % n * n + ((x % n) + n) % n;
        nx = DNX[lv][i]; ny = DNY[lv][i];
    }

    static float Sm(float e0, float e1, float x) { float t = (x - e0) / (e1 - e0); t = t < 0 ? 0 : (t > 1 ? 1 : t); return t * t * (3 - 2 * t); }
    static float Cl(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
    static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
    static float Lum(float r, float g, float b) { return 0.299f * r + 0.587f * g + 0.114f * b; }

    // Snow colours (sRGB): lit snow, shadowed snow (blue sky light), packed road snow.
    static readonly float[] Hi = { 0.935f, 0.945f, 0.965f };
    static readonly float[] Sh = { 0.58f, 0.64f, 0.77f };
    static readonly float[] PackHi = { 0.90f, 0.905f, 0.92f };
    static readonly float[] PackSh = { 0.60f, 0.62f, 0.67f };
    static readonly float[] Frost = { 0.84f, 0.875f, 0.92f };
    static readonly float[][] Straw = { new[] { 0.31f, 0.26f, 0.17f }, new[] { 0.59f, 0.50f, 0.33f }, new[] { 0.77f, 0.70f, 0.52f } };
    static readonly float[][] BareCrown = { new[] { 0.24f, 0.21f, 0.20f }, new[] { 0.41f, 0.37f, 0.35f }, new[] { 0.56f, 0.52f, 0.49f } };

    public static bool IsVegetation(string g) { return g == "conifer" || g == "deciduous" || g == "marcescent" || g == "grass" || g == "billboard"; }
    public static bool IsTiling(string g) { return g == "terrain" || g == "road" || g == "roof" || g == "rock"; }

    // How leafy a texel is (0..1): saturated green or yellow-green; bark, wood and stone score 0.
    static float Leafy(float r, float g, float b)
    {
        float other = Math.Max(r, b);
        float gr = g > other ? (g - other) / Math.Max(0.08f, g * 0.3f) : 0;
        float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
        float sat = mx > 0 ? (mx - mn) / mx : 0;
        float yellow = (sat > 0.45f && g >= r * 0.8f && g > b * 1.6f && g > 0.24f) ? 1 : 0;
        return Math.Max(Sm(0.08f, 0.25f, gr), yellow);
    }

    // Any leaf colour (0..1): green (Leafy), but also the yellow, orange and red of
    // autumn foliage. Brown bark and grey wood score 0 - they are far less
    // saturated (low chroma) than a living or turning leaf.
    public static float LeafAny(float r, float g, float b)
    {
        float green = Leafy(r, g, b);
        float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
        float ch = mx - mn;
        if (ch <= 0.001f) return green;
        float hue;
        if (mx == r) hue = 60f * (((g - b) / ch) % 6f);
        else if (mx == g) hue = 60f * ((b - r) / ch + 2f);
        else hue = 60f * ((r - g) / ch + 4f);
        if (hue < 0) hue += 360f;
        float warm = 0;
        if (hue >= 14f && hue <= 70f) warm = Sm(0.13f, 0.24f, ch) * Sm(0.24f, 0.38f, mx);             // yellow, gold, orange
        else if (hue < 14f || hue >= 330f) warm = Sm(0.22f, 0.36f, ch) * Sm(0.24f, 0.40f, mx);      // red
        return Math.Max(green, warm);
    }

    // ------------------------------------------------------------------ trees
    // Trees are winter trees, never autumn ones:
    //   * broadleaf foliage (green, yellow, orange, red) is removed outright where
    //     the texture is alpha-tested, so the tree stands bare; where it can't be
    //     cut out (solid LOD textures) it turns bare grey-brown crown.
    //   * snow lies only where snow can lie: along the UPPER edge of every twig,
    //     branch and needle spray (from the open space above it), as thick as the
    //     twig allows, lumpy along its length - and in the forks, which are upper
    //     edges too. Conifers also carry snow on top of each needle layer inside a
    //     spray. Nothing is splashed at random over bark or crowns.
    //   * bark and twigs get a light hoar frost.
    // Texture "up" is taken as world up, as on the game's branch and billboard cards.
    public static int Trees(Img im, string group, int seed, float k, bool alpha, bool noNoise)
    {
        int w = im.W, h = im.H, n = w * h;
        bool con = group == "conifer", bill = group == "billboard";
        bool dec = !con && !bill;
        var L = new float[n]; var leaf = new float[n];
        for (int i = 0; i < n; i++) { L[i] = Lum(im.R[i], im.G[i], im.B[i]); leaf[i] = LeafAny(im.R[i], im.G[i], im.B[i]); }
        var clearedPerRow = new int[h];
        float qBill = Quantile(0.3f);
        // Twig / bark texels before any leaf is removed (opaque and not leaf-coloured).
        var twig = new bool[n]; var cut = new bool[n];
        if (dec && alpha) for (int i = 0; i < n; i++) twig[i] = im.A[i] >= 0.5f && leaf[i] <= 0.1f;
        // 1. Foliage and frost.
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float r = im.R[i], g = im.G[i], b = im.B[i], a = im.A[i], l = L[i], lf = leaf[i];
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                if (dec)
                {
                    if (alpha && lf > 0.35f && a > 0.02f) { im.A[i] = 0; cut[i] = true; clearedPerRow[y]++; continue; }
                    if (lf > 0)
                    {
                        // Leaf edges that stay (and solid LOD crowns): bare grey-brown twigs.
                        float pr, pg, pb; Pal(l, BareCrown, out pr, out pg, out pb);
                        float t = Cl(lf * (alpha ? 1.8f : 1.2f));
                        r = Lerp(r, pr, t); g = Lerp(g, pg, t); b = Lerp(b, pb, t);
                    }
                }
                else if (bill)
                {
                    bool needles = l < 0.22f && Leafy(r, g, b) > 0.2f;     // dark conifer crowns stay
                    if (!needles && lf > 0.2f)
                    {
                        if (alpha && (noNoise ? 0.5f : Bc.Fbm(u, v, 64, seed + 101, 2)) < qBill) { im.A[i] = 0; clearedPerRow[y]++; continue; }
                        float pr, pg, pb; Pal(l, BareCrown, out pr, out pg, out pb);
                        float t = Cl(lf * 1.5f);
                        r = Lerp(r, pr, t); g = Lerp(g, pg, t); b = Lerp(b, pb, t);
                    }
                }
                else if (lf > 0)
                {
                    // Conifer needles: a little darker and cooler under the winter sky; any
                    // warm (browning) needles go back to a dull winter green.
                    float warm = Cl(LeafAny(r, g, b) - Leafy(r, g, b));
                    r = Lerp(r, l * 0.62f, warm); g = Lerp(g, l * 0.95f, warm); b = Lerp(b, l * 0.66f, warm);
                    r *= 0.9f; g *= 0.93f; b = Math.Min(1, b * 0.97f + 0.01f);
                }
                // Hoar frost on what is left (bark, twigs, needles).
                float fr = (con ? 0.07f : 0.13f) * k;
                float br = 0.55f + 0.55f * l;
                im.R[i] = Lerp(r, Frost[0] * br, fr); im.G[i] = Lerp(g, Frost[1] * br, fr); im.B[i] = Lerp(b, Frost[2] * br, fr);
            }
        });
        int cleared = 0;
        foreach (var c in clearedPerRow) cleared += c;
        if (!alpha) return cleared;   // solid bark / trunk textures: no silhouette, frost only
        // Twigs that ran under a removed leaf are bridged again from both sides, so
        // branches don't end in a cut.
        if (dec && cleared > 0) { Bridge(im, twig, cut, Math.Max(4, w / 30)); Despeckle(im, 6); }

        // 2. Snow along upper edges.
        float frac = con ? 0.7f : (bill ? 0.45f : 0.55f);
        float maxBand = Math.Max(con ? 2.5f : 1.5f, h * (con ? 0.035f : (bill ? 0.02f : 0.012f)));
        float cover = Cl(k);
        Parallel.For(0, w, x =>
        {
            float u = (x + 0.5f) / w;
            int y = 0;
            while (y < h)
            {
                while (y < h && im.A[y * w + x] < 0.5f) y++;
                if (y >= h) break;
                int y0 = y;
                while (y < h && im.A[y * w + x] >= 0.5f) y++;
                int len = y - y0;
                bool open = y0 == 0 || (im.A[(y0 - 1) * w + x] < 0.5f && (y0 < 2 || im.A[(y0 - 2) * w + x] < 0.5f));
                if (open) Cap(im, L, w, x, y0, len, Math.Min(len * frac, maxBand), u, seed, noNoise, cover, len < 3 ? 0.6f : 1f);
                // Conifers: snow also lies on each needle layer inside the spray (a
                // bright layer right under a dark gap).
                if (con && len > 6)
                {
                    for (int yy = y0 + 3; yy < y0 + len - 2; yy++)
                    {
                        int i0 = yy * w + x;
                        if (leaf[i0] > 0.2f && L[i0] - L[(yy - 2) * w + x] > 0.09f && L[(yy - 1) * w + x] < L[i0])
                        {
                            int rest = y0 + len - yy;
                            Cap(im, L, w, x, yy, rest, Math.Min(rest * 0.5f, maxBand * 0.55f), u, seed + 17, noNoise, cover, 0.8f);
                            yy += (int)Math.Max(2, maxBand * 0.6f);
                        }
                    }
                }
            }
        });
        return cleared;
    }

    // Fills cleared texels lying between two twig texels (within r, along a row,
    // a column or a diagonal) with the average of the two, i.e. re-joins a twig a
    // leaf used to hide.
    static void Bridge(Img im, bool[] twig, bool[] cut, int r)
    {
        int w = im.W, h = im.H;
        var fill = new int[w * h]; for (int i = 0; i < fill.Length; i++) fill[i] = -1;
        int[] dx = { 1, 0, 1, 1 }, dy = { 0, 1, 1, -1 };
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!cut[i]) continue;                   // only texels a removed leaf covered
                for (int d = 0; d < 4; d++)
                {
                    int a = -1, b = -1, da = 0, db = 0;
                    // Walk both ways over removed-leaf texels only, until a twig texel.
                    for (int s = 1; s <= r && a < 0; s++) { int xx = x + dx[d] * s, yy = y + dy[d] * s; if (xx < 0 || yy < 0 || xx >= w || yy >= h) break; int j = yy * w + xx; if (twig[j]) { a = j; da = s; } else if (!cut[j]) break; }
                    if (a < 0) continue;
                    for (int s = 1; s <= r - da + 1 && b < 0; s++) { int xx = x - dx[d] * s, yy = y - dy[d] * s; if (xx < 0 || yy < 0 || xx >= w || yy >= h) break; int j = yy * w + xx; if (twig[j]) { b = j; db = s; } else if (!cut[j]) break; }
                    if (b < 0) continue;
                    fill[i] = da <= db ? a : b; break;
                }
            }
        });
        for (int i = 0; i < fill.Length; i++)
        {
            int j = fill[i];
            if (j < 0) continue;
            im.R[i] = im.R[j]; im.G[i] = im.G[j]; im.B[i] = im.B[j]; im.A[i] = 1;
        }
    }

    // Clears opaque specks smaller than minArea texels (crumbs of leaf edges left
    // floating once the leaves are gone).
    static void Despeckle(Img im, int minArea)
    {
        int w = im.W, h = im.H;
        var seen = new bool[w * h];
        var stack = new Stack<int>(); var comp = new List<int>();
        for (int s0 = 0; s0 < w * h; s0++)
        {
            if (seen[s0] || im.A[s0] < 0.5f) continue;
            comp.Clear(); stack.Push(s0); seen[s0] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop(); comp.Add(i);
                int x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int xx = x + (k == 0 ? 1 : (k == 1 ? -1 : 0)), yy = y + (k == 2 ? 1 : (k == 3 ? -1 : 0));
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    int j = yy * w + xx;
                    if (!seen[j] && im.A[j] >= 0.5f) { seen[j] = true; stack.Push(j); }
                }
                if (comp.Count > minArea) { while (stack.Count > 0) { int j = stack.Pop(); comp.Add(j); } break; }
            }
            if (comp.Count <= minArea) foreach (var i in comp) im.A[i] = 0;
        }
    }

    // Lays a cap of snow from row y0 down by `band` texels in column x (soft lower edge, lumpy).
    static void Cap(Img im, float[] L, int w, int x, int y0, int len, float band, float u, int seed, bool noNoise, float cover, float strength)
    {
        float lump = noNoise ? 1f : 0.6f + 0.8f * Bc.Fbm(u, (float)y0 / im.H, 18, seed + 31, 2);
        band *= lump;
        if (band < 0.6f) return;
        int end = Math.Min(y0 + len, y0 + (int)Math.Ceiling(band));
        for (int yy = y0; yy < end; yy++)
        {
            int i = yy * w + x;
            float t = (yy - y0 + 0.5f) / band;
            float s = (1 - Sm(0.5f, 1.0f, t)) * cover * strength;
            if (s <= 0) continue;
            float shade = Cl(0.95f - 0.28f * t + 0.18f * (L[i] - 0.3f));
            float sr = Lerp(Sh[0], Hi[0], shade), sg = Lerp(Sh[1], Hi[1], shade), sb = Lerp(Sh[2], Hi[2], shade);
            im.R[i] = Lerp(im.R[i], sr, s); im.G[i] = Lerp(im.G[i], sg, s); im.B[i] = Lerp(im.B[i], sb, s);
        }
    }

    static void Pal(float l, float[][] p, out float r, out float g, out float b)
    {
        float t = Math.Min(1f, l * 1.7f) * 2; int lo = Math.Min(1, (int)t); float f = t - lo;
        r = Lerp(p[lo][0], p[lo + 1][0], f); g = Lerp(p[lo][1], p[lo + 1][1], f); b = Lerp(p[lo][2], p[lo + 1][2], f);
    }

    static float[] fbmSorted;
    static float Quantile(float q)
    {
        if (fbmSorted == null)
        {
            var s = new List<float>();
            for (int seed = 1; seed <= 4; seed++)
                for (int y = 0; y < 96; y++)
                    for (int x = 0; x < 96; x++) s.Add(Bc.Fbm((x + 0.5f) / 96, (y + 0.5f) / 96, 16, seed * 7919, 2));
            s.Sort();
            fbmSorted = s.ToArray();
        }
        if (q <= 0) return -1; if (q >= 1) return 2;
        return fbmSorted[(int)Math.Round(q * (fbmSorted.Length - 1))];
    }

    // Edits an image in place. im is sRGB-encoded colour, alpha linear. k = strength (1 = default).
    // alpha: the texture really uses its alpha (alpha-tested leaves). Returns texels cleared.
    public static int Apply(Img im, string group, int seed, float k, bool alpha, bool noNoise)
    {
        if (group == "conifer" || group == "deciduous" || group == "marcescent" || group == "billboard")
            return Trees(im, group, seed, k, alpha, noNoise);
        int w = im.W, h = im.H, n = w * h;
        if (group == "snowdirt") return SnowDirt(im, k);
        var L = new float[n];
        for (int i = 0; i < n; i++) L[i] = Lum(im.R[i], im.G[i], im.B[i]);
        float[] LB = null, DS = null, LBp = null;
        if (IsTiling(group) || group == "cover" || group == "panorama")
        {
            LB = Bc.Blur(L, w, h, Math.Max(2, w / 48));
            DS = Bc.Blur(L, w, h, Math.Max(1, w / 256));
        }
        if (group == "props") LBp = Bc.Blur(L, w, h, Math.Max(2, w / 96));
        int cleared = 0;
        var clearedPerRow = new int[h];
        float qGrass = Quantile(0.68f);
        int dlv = HasDetail ? DetailLevel(w) : -1;
        bool useDetail = dlv >= 0 && !noNoise;
        Parallel.For(0, h, y =>
        {
            float v = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float u = (x + 0.5f) / w;
                float r = im.R[i], g = im.G[i], b = im.B[i], a = im.A[i], l = L[i];
                float n1 = noNoise ? 0.5f : Bc.Fbm(u, v, 4, seed, 3);
                float n2 = noNoise ? 0.5f : Bc.Fbm(u, v, 24, seed + 7, 2);
                float gr = noNoise ? 0.5f : Bc.Hash(x, y, seed + 13);
                float dd = useDetail ? DetailA(dlv, x, y) : 0;      // photo snow detail, about -1..1
                switch (group)
                {
                    case "terrain":
                    {
                        float d = l - LB[i], ds = DS[i] - LB[i];
                        float poke = Sm(0.10f, 0.28f, d) * Sm(0.42f, 0.22f, n1);
                        float cover = Cl(k * 1.02f - poke * 0.9f);
                        float shade = Cl(0.80f + 0.22f * (n2 - 0.5f) + 1.4f * ds + 0.12f * (n1 - 0.5f) + 0.16f * dd);
                        float sr = Lerp(Sh[0], Hi[0], shade), sg = Lerp(Sh[1], Hi[1], shade), sb = Lerp(Sh[2], Hi[2], shade);
                        float grain = 1 + (gr - 0.5f) * (useDetail ? 0.015f : 0.035f); sr *= grain; sg *= grain; sb *= grain;
                        if (gr > 0.9975f || dd > 0.95f) { sr += 0.05f; sg += 0.05f; sb += 0.055f; }
                        float under = 0.75f;
                        float ur = Lerp(r, l, 0.5f) * under, ug = Lerp(g, l, 0.5f) * under, ub = Lerp(b, l, 0.5f) * under;
                        im.R[i] = Lerp(ur, sr, cover); im.G[i] = Lerp(ug, sg, cover); im.B[i] = Lerp(ub, sb, cover);
                        break;
                    }
                    case "roof":
                    {
                        float d = l - LB[i], ds = DS[i] - LB[i];
                        float gap = Sm(-0.10f, -0.28f, d) * Sm(0.62f, 0.38f, n1);
                        float cover = Cl(k * 1.05f - gap * 0.55f);
                        float shade = Cl(0.86f + 0.16f * (n2 - 0.5f) + 1.1f * ds + 0.11f * dd);
                        float sr = Lerp(Sh[0], Hi[0], shade), sg = Lerp(Sh[1], Hi[1], shade), sb = Lerp(Sh[2], Hi[2], shade);
                        float grain = 1 + (gr - 0.5f) * (useDetail ? 0.015f : 0.03f); sr *= grain; sg *= grain; sb *= grain;
                        if (gr > 0.998f) { sr += 0.06f; sg += 0.06f; sb += 0.06f; }
                        im.R[i] = Lerp(r * 0.8f, sr, cover); im.G[i] = Lerp(g * 0.8f, sg, cover); im.B[i] = Lerp(b * 0.82f, sb, cover);
                        break;
                    }
                    case "road":
                    {
                        float ds = DS[i] - LB[i];
                        // Packed snow everywhere; brown slush only in the darkest, wettest ruts.
                        float cov = Cl(Math.Max(Sm(0.16f, 0.42f, LB[i] + (n1 - 0.5f) * 0.35f), 0.55f) * k);
                        float shade = Cl(0.70f + 0.22f * (n2 - 0.5f) + 1.2f * ds + 0.25f * (l - 0.4f) + 0.08f * dd);
                        float sr = Lerp(PackSh[0], PackHi[0], shade), sg = Lerp(PackSh[1], PackHi[1], shade), sb = Lerp(PackSh[2], PackHi[2], shade);
                        float dirt = Sm(0.6f, 0.82f, n2) * 0.28f;
                        sr = Lerp(sr, 0.60f, dirt); sg = Lerp(sg, 0.55f, dirt); sb = Lerp(sb, 0.48f, dirt);
                        float grain = 1 + (gr - 0.5f) * 0.03f; sr *= grain; sg *= grain; sb *= grain;
                        float slr = Lerp(r, l, 0.45f) * 0.7f + 0.05f, slg = Lerp(g, l, 0.45f) * 0.68f + 0.045f, slb = Lerp(b, l, 0.45f) * 0.66f + 0.045f;
                        im.R[i] = Lerp(slr, sr, cov); im.G[i] = Lerp(slg, sg, cov); im.B[i] = Lerp(slb, sb, cov);
                        break;
                    }
                    case "panorama":
                    case "cover":
                    {
                        // The far landscape: the horizon meshes' painted landscape and
                        // the terrain colour map the engine shows beyond the textured
                        // ground (cover.ctc - the farthest layer, where forests exist
                        // only as their dark colour). Open land (fields, meadows, bare
                        // hills) under deep snow; WOODS stay dark and dusted white, so
                        // the horizon reads as snowy forest, not one flat white sheet;
                        // the relief and texture are kept from the original light.
                        // woods are the DARK areas (fields and meadows are light, even when green)
                        float leafy = Leafy(r, g, b);
                        float wood = Cl(Sm(0.34f, 0.15f, LB[i]) * (0.75f + 0.25f * leafy));
                        float ds = DS[i] - LB[i];
                        // relief and texture: local contrast plus the area's own light
                        float shade = Cl(0.78f + 1.3f * ds + 0.45f * (l - LB[i]) + 0.35f * (LB[i] - 0.35f) + 0.10f * (n2 - 0.5f));
                        float sr = Lerp(Sh[0], Hi[0], shade), sg = Lerp(Sh[1], Hi[1], shade), sb = Lerp(Sh[2], Hi[2], shade);
                        // woods: the trees' own darkness, greyed and cooled, with snow in the crowns
                        // snowy conifer forest from afar: dark grey-green, cooled, the
                        // crowns' lighter texels carrying snow (keeps the trees' grain)
                        float wr0 = Lerp(r, l, 0.6f) * 0.9f, wg0 = Lerp(g, l, 0.6f) * 0.92f, wb0 = Lerp(b, l, 0.6f) * 1.0f;
                        float crown = Cl(0.22f + 2.2f * (l - LB[i]));
                        float dust = (0.18f + 0.30f * crown) * k;
                        float wr = Lerp(wr0, sr, dust), wg = Lerp(wg0, sg, dust), wb = Lerp(wb0, sb, dust);
                        float cover = Cl(k * 0.9f);
                        float or_ = Lerp(r, sr, cover), og = Lerp(g, sg, cover), ob = Lerp(b, sb, cover);
                        im.R[i] = Lerp(or_, wr, wood); im.G[i] = Lerp(og, wg, wood); im.B[i] = Lerp(ob, wb, wood);
                        break;
                    }
                    case "rock":
                    {
                        float cover = Sm(0.44f, 0.70f, l + (n2 - 0.5f) * 0.55f + (n1 - 0.5f) * 0.3f) * 0.78f * k;
                        float shade = Cl(0.82f + 0.3f * (l - 0.5f) + 0.06f * dd);
                        float sr = Lerp(Sh[0], Hi[0], shade), sg = Lerp(Sh[1], Hi[1], shade), sb = Lerp(Sh[2], Hi[2], shade);
                        im.R[i] = Lerp(r * 0.92f, sr, cover); im.G[i] = Lerp(g * 0.92f, sg, cover); im.B[i] = Lerp(b * 0.94f, sb, cover);
                        break;
                    }
                    case "props":
                    {
                        // Wood and old stone outdoors: snow settles in clumps and in the
                        // grain's grooves (darker texels), hoar frost on the rest.
                        float fine = noNoise ? 0.5f : Bc.Fbm(u, v, 64, seed + 3, 2);
                        float clump = noNoise ? 0.5f : Bc.Fbm(u, v, 12, seed + 9, 3);
                        float groove = Sm(0.02f, -0.10f, l - (LBp != null ? LBp[i] : l));
                        float snowy = Cl((Sm(0.56f, 0.72f, clump + groove * 0.25f) * 0.75f + groove * 0.25f) * k);
                        float f = (0.10f + Sm(0.62f, 0.86f, fine) * 0.12f) * k;
                        float br = 0.55f + 0.6f * l;
                        float fr = Lerp(r, Frost[0] * br, f), fg = Lerp(g, Frost[1] * br, f), fb = Lerp(b, Frost[2] * br, f);
                        float s2 = Cl(0.84f + 0.2f * (n2 - 0.5f) + 0.06f * dd);
                        float sr = Lerp(Sh[0], Hi[0], s2), sg = Lerp(Sh[1], Hi[1], s2), sb = Lerp(Sh[2], Hi[2], s2);
                        im.R[i] = Lerp(fr, sr, snowy); im.G[i] = Lerp(fg, sg, snowy); im.B[i] = Lerp(fb, sb, snowy);
                        break;
                    }
                    case "glass":
                    {
                        float fr = Sm(0.30f, 0.70f, noNoise ? 0.5f : Bc.Fbm(u, v, 6, seed, 4)) * 0.55f + 0.15f;
                        fr *= k;
                        im.R[i] = Lerp(r, 0.86f, fr); im.G[i] = Lerp(g, 0.90f, fr); im.B[i] = Lerp(b, 0.95f, fr);
                        im.A[i] = a + (1 - a) * fr * 0.6f;
                        break;
                    }
                    case "grass":
                    {
                        float leaf = Math.Max(Leafy(r, g, b), LeafAny(r, g, b));
                        if (leaf > 0.3f && alpha && a > 0.02f)
                        {
                            if (Bc.Fbm(u, v, 24, seed + 101, 2) < qGrass) { im.A[i] = 0; clearedPerRow[y]++; break; }
                        }
                        if (leaf > 0)
                        {
                            float pr, pg, pb;
                            Pal(l, Straw, out pr, out pg, out pb);
                            float fr = 0.35f * k * (0.6f + 0.8f * Sm(0.4f, 0.8f, n2));
                            pr = Lerp(pr, Frost[0], fr); pg = Lerp(pg, Frost[1], fr); pb = Lerp(pb, Frost[2], fr);
                            im.R[i] = Lerp(r, pr, leaf); im.G[i] = Lerp(g, pg, leaf); im.B[i] = Lerp(b, pb, leaf);
                        }
                        break;
                    }
                }
            }
        });
        foreach (var c in clearedPerRow) cleared += c;
        return cleared;
    }

    // The dirt/grime colour that the game lays over clothes as people get dirty,
    // turned into powder snow: the game's own dirt system (dirt builds up
    // outdoors, washes off) then reads as snow settling and melting. Only colour
    // maps are changed; a grey mask (low colour) is left alone - whitening a mask
    // would make everyone fully "dirty". Returns -1 when skipped.
    static int SnowDirt(Img im, float k)
    {
        int n = im.W * im.H;
        double sat = 0;
        for (int i = 0; i < n; i += 7)
        {
            float mx = Math.Max(im.R[i], Math.Max(im.G[i], im.B[i])), mn = Math.Min(im.R[i], Math.Min(im.G[i], im.B[i]));
            sat += mx > 0.02f ? (mx - mn) / mx : 0;
        }
        sat /= Math.Max(1, (n + 6) / 7);
        if (sat < 0.07) return -1;
        for (int i = 0; i < n; i++)
        {
            float l = Lum(im.R[i], im.G[i], im.B[i]);
            float shade = Cl(0.78f + 0.35f * (l - 0.35f));
            float sr = Lerp(Sh[0], Hi[0], shade), sg = Lerp(Sh[1], Hi[1], shade), sb = Lerp(Sh[2], Hi[2], shade);
            float f = Cl(0.92f * k);
            im.R[i] = Lerp(im.R[i], sr, f); im.G[i] = Lerp(im.G[i], sg, f); im.B[i] = Lerp(im.B[i], sb, f);
        }
        return 0;
    }

    // Mip chain from a processed top level. Colour is filtered in linear light
    // for sRGB textures; alpha-tested foliage keeps its coverage at every level
    // so trees don't thin out with distance.
    public static List<Img> Chain(Img top, int count, bool srgb, bool keepCoverage)
    {
        var lin = top.Clone();
        if (srgb) Bc.ToLinear(lin);
        float cov0 = keepCoverage ? Coverage(top.A, 1f) : 0;
        var outList = new List<Img>();
        var cur = lin;
        for (int m = 0; m < count; m++)
        {
            if (m > 0)
            {
                cur = Bc.Down(cur);
                if (keepCoverage && cov0 > 0) FixCoverage(cur, cov0);
            }
            var e = cur.Clone();
            if (srgb) Bc.ToSrgb(e);
            outList.Add(e);
        }
        return outList;
    }

    static float Coverage(float[] a, float scale)
    {
        int c = 0;
        for (int i = 0; i < a.Length; i++) if (a[i] * scale >= 0.5f) c++;
        return (float)c / a.Length;
    }

    static void FixCoverage(Img im, float target)
    {
        float lo = 0.5f, hi = 4f;
        for (int it = 0; it < 12; it++)
        {
            float mid = (lo + hi) / 2;
            if (Coverage(im.A, mid) < target) lo = mid; else hi = mid;
        }
        float s = (lo + hi) / 2;
        for (int i = 0; i < im.A.Length; i++) im.A[i] = Math.Min(1f, im.A[i] * s);
    }

    // ------------------------------------------------------------------ normal maps
    // Flattens a BC5 normal map block toward "straight up" by amount (0..1),
    // working on the two BC4 halves directly. signedFmt: BC5_SNORM.
    public static void FlattenBc5(byte[] buf, long start, long end, float amount, bool signedFmt)
    {
        for (long p = start; p + 16 <= end; p += 16)
        {
            FlattenBc4(buf, p, amount, signedFmt);
            FlattenBc4(buf, p + 8, amount, signedFmt);
        }
    }

    static void FlattenBc4(byte[] b, long p, float amount, bool sgn)
    {
        int e0 = sgn ? (int)(sbyte)b[p] : (int)b[p], e1 = sgn ? (int)(sbyte)b[p + 1] : (int)b[p + 1];
        int neutral = sgn ? 0 : 128;
        bool eight = e0 > e1;
        if (!eight)
        {
            // 6-value mode keeps extreme codes 6/7: point them at the endpoints instead.
            ulong bits = 0; for (int i = 0; i < 6; i++) bits |= (ulong)b[p + 2 + i] << (8 * i);
            ulong nb = 0;
            for (int k = 0; k < 16; k++)
            {
                ulong c = (bits >> (3 * k)) & 7;
                if (c == 6) c = 0; else if (c == 7) c = 1;
                nb |= c << (3 * k);
            }
            for (int i = 0; i < 6; i++) b[p + 2 + i] = (byte)(nb >> (8 * i));
        }
        int n0 = (int)Math.Round(neutral + (e0 - neutral) * (1 - amount)), n1 = (int)Math.Round(neutral + (e1 - neutral) * (1 - amount));
        if (eight && n0 <= n1) { if (n0 == n1) { for (int i = 2; i < 8; i++) b[p + i] = 0; } else { int t = n0; n0 = n1; n1 = t; } }
        if (!eight && n0 > n1) { int t = n0; n0 = n1; n1 = t; }
        b[p] = unchecked((byte)n0); b[p + 1] = unchecked((byte)n1);   // two's complement for SNORM
    }

    // ------------------------------------------------------------------ snow micro-normals
    // BC5 mip (w x h texels at off): flatten toward up by amount and add the photo
    // snow's own micro-surface (strength), re-encoding each BC4 half.
    static void DecodeBc4(byte[] b, long p, bool sgn, float[] o)
    {
        float e0 = sgn ? (sbyte)b[p] / 127f : b[p] / 255f, e1 = sgn ? (sbyte)b[p + 1] / 127f : b[p + 1] / 255f;
        if (sgn) { e0 = Math.Max(-1, e0); e1 = Math.Max(-1, e1); }
        var pal = new float[8];
        pal[0] = e0; pal[1] = e1;
        int r0 = sgn ? (int)(sbyte)b[p] : (int)b[p], r1 = sgn ? (int)(sbyte)b[p + 1] : (int)b[p + 1];
        if (r0 > r1) for (int k = 1; k < 7; k++) pal[k + 1] = ((7 - k) * e0 + k * e1) / 7f;
        else { for (int k = 1; k < 5; k++) pal[k + 1] = ((5 - k) * e0 + k * e1) / 5f; pal[6] = sgn ? -1 : 0; pal[7] = 1; }
        ulong bits = 0; for (int i = 0; i < 6; i++) bits |= (ulong)b[p + 2 + i] << (8 * i);
        for (int t = 0; t < 16; t++) o[t] = pal[(int)((bits >> (3 * t)) & 7)];
    }

    static void EncodeBc4(float[] v, byte[] b, long p, bool sgn)
    {
        float lo = 9, hi = -9;
        for (int t = 0; t < 16; t++) { if (v[t] < lo) lo = v[t]; if (v[t] > hi) hi = v[t]; }
        int q0 = sgn ? (int)Math.Round(Math.Max(-1, Math.Min(1, hi)) * 127) : (int)Math.Round(Cl(hi) * 255);
        int q1 = sgn ? (int)Math.Round(Math.Max(-1, Math.Min(1, lo)) * 127) : (int)Math.Round(Cl(lo) * 255);
        if (q0 == q1) { if (sgn ? q0 > -127 : q0 > 0) q1 = q0 - 1; else q0 = q1 + 1; }   // keep the 8-value mode
        float e0 = sgn ? q0 / 127f : q0 / 255f, e1 = sgn ? q1 / 127f : q1 / 255f;
        var pal = new float[8]; pal[0] = e0; pal[1] = e1;
        for (int k = 1; k < 7; k++) pal[k + 1] = ((7 - k) * e0 + k * e1) / 7f;
        ulong bits = 0;
        for (int t = 0; t < 16; t++)
        {
            int best = 0; float bd = 99;
            for (int c = 0; c < 8; c++) { float d = Math.Abs(pal[c] - v[t]); if (d < bd) { bd = d; best = c; } }
            bits |= (ulong)best << (3 * t);
        }
        b[p] = unchecked((byte)q0); b[p + 1] = unchecked((byte)q1);
        for (int i = 0; i < 6; i++) b[p + 2 + i] = (byte)(bits >> (8 * i));
    }

    public static void SnowNormalsBc5(byte[] buf, long off, int w, int h, float amount, float strength, bool sgn)
    {
        int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
        int lv = HasDetail ? DetailLevel(w) : -1;
        Parallel.For(0, bh, by =>
        {
            var xs = new float[16]; var ys = new float[16];
            for (int bx = 0; bx < bw; bx++)
            {
                long p = off + ((long)by * bw + bx) * 16;
                DecodeBc4(buf, p, sgn, xs); DecodeBc4(buf, p + 8, sgn, ys);
                for (int t = 0; t < 16; t++)
                {
                    float x = sgn ? xs[t] : xs[t] * 2 - 1, y = sgn ? ys[t] : ys[t] * 2 - 1;
                    x *= 1 - amount; y *= 1 - amount;
                    if (lv >= 0)
                    {
                        float dx, dy; DetailN(lv, bx * 4 + (t & 3), by * 4 + (t >> 2), out dx, out dy);
                        x += dx * strength; y += dy * strength;
                    }
                    float l = (float)Math.Sqrt(x * x + y * y);
                    if (l > 0.95f) { x *= 0.95f / l; y *= 0.95f / l; }
                    xs[t] = sgn ? x : x * 0.5f + 0.5f; ys[t] = sgn ? y : y * 0.5f + 0.5f;
                }
                EncodeBc4(xs, buf, p, sgn); EncodeBc4(ys, buf, p + 8, sgn);
            }
        });
    }

    // Flattens an RGB normal map image (x,y in R,G around 0.5, z in B) and adds
    // the snow micro-surface.
    public static void FlattenRgb(Img im, float amount) { FlattenRgb(im, amount, 0); }
    public static void FlattenRgb(Img im, float amount, float strength)
    {
        int lv = HasDetail && strength > 0 ? DetailLevel(im.W) : -1;
        for (int i = 0; i < im.R.Length; i++)
        {
            float x = (im.R[i] * 2 - 1) * (1 - amount), y = (im.G[i] * 2 - 1) * (1 - amount);
            if (lv >= 0) { float dx, dy; DetailN(lv, i % im.W, i / im.W, out dx, out dy); x += dx * strength; y += dy * strength; }
            float z = (float)Math.Sqrt(Math.Max(0, 1 - x * x - y * y));
            im.R[i] = x * 0.5f + 0.5f; im.G[i] = y * 0.5f + 0.5f; im.B[i] = z * 0.5f + 0.5f;
        }
    }
}
