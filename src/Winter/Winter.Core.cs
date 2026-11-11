// =============================================================================
// Harsh Winter builder - main: game paks, rules, textures, terrain map, output.
//
// Builds Mods\harsh_winter_world from the player's OWN game files (the game's
// art and data can't be redistributed). Compiled on the fly by
// HarshWinterTextures.ps1 from the Winter.*.cs files, so it needs nothing but
// Windows. C# 5 only (Windows PowerShell 5.1 compiles it).
//
//   textures    Data\harsh_winter_world*.pak     snow on every snow-catching surface
//   terrain     Data\Levels\<level>\level.pak    the distant landscape under snow
//   footprints  Data\harsh_winter_world_life.pak snow prints at every footstep
//   clothing    (same pak)                       NPC winter clothing
//
// Paks are written uncompressed ("stored" zip), as Warhorse's publishing guide
// describes, and split before 1.9 GB.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

// ---------------------------------------------------------------------- stored zip writer
public class PakWriter : IDisposable
{
    class Rec { public byte[] Name; public uint Crc; public long Size, Offset; }
    readonly FileStream fs;
    readonly List<Rec> recs = new List<Rec>();
    public long Length { get { return fs.Position; } }
    public int Count { get { return recs.Count; } }
    public readonly string Path;

    public PakWriter(string path) { Path = path; fs = new FileStream(path, FileMode.Create, FileAccess.Write); }

    static readonly uint[] Table = MakeTable();
    static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[i] = c; }
        return t;
    }
    public static uint Crc32(byte[] d)
    {
        uint c = 0xFFFFFFFF;
        for (int i = 0; i < d.Length; i++) c = Table[(c ^ d[i]) & 0xFF] ^ (c >> 8);
        return ~c;
    }

    public void Add(string name, byte[] data)
    {
        var r = new Rec { Name = Encoding.UTF8.GetBytes(name.Replace('\\', '/')), Crc = Crc32(data), Size = data.Length, Offset = fs.Position };
        bool z64 = r.Offset >= 0xFFFFFFFFL || r.Size >= 0xFFFFFFFFL;
        var w = new BinaryWriter(fs);
        w.Write(0x04034b50u); W16(w, z64 ? 45 : 10); W16(w, 0x800); W16(w, 0); W16(w, 0); W16(w, 0x21);
        w.Write(r.Crc);
        w.Write(z64 ? 0xFFFFFFFFu : (uint)r.Size); w.Write(z64 ? 0xFFFFFFFFu : (uint)r.Size);
        W16(w, r.Name.Length); W16(w, z64 ? 20 : 0);
        w.Write(r.Name);
        if (z64) { W16(w, 1); W16(w, 16); w.Write(r.Size); w.Write(r.Size); }
        w.Write(data);
        w.Flush();
        recs.Add(r);
    }
    public void AddText(string name, string text) { Add(name, Encoding.UTF8.GetBytes(text)); }
    static void W16(BinaryWriter w, int v) { w.Write((ushort)v); }

    public void Dispose()
    {
        var w = new BinaryWriter(fs);
        long cdStart = fs.Position;
        foreach (var r in recs)
        {
            bool bigS = r.Size >= 0xFFFFFFFFL, bigO = r.Offset >= 0xFFFFFFFFL;
            int extra = (bigS ? 16 : 0) + (bigO ? 8 : 0);
            w.Write(0x02014b50u); W16(w, 45); W16(w, (bigS || bigO) ? 45 : 10); W16(w, 0x800); W16(w, 0); W16(w, 0); W16(w, 0x21);
            w.Write(r.Crc);
            w.Write(bigS ? 0xFFFFFFFFu : (uint)r.Size); w.Write(bigS ? 0xFFFFFFFFu : (uint)r.Size);
            W16(w, r.Name.Length); W16(w, extra > 0 ? extra + 4 : 0); W16(w, 0); W16(w, 0); W16(w, 0); w.Write(0u);
            w.Write(bigO ? 0xFFFFFFFFu : (uint)r.Offset);
            w.Write(r.Name);
            if (extra > 0) { W16(w, 1); W16(w, extra); if (bigS) { w.Write(r.Size); w.Write(r.Size); } if (bigO) w.Write(r.Offset); }
        }
        long cdEnd = fs.Position, cdSize = cdEnd - cdStart;
        bool z64 = recs.Count >= 0xFFFF || cdStart >= 0xFFFFFFFFL;
        if (z64)
        {
            w.Write(0x06064b50u); w.Write(44L); W16(w, 45); W16(w, 45); w.Write(0u); w.Write(0u);
            w.Write((long)recs.Count); w.Write((long)recs.Count); w.Write(cdSize); w.Write(cdStart);
            w.Write(0x07064b50u); w.Write(0u); w.Write(cdEnd); w.Write(1u);
        }
        w.Write(0x06054b50u); W16(w, 0); W16(w, 0);
        W16(w, z64 ? 0xFFFF : recs.Count); W16(w, z64 ? 0xFFFF : recs.Count);
        w.Write(z64 ? 0xFFFFFFFFu : (uint)cdSize); w.Write(z64 ? 0xFFFFFFFFu : (uint)cdStart);
        W16(w, 0);
        w.Flush();
        fs.Dispose();
    }
}

// A series of paks name.pak, name_2.pak ... each kept under a size limit.
public class PakSet : IDisposable
{
    readonly string dir, baseName; readonly long limit;
    PakWriter cur; int n;
    public readonly List<string> Files = new List<string>();
    public PakSet(string dir, string baseName, long limit) { this.dir = dir; this.baseName = baseName; this.limit = limit; }
    public void AddGroup(List<KeyValuePair<string, byte[]>> files)
    {
        long size = 0; foreach (var f in files) size += f.Value.Length;
        if (cur == null || (cur.Count > 0 && cur.Length + size > limit))
        {
            if (cur != null) cur.Dispose();
            n++;
            var p = System.IO.Path.Combine(dir, baseName + (n == 1 ? "" : "_" + n) + ".pak");
            cur = new PakWriter(p); Files.Add(p);
        }
        foreach (var f in files) cur.Add(f.Key, f.Value);
    }
    public void Dispose() { if (cur != null) cur.Dispose(); cur = null; }
}

public static class WinterTextures
{
    public static Action<string> Log = Console.WriteLine;
    public const string ModId = "harsh_winter_world";
    public static string LoadingPath = "";
    const long PakLimit = 1900L * 1024 * 1024;

