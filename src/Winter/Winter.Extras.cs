// =============================================================================
// Harsh Winter builder - checks and small extras, written to
// Data\harsh_winter_world_extras.pak and Mods\harsh_winter_world\WinterEngine.cfg:
//
//  * Snow shaders: is CryEngine's deferred snow (DeferredSnow / "Snow"
//    technique) in your game's precompiled shader cache? Without it the
//    WinterEngine add-on's snow layer can't be drawn. Reported in
//    build_report.txt and WinterEngine.cfg (snow_shaders=found|missing).
//  * The death screen: the builder opens the game's game-over UI, lists the
//    pictures it uses and looks for the one of the explosion deaths (type 8,
//    used only by four rare gunpowder quest failures). If found, it is replaced
//    by the frozen skeletons (resized to the original, same format) and Harsh
//    Winter's three cold deaths are given that type - so dying of the cold
//    shows the frozen art and every other common death keeps its own. If that
//    picture can't be identified, nothing is replaced and the candidates are
//    listed for you (the add-on's DEATH_ART setting can still pick one).
// C# 5 only (Windows PowerShell 5.1 compiles it).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

public static class WinterExtras
{
    public static string ShaderState = "unknown";
    public static List<string> ShaderHits = new List<string>();
    public static List<string> DeathImages = new List<string>();
    public static string DeathArt = null;
    public static int DeathType = 0;
    public static bool LastUpgraded = false;
    public static string DeathArtInfo = "";

    static readonly string[][] TypeWords = {
        new[] { "8", "explos", "gunpowder", "blast", "powder" },
        new[] { "5", "fall" },
        new[] { "3", "poison" },
        new[] { "2", "blood", "bleed" },
        new[] { "4", "combat", "battle", "fight" },
        new[] { "6", "instant", "execut" },
    };

    // Uncompressed "CFX" (zlib) or plain "FWS"/"GFX" movie bytes.
    static byte[] Inflate(byte[] d)
    {
        if (d.Length > 8 && (d[0] == 'C') && (d[1] == 'F' || d[1] == 'W') && d[2] == 'X' || d.Length > 8 && d[0] == 'C' && d[1] == 'W' && d[2] == 'S')
        {
            try
            {
                using (var ms = new MemoryStream(d, 10, d.Length - 10))      // skip 8-byte header + 2-byte zlib header
                using (var z = new DeflateStream(ms, CompressionMode.Decompress))
                using (var o = new MemoryStream()) { z.CopyTo(o); return o.ToArray(); }
            }
            catch (Exception) { return d; }
        }
        return d;
    }

    static List<string> ImageRefs(byte[] movie)
    {
        var u = Inflate(movie);
        var s = Encoding.ASCII.GetString(u);
        var res = new List<string>();
        foreach (Match m in Regex.Matches(s, "(img://)?[A-Za-z0-9_/\\\\.-]{3,}\\.(dds|png|tif|tga|jpg)", RegexOptions.IgnoreCase))
        {
            var v = m.Value.Replace('\\', '/');
            if (v.StartsWith("img://", StringComparison.OrdinalIgnoreCase)) v = v.Substring(6);
            if (!res.Contains(v)) res.Add(v);
        }
        return res;
    }

    public static void Run(Dictionary<string, WinterTextures.Entry> index, Func<string, byte[]> read, string helperDir, string outDir, string dataDir, bool list)
    {
        var log = WinterTextures.Log;
        // ---- snow shaders in the shader cache
        ShaderHits.Clear();
        bool sawCache = false;
        foreach (var k in index.Keys)
        {
            bool shaderish = k.Contains("shadercache") || k.StartsWith("shaders/cache") || k.Contains("/cgpshaders/") || k.Contains("/cgvshaders/") || k.Contains("shaderlist");
            if (!shaderish) continue;
            sawCache = true;
            if (k.Contains("deferredsnow") || k.Contains("snow@") || k.Contains("/snow")) ShaderHits.Add(k);
        }
        ShaderState = ShaderHits.Count > 0 ? "found" : (sawCache ? "missing" : "unknown");
        log("Snow shaders in the game's shader cache: " + ShaderState + (ShaderHits.Count > 0 ? " (" + ShaderHits.Count + " entries)" : ""));

        // ---- the death screen
        DeathImages.Clear(); DeathArt = null; DeathType = 0;
        foreach (var k in index.Keys)
        {
            if (!k.StartsWith("libs/ui/")) continue;
            bool name = k.Contains("gameover") || k.Contains("game_over") || k.Contains("death") || k.Contains("skelet") || k.Contains("danse") || k.Contains("macabre");
            if (k.EndsWith(".gfx") && name)
            {
                try { foreach (var r in ImageRefs(read(k))) { var rl = r.ToLowerInvariant(); if (!rl.StartsWith("libs/")) rl = "libs/ui/" + rl.TrimStart('/'); if (!DeathImages.Contains(rl)) DeathImages.Add(rl); } }
                catch (Exception) { }
            }
            else if (name && (k.EndsWith(".dds") || k.EndsWith(".tif") || k.EndsWith(".png"))) { if (!DeathImages.Contains(k)) DeathImages.Add(k); }
        }
        // KCD2 names the death pictures by death type: libs/ui/textures/gameover/game_over_<type>_ui.dds.
        // Type 8 (explosion) is used by four rare gunpowder quest failures only.
        foreach (var img in DeathImages)
        {
            var mt = Regex.Match(img, "game_?over_(\\d+)_ui\\.dds$");
            if (mt.Success && mt.Groups[1].Value == "8" && index.ContainsKey(img) && !index.ContainsKey(img + ".1")) { DeathArt = img; DeathType = 8; break; }
        }
        if (DeathArt == null)
        foreach (var img in DeathImages)
        {
            foreach (var tw in TypeWords)
            {
                if (tw[0] != "8") continue;
                for (int i = 1; i < tw.Length; i++)
                    if (img.Contains(tw[i]) && index.ContainsKey(img) && !index.ContainsKey(img + ".1") && img.EndsWith(".dds")) { DeathArt = img; DeathType = 8; break; }
                if (DeathArt != null) break;
            }
            if (DeathArt != null) break;
        }
        log("Death screen: " + DeathImages.Count + " picture(s) found" + (DeathArt != null ? ", the explosion death's art -> frozen skeletons (" + DeathArt + ")" : ", none clearly for one kind of death only - the game's art stays (see build_report.txt)"));

        if (list) { WinterPerkIcons.Run(index, read, helperDir, null); return; }
        // ---- extras pak
        var pakPath = Path.Combine(dataDir, WinterTextures.ModId + "_extras.pak");
        using (var pak = new PakWriter(pakPath + ".tmp"))
        {
            // cold deaths get the type whose art is the frozen one (0 = the plain death screen)
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"us-ascii\"?>\r\n<!-- Harsh Winter (generated): the cold deaths, with the death-screen type the builder chose. -->\r\n");
            sb.Append("<database xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" name=\"barbora\" xsi:noNamespaceSchemaLocation=\"../database.xsd\">\r\n\t<game_overs version=\"1\">\r\n");
            sb.Append("\t\t<game_over game_over_id=\"201\" game_over_name=\"harsh_winter_froze\" game_over_type_id=\"" + DeathType + "\" game_over_ui_message=\"game_over_harsh_winter_cold\" />\r\n");
            sb.Append("\t\t<game_over game_over_id=\"202\" game_over_name=\"harsh_winter_frostbite\" game_over_type_id=\"" + DeathType + "\" game_over_ui_message=\"game_over_harsh_winter_hands\" />\r\n");
            sb.Append("\t\t<game_over game_over_id=\"203\" game_over_name=\"harsh_winter_blizzard\" game_over_type_id=\"" + DeathType + "\" game_over_ui_message=\"game_over_harsh_winter_blizzard\" />\r\n");
            sb.Append("\t</game_overs>\r\n</database>\r\n");
            pak.AddText("Libs/Tables/rpg/game_over__" + WinterTextures.ModId + ".xml", sb.ToString());
            // winter perk icons the game doesn't have -> its own
            try { WinterPerkIcons.Run(index, read, helperDir, pak); }
            catch (Exception ex) { WinterPerkIcons.State = "not checked: " + ex.Message; log("Winter perk icons: " + WinterPerkIcons.State); }
            if (DeathArt != null)
            {
                var icy = Path.Combine(helperDir, "KCD2ModLoader", "HarshWinter-WinterEngine", "icy_death_screen.dds");
                if (!File.Exists(icy)) icy = Path.Combine(helperDir, "..", "..", "assets", "death_screen", "harsh_winter_skeletons_ice.dds");   // source tree
                try
                {
                    var orig = read(DeathArt);
                    var oh = WinterTextures.ParseHeader(orig);
                    var dds = Frozen(File.ReadAllBytes(icy), orig);
                    if (oh != null)
                    {
                        DeathArtInfo = string.Format("the game's picture is {0}x{1} ({2}, {3} mips); the frozen skeletons fill it, edges fading into the game's black, written as BC7", oh.W, oh.H, oh.FormatName, oh.Mips);
                        log("  " + DeathArtInfo);
                    }
                    if (dds != null) pak.Add(index[DeathArt].Raw, dds);
                    else { log("  (the original death art's format isn't handled - kept)"); DeathArt = null; }
                }
                catch (Exception ex) { log("  frozen death art not written: " + ex.Message); DeathArt = null; }
            }
        }
        if (File.Exists(pakPath)) File.Delete(pakPath);
        File.Move(pakPath + ".tmp", pakPath);
        // ---- for the WinterEngine add-on
        var cfg = new StringBuilder();
        cfg.Append("# Written by Build winter.bat for the WinterEngine add-on. Rebuilt every run.\r\n");
        cfg.Append("snow_shaders=" + ShaderState + "\r\n");
        cfg.Append("death_type=" + DeathType + "\r\n");
        // the art is already in the extras pak; the add-on only needs a path when the builder didn't replace it
        cfg.Append("death_art=\r\n");
        File.WriteAllText(Path.Combine(outDir, "WinterEngine.cfg"), cfg.ToString(), Encoding.ASCII);
    }

    // The frozen skeletons (uncompressed BGRA DDS) resized to the original
    // picture and encoded in its format.
    static byte[] Frozen(byte[] icy, byte[] orig)
    {
        if (icy.Length < 128 + 16 || orig.Length < 128) return null;
        int ih = BitConverter.ToInt32(icy, 12), iw = BitConverter.ToInt32(icy, 16);
        var src = new Img(iw, ih);
        for (int i = 0; i < iw * ih; i++) { int o = 128 + i * 4; src.B[i] = icy[o] / 255f; src.G[i] = icy[o + 1] / 255f; src.R[i] = icy[o + 2] / 255f; src.A[i] = icy[o + 3] / 255f; }
        var hd = WinterTextures.ParseHeader(orig);
        if (hd == null) return null;
        // Uncompressed 32-bit picture (no FourCC): write the pixels directly.
        if (BitConverter.ToUInt32(orig, 84) == 0 && BitConverter.ToInt32(orig, 88) == 32)
        {
            uint rm = BitConverter.ToUInt32(orig, 92), gm = BitConverter.ToUInt32(orig, 96), bm = BitConverter.ToUInt32(orig, 100), am = BitConverter.ToUInt32(orig, 104);
            var raw = new MemoryStream();
            raw.Write(orig, 0, 128);
            var c0 = Fit(src, hd.W, hd.H);
            long exp = 128;
            for (int m = 0; m < hd.Mips; m++)
            {
                var px = new byte[c0.W * c0.H * 4];
                for (int i = 0; i < c0.W * c0.H; i++)
                {
                    uint v = Pack(c0.R[i], rm) | Pack(c0.G[i], gm) | Pack(c0.B[i], bm) | Pack(c0.A[i], am);
                    px[i * 4] = (byte)v; px[i * 4 + 1] = (byte)(v >> 8); px[i * 4 + 2] = (byte)(v >> 16); px[i * 4 + 3] = (byte)(v >> 24);
                }
                raw.Write(px, 0, px.Length); exp += px.Length;
                if (m + 1 < hd.Mips) c0 = Bc.Down(c0);
            }
            if (orig.Length > exp) raw.Write(orig, (int)exp, (int)(orig.Length - exp));
            return raw.ToArray();
        }
        bool bc7 = hd.FormatName == "DXGI 98" || hd.FormatName == "DXGI 99";
        if (hd.Fmt == Bc.OTHER && !bc7) return null;
        var im = Fit(src, hd.W, hd.H);
        var ms = new MemoryStream();
        if (bc7) ms.Write(orig, 0, hd.Hdr);
        else
        {
            // Highest quality: BC1/BC3 band the dark gradients and the frost
            // glitter, so the picture is written as BC7 at the same size (a DX10
            // header in place of the old one; the CExt trailer is kept).
            var h = new byte[148];
            Array.Copy(orig, 0, h, 0, 128);
            Array.Copy(BitConverter.GetBytes(0x4u), 0, h, 80, 4);                       // pixel format: FourCC
            Array.Copy(Encoding.ASCII.GetBytes("DX10"), 0, h, 84, 4);
            Array.Copy(BitConverter.GetBytes((uint)(((hd.W + 3) / 4) * ((hd.H + 3) / 4) * 16)), 0, h, 20, 4);
            Array.Copy(BitConverter.GetBytes(hd.Srgb ? 99u : 98u), 0, h, 128, 4);       // BC7 (sRGB like the original)
            Array.Copy(BitConverter.GetBytes(3u), 0, h, 132, 4);                         // 2D texture
            Array.Copy(BitConverter.GetBytes(1u), 0, h, 140, 4);                         // array size 1
            ms.Write(h, 0, 148);
            LastUpgraded = true;
        }
        var cur = im;
        long expect = hd.Hdr;
        for (int m = 0; m < hd.Mips; m++)
        {
            var data = Bc7.Encode(cur);
            ms.Write(data, 0, data.Length);
            expect += bc7 ? (long)((cur.W + 3) / 4) * ((cur.H + 3) / 4) * 16 : Bc.MipBytes(hd.Fmt, cur.W, cur.H);
            if (m + 1 < hd.Mips) cur = Bc.Down(cur);
        }
        if (orig.Length > expect) ms.Write(orig, (int)expect, (int)(orig.Length - expect));   // CryEngine's CExt trailer
        return ms.ToArray();
    }

    static uint Pack(float v, uint mask)
    {
        if (mask == 0) return 0;
        int sh = 0; while (((mask >> sh) & 1) == 0) sh++;
        uint max = mask >> sh;
        return ((uint)Math.Round(Math.Max(0, Math.Min(1, v)) * max) << sh) & mask;
    }

    // The whole picture, as large as fits, centred; the rest in the colour of
    // its edges (so a square picture on a wide screen isn't stretched or cut).
    static Img Fit(Img s, int w, int h)
    {
        // shrinking: halve with box filtering first (no aliasing in the fine frost)
        while (s.W / 2 >= w && s.H / 2 >= h) s = Bc.Down(s);
        float sc = Math.Min((float)w / s.W, (float)h / s.H);
        int fw = Math.Max(1, (int)Math.Round(s.W * sc)), fh = Math.Max(1, (int)Math.Round(s.H * sc));
        var inner = Resize(s, fw, fh);
        double r = 0, g = 0, b = 0, a = 0; int n = 0;
        for (int x = 0; x < s.W; x++) foreach (int y in new[] { 0, s.H - 1 }) { int i = y * s.W + x; r += s.R[i]; g += s.G[i]; b += s.B[i]; a += s.A[i]; n++; }
        for (int y = 0; y < s.H; y++) foreach (int x in new[] { 0, s.W - 1 }) { int i = y * s.W + x; r += s.R[i]; g += s.G[i]; b += s.B[i]; a += s.A[i]; n++; }
        var d = new Img(w, h);
        int ox = (w - fw) / 2, oy = (h - fh) / 2;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, ix = x - ox, iy = y - oy;
                if (ix >= 0 && iy >= 0 && ix < fw && iy < fh) { int j = iy * fw + ix; d.R[i] = inner.R[j]; d.G[i] = inner.G[j]; d.B[i] = inner.B[j]; d.A[i] = inner.A[j]; }
                else { d.R[i] = 0; d.G[i] = 0; d.B[i] = 0; d.A[i] = 1; }   // the game's own black around the picture
            }
        return d;
    }

    static Img Resize(Img s, int w, int h)
    {
        var d = new Img(w, h);
        for (int y = 0; y < h; y++)
        {
            float fy = (y + 0.5f) * s.H / h - 0.5f; int y0 = Math.Max(0, Math.Min(s.H - 1, (int)Math.Floor(fy))), y1 = Math.Min(s.H - 1, y0 + 1); float ty = Math.Max(0, Math.Min(1, fy - y0));
            for (int x = 0; x < w; x++)
            {
                float fx = (x + 0.5f) * s.W / w - 0.5f; int x0 = Math.Max(0, Math.Min(s.W - 1, (int)Math.Floor(fx))), x1 = Math.Min(s.W - 1, x0 + 1); float tx = Math.Max(0, Math.Min(1, fx - x0));
                int a = y0 * s.W + x0, b = y0 * s.W + x1, c = y1 * s.W + x0, e = y1 * s.W + x1, i = y * w + x;
                d.R[i] = (s.R[a] * (1 - tx) + s.R[b] * tx) * (1 - ty) + (s.R[c] * (1 - tx) + s.R[e] * tx) * ty;
                d.G[i] = (s.G[a] * (1 - tx) + s.G[b] * tx) * (1 - ty) + (s.G[c] * (1 - tx) + s.G[e] * tx) * ty;
                d.B[i] = (s.B[a] * (1 - tx) + s.B[b] * tx) * (1 - ty) + (s.B[c] * (1 - tx) + s.B[e] * tx) * ty;
                d.A[i] = (s.A[a] * (1 - tx) + s.A[b] * tx) * (1 - ty) + (s.A[c] * (1 - tx) + s.A[e] * tx) * ty;
            }
        }
        return d;
    }

    public static void Report(StringBuilder sb)
    {
        sb.AppendLine("== Snow shaders (WinterEngine add-on's snow layer) ==");
        sb.AppendLine("state: " + ShaderState);
        foreach (var h in ShaderHits) sb.AppendLine(h);
        if (ShaderState == "missing") sb.AppendLine("The deferred snow shaders are not in the shader cache: the add-on's snow layer can't be drawn. See helper\\KCD2ModLoader\\README.md, 'Snow shaders'.");
        sb.AppendLine();
        sb.AppendLine("== Death screen pictures (game-over UI) ==");
        foreach (var d in DeathImages) sb.AppendLine(d + (d == DeathArt ? "   <- replaced by the frozen skeletons (cold deaths use its type " + DeathType + ")" : ""));
        if (DeathArtInfo != "") sb.AppendLine(DeathArtInfo);
        if (DeathArt == null) sb.AppendLine("No picture belongs clearly to one rare kind of death, so none was replaced: the cold deaths show the game's plain death screen with Harsh Winter's own death text. Send this list to have one chosen.");
        sb.AppendLine();
        WinterPerkIcons.Report(sb);
    }
}