    // ------------------------------------------------------------------ paks
    public class Entry { public string Pak, Raw, Level; }

    public static Dictionary<string, Entry> IndexPaks(string game)
    {
        var index = new Dictionary<string, Entry>();
        foreach (var dir in new[] { Path.Combine(game, "Data"), Path.Combine(game, "Engine") })
        {
            if (!Directory.Exists(dir)) continue;
            var paks = new List<string>(Directory.GetFiles(dir, "*.pak", SearchOption.AllDirectories));
            paks.Sort(StringComparer.Ordinal);
            foreach (var pak in paks)
            {
                string level = null;
                var rel = pak.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/');
                var m = Regex.Match(rel, "^levels/([^/]+)/", RegexOptions.IgnoreCase);
                if (m.Success) level = m.Groups[1].Value.ToLowerInvariant();
                try
                {
                    using (var z = ZipFile.OpenRead(pak))
                        foreach (var e in z.Entries)
                        {
                            if (e.FullName.EndsWith("/")) continue;
                            var name = e.FullName.Replace('\\', '/').ToLowerInvariant();
                            if (level != null) name = "levels/" + level + "/" + name;
                            if (!index.ContainsKey(name)) index[name] = new Entry { Pak = pak, Raw = e.FullName.Replace('\\', '/'), Level = level };
                        }
                }
                catch (Exception) { Log("  skipped unreadable pak " + rel); }
            }
        }
        return index;
    }

    public class Reader : IDisposable
    {
        readonly Dictionary<string, ZipArchive> open = new Dictionary<string, ZipArchive>();
        readonly Dictionary<string, Dictionary<string, ZipArchiveEntry>> map = new Dictionary<string, Dictionary<string, ZipArchiveEntry>>();
        public byte[] Read(Entry e)
        {
            ZipArchive z;
            if (!open.TryGetValue(e.Pak, out z))
            {
                z = ZipFile.OpenRead(e.Pak); open[e.Pak] = z;
                var m = new Dictionary<string, ZipArchiveEntry>();
                foreach (var en in z.Entries) { var k = en.FullName.Replace('\\', '/'); if (!m.ContainsKey(k)) m[k] = en; }
                map[e.Pak] = m;
            }
            var entry = map[e.Pak][e.Raw];
            using (var s = entry.Open())
            using (var ms = new MemoryStream((int)Math.Min(int.MaxValue, Math.Max(0, entry.Length))))
            { s.CopyTo(ms); return ms.ToArray(); }
        }
        public void Dispose() { foreach (var z in open.Values) z.Dispose(); }
    }

    // ------------------------------------------------------------------ rules
    class Rule { public string Group; public Regex Rx; public string Pattern; public int Hits; }

    static List<Rule> ReadRules(string file)
    {
        var rules = new List<Rule>();
        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var parts = Regex.Split(line, "\\s+");
            if (parts.Length < 2) continue;
            var pat = parts[1].ToLowerInvariant().Replace('\\', '/');
            rules.Add(new Rule { Group = parts[0].ToLowerInvariant(), Pattern = pat,
                Rx = new Regex("^" + Regex.Escape(pat).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.Singleline) });
        }
        return rules;
    }

    static string ToDds(string path)
    {
        var p = path.Replace('\\', '/').ToLowerInvariant().TrimStart('/');
        return Regex.Replace(p, "\\.(tif|tga|png|dds)$", "") + ".dds";
    }

    // Diffuse and normal map files of a material.
    static void MaterialMaps(string text, List<string> diffuse, List<string> normal)
    {
        foreach (Match m in Regex.Matches(text, "<Texture\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var map = Regex.Match(m.Value, "Map=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            var file = Regex.Match(m.Value, "File=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (!map.Success || !file.Success) continue;
            var k = map.Groups[1].Value.ToLowerInvariant();
            if (k == "diffuse" || k == "texslot_diffuse") diffuse.Add(ToDds(file.Groups[1].Value));
            else if (k == "bumpmap" || k == "normal" || k == "normalmap" || k == "texslot_normalmap" || k == "texslot_bumpmap") normal.Add(ToDds(file.Groups[1].Value));
        }
    }

    // Path -> group for everything the rules select; normals: diffuse -> its normal map.
    public static readonly HashSet<string> InteriorTextures = new HashSet<string>(StringComparer.Ordinal);
    public static readonly List<string> InteriorSkipped = new List<string>();
    // Words that mark textures a snow pass probably should cover (for the report).
    static readonly string[] Candidates = { "road", "path", "street", "cobble", "pavement", "mud", "dirt", "ground", "soil", "terrain", "gravel", "roof", "thatch", "shingle", "straw",
        "tree", "leaf", "leaves", "foliage", "branch", "twig", "bush", "shrub", "crown", "birch", "betula", "acer", "maple", "tilia", "linden", "alnus", "alder",
        "salix", "willow", "populus", "poplar", "fraxinus", "ulmus", "sorbus", "malus", "prunus", "pyrus", "corylus", "larix", "spruce", "pine" };

    static SortedDictionary<string, string> Select(Dictionary<string, Entry> index, List<Rule> rules, Reader reader, Dictionary<string, string> normals)
    {
        var excl = rules.FindAll(delegate (Rule r) { return r.Group == "exclude"; });
        Func<string, bool> excluded = delegate (string n) { foreach (var r in excl) if (r.Rx.IsMatch(n)) { r.Hits++; return true; } return false; };
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var names = new List<string>(index.Keys);
        names.Sort(StringComparer.Ordinal);
        // Textures that interior materials use (floors, walls, furniture, the inside
        // of roofs): they must not get snow, wherever else they're used too.
        InteriorTextures.Clear(); InteriorSkipped.Clear();
        var interiorRules = rules.FindAll(delegate (Rule r) { return r.Group == "interior-mtl"; });
        if (interiorRules.Count > 0)
            foreach (var n in names)
            {
                if (!n.EndsWith(".mtl")) continue;
                bool hit = false;
                foreach (var r in interiorRules) if (r.Rx.IsMatch(n)) { r.Hits++; hit = true; break; }
                if (!hit) continue;
                string text;
                try { text = Encoding.UTF8.GetString(reader.Read(index[n])); } catch (Exception) { continue; }
                var dif = new List<string>(); var nor = new List<string>();
                MaterialMaps(text, dif, nor);
                foreach (var dds in dif) InteriorTextures.Add(dds);
            }
        foreach (var r in rules)
        {
            if (r.Group != "mtl-terrain" && r.Group != "mtl-road") continue;
            string g = r.Group == "mtl-road" ? "road" : "terrain";
            foreach (var n in names)
                if (r.Rx.IsMatch(n))
                {
                    r.Hits++;
                    string text;
                    try { text = Encoding.UTF8.GetString(reader.Read(index[n])); } catch (Exception) { continue; }
                    var dif = new List<string>(); var nor = new List<string>();
                    MaterialMaps(text, dif, nor);
                    foreach (var dds in dif)
                    {
                        if (!index.ContainsKey(dds) || excluded(dds) || result.ContainsKey(dds)) continue;
                        result[dds] = g;
                        if (nor.Count > 0 && index.ContainsKey(nor[0])) normals[dds] = nor[0];
                    }
                }
        }
        foreach (var n in names)
        {
            if (result.ContainsKey(n)) continue;
            bool isDds = n.EndsWith(".dds"), isCover = n.EndsWith("cover.ctc");
            if (!isDds && !isCover) continue;
            // Snow-as-dirt: the characters' dirt/grime colour maps (normally excluded with
            // everything on characters) become powder snow - see winter_rules.txt.
            bool sd = false;
            if (isDds && !n.Contains("_ddn") && !n.Contains("_spec") && !n.Contains("_normal") && !n.Contains("_nrm"))
                foreach (var r in rules)
                    if (r.Group == "snowdirt" && r.Rx.IsMatch(n)) { r.Hits++; result[n] = "snowdirt"; sd = true; break; }
            if (sd) continue;
            if (excluded(n)) continue;
            foreach (var r in rules)
            {
                if (r.Group == "exclude" || r.Group.StartsWith("mtl-") || r.Group == "interior-mtl" || r.Group == "snowdirt") continue;
                if (r.Rx.IsMatch(n))
                {
                    r.Hits++;
                    if (InteriorTextures.Contains(n)) { InteriorSkipped.Add(n + " (" + r.Group + ")"); break; }
                    result[n] = r.Group; break;
                }
            }
        }
        // Normal maps by name for textures that didn't come from a material.
        foreach (var kv in result)
        {
            if (!Snow.IsTiling(kv.Value) || normals.ContainsKey(kv.Key) || !kv.Key.EndsWith("_diff.dds")) continue;
            foreach (var suffix in new[] { "_ddna.dds", "_ddn.dds" })
            {
                var cand = kv.Key.Substring(0, kv.Key.Length - "_diff.dds".Length) + suffix;
                if (index.ContainsKey(cand)) { normals[kv.Key] = cand; break; }
            }
        }
        return result;
    }

    // ------------------------------------------------------------------ DDS
    public class Dds { public int W, H, Mips, Fmt, Hdr; public uint Dxgi; public bool Srgb, Signed; public string FormatName; }

    public static Dds ParseHeader(byte[] d)
    {
        if (d.Length < 128 || d[0] != 'D' || d[1] != 'D' || d[2] != 'S' || d[3] != ' ') return null;
        var x = new Dds { H = BitConverter.ToInt32(d, 12), W = BitConverter.ToInt32(d, 16), Mips = Math.Max(1, BitConverter.ToInt32(d, 28)), Hdr = 128 };
        string fourcc = Encoding.ASCII.GetString(d, 84, 4);
        x.FormatName = fourcc;
        x.Srgb = (BitConverter.ToUInt32(d, 36) & 0x80000) != 0;   // CryEngine's sRGB flag in the reserved header words
        if (fourcc == "DXT1") x.Fmt = Bc.BC1;
        else if (fourcc == "DXT5" || fourcc == "DXT4") x.Fmt = Bc.BC3;
        else if (fourcc == "ATI2" || fourcc == "BC5U") x.Fmt = Bc.BC5;
        else if (fourcc == "BC5S") { x.Fmt = Bc.BC5; x.Signed = true; }
        else if (fourcc == "DX10")
        {
            x.Hdr = 148;
            x.Dxgi = BitConverter.ToUInt32(d, 128);
            x.FormatName = "DXGI " + x.Dxgi;
            if (x.Dxgi >= 70 && x.Dxgi <= 72) x.Fmt = Bc.BC1;
            else if (x.Dxgi >= 76 && x.Dxgi <= 78) x.Fmt = Bc.BC3;
            else if (x.Dxgi >= 82 && x.Dxgi <= 84) { x.Fmt = Bc.BC5; x.Signed = x.Dxgi == 84; }
            else x.Fmt = Bc.OTHER;
            if (x.Dxgi == 72 || x.Dxgi == 78) x.Srgb = true;
            if (x.Dxgi == 71 || x.Dxgi == 77) x.Srgb = false;
        }
        else x.Fmt = Bc.OTHER;
        return x;
    }

    static long MipBytes(Dds x, int i) { return Bc.MipBytes(x.Fmt, Math.Max(1, x.W >> i), Math.Max(1, x.H >> i)); }

    static bool IsCExt(byte[] d, long at) { return at + 4 <= d.Length && d[at] == 'C' && d[at + 1] == 'E' && d[at + 2] == 'x' && d[at + 3] == 't'; }

    public class Region { public byte[] Buf; public long Start; public int FirstMip, LastMip, Part; }

    // Which mips each file holds, by size. Region 0 = header file. Null if unknown.
    static List<Region> Layout(Dds x, byte[] head, List<byte[]> parts)
    {
        var regions = new List<Region>();
        var used = new bool[x.Mips];
        long avail = head.Length - x.Hdr;
        int first = -1;
        for (int j = 0; j < x.Mips; j++)
        {
            long sum = 0;
            for (int i = j; i < x.Mips; i++) sum += MipBytes(x, i);
            if (sum == avail || (sum < avail && IsCExt(head, x.Hdr + sum))) { first = j; break; }
        }
        if (first < 0) return null;
        regions.Add(new Region { Buf = head, Start = x.Hdr, FirstMip = first, LastMip = x.Mips - 1, Part = 0 });
        for (int i = first; i < x.Mips; i++) used[i] = true;
        for (int k = 0; k < parts.Count; k++)
        {
            var p = parts[k];
            int found = -1;
            for (int i = 0; i < x.Mips && found < 0; i++)
                if (!used[i] && (MipBytes(x, i) == p.Length || (MipBytes(x, i) < p.Length && IsCExt(p, MipBytes(x, i))))) found = i;
            if (found < 0) return null;
            used[found] = true;
            regions.Add(new Region { Buf = p, Start = 0, FirstMip = found, LastMip = found, Part = k + 1 });
        }
        for (int i = 0; i < x.Mips; i++) if (!used[i]) return null;
        return regions;
    }

    static long MipOffset(Dds x, Region r, int mip) { long o = r.Start; for (int i = r.FirstMip; i < mip; i++) o += MipBytes(x, i); return o; }

    static Region RegionOf(List<Region> regs, int mip) { foreach (var r in regs) if (mip >= r.FirstMip && mip <= r.LastMip) return r; return null; }

    // ------------------------------------------------------------------ one texture
    public class Options { public float Strength = 1; public string Detail = "high"; }

    class Result { public List<KeyValuePair<string, byte[]>> Files = new List<KeyValuePair<string, byte[]>>(); public bool Upscaled; public int Cleared; }

    // Returns the files to write, or null with why set.
    static Result ProcessTexture(string name, string group, Dictionary<string, Entry> index, Reader reader, Options o, out string why)
    {
        why = null;
        var e = index[name];
        var head = reader.Read(e);
        var x = ParseHeader(head);
        if (x == null) { why = "not a DDS file"; return null; }
        if (x.Fmt != Bc.BC1 && x.Fmt != Bc.BC3) { why = x.FormatName + " (only BC1/BC3 colour textures are rebuilt)"; return null; }
        var partRaw = new List<string>(); var parts = new List<byte[]>();
        for (int k = 1; index.ContainsKey(name + "." + k); k++) { partRaw.Add(index[name + "." + k].Raw); parts.Add(reader.Read(index[name + "." + k])); }
        var regs = Layout(x, head, parts);
        if (regs == null) { why = "unknown mip layout"; return null; }
        var r0 = RegionOf(regs, 0);
        var top = Bc.Decode(r0.Buf, MipOffset(x, r0, 0), x.Fmt, x.W, x.H);

        bool alpha = false;
        float lim = x.Fmt == Bc.BC1 ? 0.5f : 0.98f;
        foreach (var a in top.A) if (a < lim) { alpha = true; break; }

        // Twice the resolution for the tiling surfaces, where it can be done safely.
        int maxSrc = o.Detail == "ultra" ? 2048 : (o.Detail == "high" ? 1024 : 0);
        int order = UpscaleOrder(regs);    // 0 = can't, 1 = not split, 2 = .1 next to the tail, 3 = .1 = biggest
        bool up = Snow.IsTiling(group) && Math.Max(x.W, x.H) <= maxSrc && x.W >= 16 && x.H >= 16 && order != 0
            && !index.ContainsKey(name + ".a") && x.Mips >= 2;
        if (up)
        {
            if (x.Srgb) Bc.ToLinear(top);
            top = Bc.Up2(top);
            if (x.Srgb) Bc.ToSrgb(top);
        }
        int seed = (int)(PakWriter.Crc32(Encoding.UTF8.GetBytes(name)) & 0x7FFFFFFF);
        var res = new Result { Upscaled = up };
        res.Cleared = Snow.Apply(top, group, seed, o.Strength, alpha, false);
        if (res.Cleared < 0) { why = "a grey mask, not a dirt colour map - left alone"; return null; }
        int count = x.Mips + (up ? 1 : 0);
        var chain = Snow.Chain(top, count, x.Srgb, alpha && Snow.IsVegetation(group));
        bool punch = x.Fmt == Bc.BC1 && alpha;
        var enc = new List<byte[]>();
        foreach (var lvl in chain) enc.Add(Bc.Encode(lvl, x.Fmt, punch));

        if (!up)
        {
            foreach (var r in regs)
                for (int m = r.FirstMip; m <= r.LastMip; m++)
                    Array.Copy(enc[m], 0, r.Buf, MipOffset(x, r, m), enc[m].Length);
            res.Files.Add(new KeyValuePair<string, byte[]>(e.Raw, head));
            for (int k = 0; k < parts.Count; k++) res.Files.Add(new KeyValuePair<string, byte[]>(partRaw[k], parts[k]));
            return res;
        }

        // Upscaled: new header (double size, one more mip), same tail mips in the header file.
        var hr = regs[0];   // the header file's region (the tail of small mips)
        long tailBytes = 0; for (int i = hr.FirstMip; i <= hr.LastMip; i++) tailBytes += MipBytes(x, i);
        var trailer = new byte[head.Length - x.Hdr - tailBytes];
        Array.Copy(head, x.Hdr + tailBytes, trailer, 0, trailer.Length);
        int tailCount = x.Mips - regs[0].FirstMip;
        var ms = new MemoryStream();
        var hdr = new byte[x.Hdr]; Array.Copy(head, hdr, x.Hdr);
        PutInt(hdr, 12, x.H * 2); PutInt(hdr, 16, x.W * 2); PutInt(hdr, 20, enc[0].Length); PutInt(hdr, 28, count);
        ms.Write(hdr, 0, hdr.Length);
        for (int m = count - tailCount; m < count; m++) ms.Write(enc[m], 0, enc[m].Length);
        ms.Write(trailer, 0, trailer.Length);
        res.Files.Add(new KeyValuePair<string, byte[]>(e.Raw, ms.ToArray()));
        // Parts: same sizes as before, plus the new biggest one, named the way the game names them.
        int nParts = parts.Count;
        for (int k = 1; k <= nParts; k++)
        {
            var r = regs[k];
            int newMip = r.FirstMip + 1;
            string pn = order == 3 ? e.Raw + "." + (k + 1) : partRaw[k - 1];
            res.Files.Add(new KeyValuePair<string, byte[]>(pn, enc[newMip]));
        }
        res.Files.Add(new KeyValuePair<string, byte[]>(order == 3 ? e.Raw + ".1" : e.Raw + "." + (nParts + 1), enc[0]));
        if (order == 1) res.Files.RemoveRange(1, res.Files.Count - 1);   // not split: everything is in the header file
        if (order == 1)
        {
            var all = new MemoryStream();
            all.Write(hdr, 0, hdr.Length);
            for (int m = 0; m < count; m++) all.Write(enc[m], 0, enc[m].Length);
            all.Write(trailer, 0, trailer.Length);
            res.Files[0] = new KeyValuePair<string, byte[]>(e.Raw, all.ToArray());
        }
        return res;
    }

    static void PutInt(byte[] b, int at, int v) { var x = BitConverter.GetBytes(v); Array.Copy(x, 0, b, at, 4); }

    // How a split texture numbers its parts. Only clear, monotonic layouts are upscaled.
    static int UpscaleOrder(List<Region> regs)
    {
        int n = regs.Count - 1;
        if (n == 0) return regs[0].FirstMip == 0 ? 1 : 0;
        if (n < 2) return 0;
        int first = regs[0].FirstMip;
        bool next = true, big = true;
        for (int k = 1; k <= n; k++)
        {
            if (regs[k].FirstMip != first - k) next = false;
            if (regs[k].FirstMip != k - 1) big = false;
        }
        return next ? 2 : (big ? 3 : 0);
    }

    // ------------------------------------------------------------------ normal maps
    // How strongly the photo snow's micro-surface shows in a snowed normal map.
    static float MicroAmount(string group) { return group == "terrain" ? 0.35f : (group == "roof" ? 0.28f : (group == "road" ? 0.22f : (group == "rock" ? 0.15f : 0))); }

    static Result ProcessNormal(string name, string group, Dictionary<string, Entry> index, Reader reader, float amount, out string why)
    {
        why = null;
        var e = index[name];
        var head = reader.Read(e);
        var x = ParseHeader(head);
        if (x == null) { why = "not a DDS file"; return null; }
        var partRaw = new List<string>(); var parts = new List<byte[]>();
        for (int k = 1; index.ContainsKey(name + "." + k); k++) { partRaw.Add(index[name + "." + k].Raw); parts.Add(reader.Read(index[name + "." + k])); }
        if (x.Fmt == Bc.OTHER) { why = x.FormatName; return null; }
        var regs = Layout(x, head, parts);
        if (regs == null) { why = "unknown mip layout"; return null; }
        float micro = Snow.HasDetail ? MicroAmount(group) : 0;
        if (x.Fmt == Bc.BC5)
        {
            foreach (var r in regs)
            {
                if (micro <= 0)
                {
                    long end = MipOffset(x, r, r.LastMip) + MipBytes(x, r.LastMip);
                    Snow.FlattenBc5(r.Buf, r.Start, end, amount, x.Signed);
                    continue;
                }
                for (int m = r.FirstMip; m <= r.LastMip; m++)
                {
                    int w = Math.Max(1, x.W >> m), h = Math.Max(1, x.H >> m);
                    Snow.SnowNormalsBc5(r.Buf, MipOffset(x, r, m), w, h, amount, micro / (1 + m * 0.5f), x.Signed);
                }
            }
        }
        else
        {
            var r0 = RegionOf(regs, 0);
            var test = Bc.Decode(r0.Buf, MipOffset(x, r0, 0), x.Fmt, x.W, x.H);
            double blue = 0; foreach (var bv in test.B) blue += bv; blue /= test.B.Length;
            if (blue < 0.6) { why = "not an RGB normal map"; return null; }
            foreach (var r in regs)
                for (int m = r.FirstMip; m <= r.LastMip; m++)
                {
                    int w = Math.Max(1, x.W >> m), h = Math.Max(1, x.H >> m);
                    long off = MipOffset(x, r, m);
                    var im = Bc.Decode(r.Buf, off, x.Fmt, w, h);
                    Snow.FlattenRgb(im, amount, micro / (1 + m * 0.5f));
                    var enc = Bc.Encode(im, x.Fmt, false);
                    Array.Copy(enc, 0, r.Buf, off, enc.Length);
                }
        }
        var res = new Result();
        res.Files.Add(new KeyValuePair<string, byte[]>(e.Raw, head));
        for (int k = 0; k < parts.Count; k++) res.Files.Add(new KeyValuePair<string, byte[]>(partRaw[k], parts[k]));
        return res;
    }

    static float NormalAmount(string group) { return group == "roof" ? 0.8f : (group == "terrain" ? 0.75f : (group == "road" ? 0.55f : 0.35f)); }

    // ------------------------------------------------------------------ terrain cover (cover.ctc)
    // Layout (checked against KCD1's; verified again on each file):
    //   "CRY\0", u16 version, u16 flags, u32 layers, f32,
    //   per layer 12 bytes: u16 sector size (px), u16, u8 format (+3), u32 sector bytes,
    //   u16 node count, node count x u16 tile index (0xFFFF = none),
    //   tiles: for each tile, every layer's sector data. Layer 0 is the colour map.
    static byte[] ProcessCover(byte[] d, float k, string name, out string why)
    {
        why = null;
        if (d.Length < 64 || d[0] != 'C' || d[1] != 'R' || d[2] != 'Y' || d[3] != 0) { why = "no CRY signature"; return null; }
        int layers = BitConverter.ToInt32(d, 8);
        if (layers < 1 || layers > 4) { why = "unexpected layer count " + layers; return null; }
        var res = new int[layers]; var bytes = new long[layers];
        for (int i = 0; i < layers; i++) { res[i] = BitConverter.ToUInt16(d, 16 + 12 * i); bytes[i] = BitConverter.ToUInt32(d, 16 + 12 * i + 8); }
        int at = 16 + 12 * layers;
        int nodes = BitConverter.ToUInt16(d, at);
        long dataStart = at + 2 + 2L * nodes;
        long per = 0; for (int i = 0; i < layers; i++) per += bytes[i];
        if (per <= 0 || dataStart >= d.Length || (d.Length - dataStart) % per != 0) { why = "sizes don't add up"; return null; }
        long tiles = (d.Length - dataStart) / per;
        int maxIdx = -1;
        for (int q = 0; q < nodes; q++) { int v = BitConverter.ToUInt16(d, at + 2 + 2 * q); if (v != 0xFFFF && v > maxIdx) maxIdx = v; }
        if (maxIdx + 1 != tiles) { why = "tile index doesn't match tile count"; return null; }
        long px = (long)res[0] * res[0];
        int fmt;
        if (bytes[0] == px / 2) fmt = Bc.BC1; else if (bytes[0] == px) fmt = Bc.BC3; else { why = "colour layer is not BC1/BC3"; return null; }
        for (long t = 0; t < tiles; t++)
        {
            long off = dataStart + t * per;
            var im = Bc.Decode(d, off, fmt, res[0], res[0]);
            Snow.Apply(im, "cover", 0, k, false, true);
            var enc = Bc.Encode(im, fmt, false);
            Array.Copy(enc, 0, d, off, enc.Length);
        }
        Log(string.Format("  distant terrain {0}: {1} tiles of {2}px snowed under", name, tiles, res[0]));
        return d;
    }

    // ------------------------------------------------------------------ main
    const string Manifest = "<?xml version=\"1.0\" encoding=\"us-ascii\"?>\r\n<kcd_mod>\r\n\t<info>\r\n\t\t<name>Harsh Winter - World (generated)</name>\r\n\t\t<modid>harsh_winter_world</modid>\r\n\t\t<description>Permanent winter for the whole world: snow on every road, path, field, roof and on the distant landscape; snow-laden conifers and bare broadleaf trees; snow footprints and hoofprints at every step; NPCs in winter clothing. Generated on your PC from your own game files by Harsh Winter's builder.</description>\r\n\t\t<author>Harsh Winter</author>\r\n\t\t<version>4.5.1</version>\r\n\t\t<created_on>{date}</created_on>\r\n\t\t<modifies_level>{level}</modifies_level>\r\n\t</info>\r\n</kcd_mod>\r\n";

    // what: all | textures | clothing | footprints | loading | extras. detail: native | high | ultra.
    public static int Run(string what, string game, string helperDir, string modsDir, float strength, string detail, bool list, bool noCover)
    {
        return Run(what, game, helperDir, modsDir, strength, detail, list, noCover, false);
    }

    public static int Run(string what, string game, string helperDir, string modsDir, float strength, string detail, bool list, bool noCover, bool noLoading)
    {
        if (what != "all" && what != "textures" && what != "clothing" && what != "footprints" && what != "loading" && what != "extras") { Log("Choose all, textures, clothing, footprints, loading or extras."); return 1; }
        if (detail != "native" && detail != "high" && detail != "ultra") { Log("Detail must be native, high or ultra."); return 1; }
        if (!Directory.Exists(Path.Combine(game, "Data"))) { Log("No Data folder in " + game + " - is this the game folder?"); return 1; }
        var o = new Options { Strength = Math.Max(0.3f, Math.Min(1.3f, strength)), Detail = detail };
        var t0 = DateTime.Now;
        Log("Reading the game's pak indexes...");
        var index = IndexPaks(game);
        // which edition this is (Czech or English): decides the language of the generated key names
        try
        {
            var lf = Path.Combine(helperDir, "language.txt");
            WinterCampKeys.Czech = !File.Exists(lf) || File.ReadAllText(lf).Trim().StartsWith("cz", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { }
        Log(index.Count + " files indexed.");
        string outDir = Path.Combine(modsDir, ModId);
        string dataDir = Path.Combine(outDir, "Data");
        if (!list) Directory.CreateDirectory(dataDir);
        using (var reader = new Reader())
        {
            Func<string, byte[]> read = delegate (string n) { return reader.Read(index[n]); };
            // Shader check, death screen and the generated game-over rows: every run (quick).
            // Kroniky: texture-only build; never add survival, death or shader patches.
            if (what == "all" || what == "textures")
            {
                if (Snow.LoadDetail(Path.Combine(helperDir, "snow_detail.bin"))) Log("Photo snow detail loaded (grain, sparkle, wind ripples).");
                else Log("(helper\\snow_detail.bin missing: procedural snow grain only)");
                int rc = BuildTextures(index, reader, ReadRules(Path.Combine(helperDir, "winter_rules.txt")), dataDir, o, list, noCover);
                if (rc != 0) return rc;
            }
            if (what == "all" || what == "extras") WinterCampKeys.Build(modsDir, list);
            var loadingPak = Path.Combine(dataDir, ModId + "_loading.pak");
            // Winter loading screens, in this order:
            //  1. shipped with the mod (Mods\harsh_winter\Data\harsh_winter_loading_*.pak) -> those are used as
            //     they are and no other mod is read; only the game's own screens (which can't be shipped) are
            //     winterised here, so every loading screen is a winter one;
            //  2. else, loading-screen mods installed -> winterise their screens (and the game's own);
            //  3. else -> nothing: the game's own loading screens.
            // "Build winter.bat loading" always repaints (the game's own screens too).
            var shipped = new List<string>();
            var shippedPaths = new List<string>();
            var modData = Path.Combine(helperDir, "..", "Data");
            if (Directory.Exists(modData)) foreach (var f in Directory.GetFiles(modData, "harsh_winter_loading*.pak")) { shipped.Add(Path.GetFileName(f)); shippedPaths.Add(f); }
            WinterLoading.Skip = WinterLoading.ArtworksIn(shippedPaths);
            if (!noLoading)
            {
                WinterLoading.FindConflicts(modsDir);
                foreach (var c in WinterLoading.Conflicts) Log("  ! " + c);
            }
            if (noLoading)
            {
                LoadingPath = "off (Build winter.bat noloading): the game's own loading screens";
                if (!list && File.Exists(loadingPak)) { File.Delete(loadingPak); Log("Winter loading screens removed (the game's own are back)."); }
            }
            else if ((what == "all" || what == "loading") && shipped.Count > 0)
            {
                Log("\n=== Winter loading screens -> Mods\\" + ModId);
                Log("  shipped winter art: " + string.Join(", ", shipped.ToArray()) + " (" + WinterLoading.Skip.Count + " pictures) - used as it is, no other mod read");
                WinterLoading.ScanMods = false;
                WinterLoading.RequireMods = false;
                int rc = WinterLoading.Build(index, read, modsDir, loadingPak, list);
                if (rc != 0) return rc;
                LoadingPath = "shipped winter art used (" + string.Join(", ", shipped.ToArray()) + ", " + WinterLoading.Skip.Count + " pictures), no other mod read; the game's own " + WinterLoading.LastCount + " screens winterised on your PC";
                if (WinterLoading.LastCount == 0 && !list && File.Exists(loadingPak)) File.Delete(loadingPak);
                Log("Winter loading screens: " + LoadingPath);
            }
            else if (what == "all" || what == "loading")
            {
                Log("\n=== Winter loading screens -> Mods\\" + ModId);
                WinterLoading.Skip.Clear();
                WinterLoading.ScanMods = true;
                WinterLoading.RequireMods = what != "loading";
                int rc = WinterLoading.Build(index, read, modsDir, loadingPak, list);
                if (rc != 0) return rc;
                if (WinterLoading.LastCount > 0)
                    LoadingPath = "compiled: " + WinterLoading.LastCount + " artworks winterised" + (WinterLoading.LastSources.Count > 0 ? " (from " + string.Join(", ", WinterLoading.LastSources.ToArray()) + " and the game)" : " (the game's own)");
                else
                {
                    LoadingPath = "skipped: no winter art shipped and no loading-screen mods installed - the game's own loading screens";
                    if (!list && File.Exists(loadingPak)) File.Delete(loadingPak);
                }
                Log("Winter loading screens: " + LoadingPath);
            }
            if (what != "textures" && what != "loading" && what != "extras")
            {
                PakWriter life = null;
                var lifePath = Path.Combine(dataDir, ModId + "_life.pak");
                // Rebuilding one half keeps the other half of the life pak.
                var keep = new List<KeyValuePair<string, byte[]>>();
                string oldLua = "";
                if (!list && what != "all" && File.Exists(lifePath))
                {
                    using (var z = ZipFile.OpenRead(lifePath))
                        foreach (var en in z.Entries)
                        {
                            bool isFoot = en.FullName.StartsWith("Libs/MaterialEffects/", StringComparison.OrdinalIgnoreCase);
                            bool isCloth = en.FullName.StartsWith("Libs/Tables/", StringComparison.OrdinalIgnoreCase);
                            bool isLua = en.FullName.StartsWith("Scripts/", StringComparison.OrdinalIgnoreCase);
                            using (var s = en.Open()) using (var ms = new MemoryStream())
                            {
                                s.CopyTo(ms);
                                if (isLua) oldLua = Encoding.UTF8.GetString(ms.ToArray());
                                else if ((what == "clothing" && isFoot) || (what == "footprints" && isCloth)) keep.Add(new KeyValuePair<string, byte[]>(en.FullName, ms.ToArray()));
                            }
                        }
                }
                if (!list) { life = new PakWriter(lifePath + ".tmp"); foreach (var kv in keep) life.Add(kv.Key, kv.Value); }
                try
                {
                    if (what == "all" || what == "footprints")
                    {
                        int rc = WinterFootprints.Build(index.Keys, read, Path.Combine(helperDir, "winter_footprints.txt"), life, list);
                        if (rc != 0) return rc;
                    }
                    if (what == "all" || what == "clothing")
                    {
                        int rc = WinterClothing.Build(index.Keys, read, Path.Combine(helperDir, "winter_clothing.txt"), life, list);
                        if (rc != 0) return rc;
                        rc = WinterWear.Build(index.Keys, read, Path.Combine(helperDir, "winter_wear.txt"), life, list);
                        if (rc != 0) return rc;
                    }
                    if (life != null)
                    {
                        int presets = what == "footprints" ? OldNumber(oldLua, "presets") : WinterClothing.LastChanged;
                        int prints = what == "clothing" ? OldNumber(oldLua, "footprints") : WinterFootprints.LastEffects;
                        string mode = what == "clothing" ? OldString(oldLua, "prints", "script") : WinterFootprints.LastMode;
                        int wearTypes = what == "footprints" ? OldNumber(oldLua, "wear") : WinterWear.LastTypes;
                        string wearLua = what == "footprints" ? OldTable(oldLua, "wearTypes") : WinterWear.LastLua;
                        life.AddText("Scripts/Mods/" + ModId + ".lua",
                            "-- Generated by Harsh Winter's builder: tells the main mod what the generated world contains.\r\n" +
                            "HarshWinterWorld = { presets = " + presets + ", footprints = " + prints + ", prints = \"" + mode + "\", wear = " + Math.Max(0, wearTypes) + ", built = \"" + DateTime.Now.ToString("yyyy-MM-dd") + "\" }\r\n" +
                            "-- Clothing warmth: worn-buff id -> { points, gloves, head, feet } (helper\\winter_wear.txt)\r\n" +
                            "HarshWinterWorld.wearTypes = " + wearLua + "\r\n");
                    }
                }
                finally { if (life != null) life.Dispose(); }
                if (!list)
                {
                    if (File.Exists(lifePath)) File.Delete(lifePath);
                    File.Move(lifePath + ".tmp", lifePath);
                }
            }
        }
        // Extras and loading screens go at the end of build_report.txt (kept from the
        // last texture build when only those were rebuilt).
        try
        {
            var rp = list ? Path.Combine(modsDir, "harsh_winter_list.txt") : Path.Combine(outDir, "build_report.txt");
            string old = File.Exists(rp) ? File.ReadAllText(rp) : "";
            int cut = old.IndexOf("== Snow shaders (WinterEngine", StringComparison.Ordinal);
            if (cut >= 0) old = old.Substring(0, cut);
            var sb = new StringBuilder(old);
            WinterExtras.Report(sb);
            sb.AppendLine("== Camping keys ==");
            sb.AppendLine(WinterCampKeys.State);
            sb.AppendLine();
            sb.AppendLine("== Winter loading screens ==");
            sb.AppendLine(LoadingPath != "" ? LoadingPath : (File.Exists(Path.Combine(dataDir, ModId + "_loading.pak")) ? "kept from an earlier build" : "none built"));
            foreach (var c in WinterLoading.Conflicts) sb.AppendLine("  ! " + c);
            if (!list || File.Exists(rp) || Directory.Exists(Path.GetDirectoryName(rp))) File.WriteAllText(rp, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex) { Log("(could not add to the report: " + ex.Message + ")"); }
        if (list) return 0;
        bool level = Directory.Exists(Path.Combine(dataDir, "Levels"));
        File.WriteAllText(Path.Combine(outDir, "mod.manifest"), Manifest.Replace("{date}", DateTime.Now.ToString("dd.MM.yyyy"))
            .Replace("{level}", level ? "true" : "false"), Encoding.ASCII);
        Log(string.Format("\nAll done in {0:0} s: Mods\\{1}.", (DateTime.Now - t0).TotalSeconds, ModId));
        if (File.Exists(Path.Combine(modsDir, "mod_order.txt")))
            Log("You use Mods\\mod_order.txt: add the lines  harsh_winter  and  " + ModId + "  to it yourself (the builder never edits it).");
        return 0;
    }

    static string OldString(string lua, string key, string fallback)
    {
        var m = Regex.Match(lua ?? "", key + "\\s*=\\s*\"([a-z]+)\"");
        return m.Success ? m.Groups[1].Value : fallback;
    }

    // A table assignment kept from the previous world script (one line).
    static string OldTable(string lua, string key)
    {
        var m = Regex.Match(lua ?? "", "HarshWinterWorld\\." + key + "\\s*=\\s*(\\{[^\\r\\n]*\\})");
        return m.Success ? m.Groups[1].Value : "{}";
    }

    static int OldNumber(string lua, string key)
    {
        var m = Regex.Match(lua ?? "", key + "\\s*=\\s*(-?\\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }

    // A plain-text report of what got snow and what didn't - handy to check that
    // every road, path and roof of YOUR game version was found.
    static void WriteReport(Dictionary<string, Entry> index, List<Rule> rules, SortedDictionary<string, string> selected, Dictionary<string, string> normals, string path)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Harsh Winter - snow texture report (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
            sb.AppendLine();
            sb.AppendLine("== Snowed (group, texture[, normal map]) ==");
            foreach (var kv in selected) sb.AppendLine(string.Format("{0,-11} {1}{2}", kv.Value, kv.Key, normals.ContainsKey(kv.Key) ? "   + " + normals[kv.Key] : ""));
            sb.AppendLine();
            sb.AppendLine("== Kept snow-free: used by interior materials ==");
            foreach (var n in InteriorSkipped) sb.AppendLine(n);
            sb.AppendLine();
            sb.AppendLine("== Not snowed, but the name suggests ground, road, roof or a tree (add a rule in winter_rules.txt if it should be) ==");
            var names = new List<string>(index.Keys); names.Sort(StringComparer.Ordinal);
            foreach (var n in names)
            {
                if (!n.EndsWith("_diff.dds") || selected.ContainsKey(n) || n.Contains("interior") || n.Contains("characters/")) continue;
                foreach (var w in Candidates) if (n.Contains(w)) { sb.AppendLine(n); break; }
            }
            sb.AppendLine();
            sb.AppendLine("== Death screen art candidates (for the WinterEngine add-on's icy death screen, see its README) ==");
            foreach (var n in names)
            {
                if (!(n.EndsWith(".dds") || n.EndsWith(".tif") || n.EndsWith(".png") || n.EndsWith(".gfx"))) continue;
                if (!(n.Contains("ui") || n.Contains("gui"))) continue;
                if (n.Contains("gameover") || n.Contains("game_over") || n.Contains("death") || n.Contains("skelet") || n.Contains("danse") || n.Contains("macabre") || n.Contains("dead_"))
                    sb.AppendLine(n);
            }
            sb.AppendLine();
            sb.AppendLine("== Rules that matched nothing ==");
            foreach (var r in rules) if (r.Hits == 0) sb.AppendLine(r.Group + " " + r.Pattern);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Log("Report: " + Path.GetFullPath(path));
        }
        catch (Exception ex) { Log("(could not write the report: " + ex.Message + ")"); }
    }

    static int BuildTextures(Dictionary<string, Entry> index, Reader reader, List<Rule> rules, string dataDir, Options o, bool list, bool noCover)
    {
        var normals = new Dictionary<string, string>();
        var selected = Select(index, rules, reader, normals);
        var counts = new SortedDictionary<string, int>();
        foreach (var kv in selected) { int c; counts.TryGetValue(kv.Value, out c); counts[kv.Value] = c + 1; }
        var summary = new List<string>();
        foreach (var kv in counts) summary.Add(kv.Key + " " + kv.Value);
        Log("Textures selected: " + string.Join(", ", summary.ToArray()) + "; normal maps to flatten: " + normals.Count);
        if (InteriorSkipped.Count > 0) Log("Kept snow-free because interior materials use them: " + InteriorSkipped.Count);
        WriteReport(index, rules, selected, normals, list ? Path.Combine(dataDir, "..", "..", "harsh_winter_list.txt") : Path.Combine(dataDir, "..", "build_report.txt"));
        if (list)
        {
            foreach (var kv in selected) Log(string.Format("  {0,-11} {1}{2}", kv.Value, kv.Key, normals.ContainsKey(kv.Key) ? "  (+ " + normals[kv.Key] + ")" : ""));
            Log("Texture rules that matched nothing:");
            foreach (var r in rules) if (r.Hits == 0) Log("  " + r.Group + " " + r.Pattern);
            return 0;
        }
        Log("\n=== Snow textures (" + o.Detail + " detail) -> Mods\\" + ModId);
        foreach (var f in Directory.GetFiles(dataDir, ModId + "*.pak")) if (!f.EndsWith("_life.pak") && !f.EndsWith("_extras.pak") && !f.EndsWith("_loading.pak")) File.Delete(f);
        var levelsDir = Path.Combine(dataDir, "Levels");
        if (Directory.Exists(levelsDir)) Directory.Delete(levelsDir, true);
        var skipped = new List<string>();
        int done = 0, upscaled = 0, flattened = 0, cleared = 0, i = 0;
        using (var set = new PakSet(dataDir, ModId, PakLimit))
        {
            foreach (var kv in selected)
            {
                i++;
                string name = kv.Key, group = kv.Value;
                try
                {
                    if (group == "cover")
                    {
                        if (noCover) continue;
                        var e = index[name];
                        string why;
                        var data = ProcessCover(reader.Read(e), o.Strength, e.Level, out why);
                        if (data == null) { skipped.Add(name + ": " + why); continue; }
                        var ld = Path.Combine(levelsDir, e.Level);
                        Directory.CreateDirectory(ld);
                        using (var lp = new PakWriter(Path.Combine(ld, "level.pak"))) lp.Add(e.Raw, data);
                        done++;
                        continue;
                    }
                    string w;
                    var res = ProcessTexture(name, group, index, reader, o, out w);
                    if (res == null) { skipped.Add(name + ": " + w); continue; }
                    set.AddGroup(res.Files);
                    done++; cleared += res.Cleared; if (res.Upscaled) upscaled++;
                    string nm;
                    if (normals.TryGetValue(name, out nm))
                    {
                        string w2;
                        var nr = ProcessNormal(nm, group, index, reader, NormalAmount(group) * Math.Min(1f, o.Strength), out w2);
                        if (nr != null) { set.AddGroup(nr.Files); flattened++; } else skipped.Add(nm + ": " + w2);
                    }
                    if (done % 25 == 0) Log(string.Format("  {0} textures ({1}/{2})", done, i, selected.Count));
                }
                catch (Exception ex) { skipped.Add(name + ": " + ex.Message + (Environment.GetEnvironmentVariable("HW_DEBUG") == "1" ? "\n" + ex.StackTrace : "")); }
            }
            Log(string.Format("Snow textures: {0} rebuilt ({1} at twice the resolution), {2} normal maps flattened, {3} leaf texels cleared; {4} pak(s).",
                done, upscaled, flattened, cleared, set.Files.Count));
        }
        if (skipped.Count > 0)
        {
            Log("Skipped " + skipped.Count + " (they keep their normal look):");
            foreach (var s in skipped) Log("  " + s);
        }
        return 0;
    }
}
