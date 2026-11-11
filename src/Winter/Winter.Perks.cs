// =============================================================================
// Harsh Winter builder - winter perk icons, checked against YOUR game
//
// The winter perks (Libs/Tables/rpg/perk__harsh_winter.xml) borrow the game's
// own perk icons, so they look like every other perk and don't change with the
// language. Icon names can't be checked without the game, so the builder does it
// here: it lists the game's perk table and icon files, and every winter perk
// whose icon isn't in your game gets an icon the game does have, taken from a
// perk of the same skill (written to Data\harsh_winter_world_extras.pak as
// perk__harsh_winter_world.xml, which replaces just those rows). Perk fields
// the game's perks have and the winter perks don't are listed in
// build_report.txt.
// C# 5 only (Windows PowerShell 5.1 compiles it).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

public static class WinterPerkIcons
{
    public static string State = "not checked";
    public static List<string> Lines = new List<string>();

    static string Attr(string tag, string name)
    {
        var m = Regex.Match(tag, "\\s" + name + "=\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    static string Selector(string tag)
    {
        var s = Attr(tag, "skill_selector");
        if (s != null) return "skill " + s;
        s = Attr(tag, "stat_selector");
        return s != null ? "stat " + s : "none";
    }

    static string OwnTable(string helperDir, string rel)
    {
        var data = Path.Combine(helperDir, "..", "Data");
        var loose = Path.Combine(data, rel.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(loose)) return File.ReadAllText(loose);       // source tree
        var pak = Path.Combine(data, "harsh_winter.pak");
        if (!File.Exists(pak)) return null;
        try
        {
            using (var z = ZipFile.OpenRead(pak))
                foreach (var e in z.Entries)
                    if (e.FullName.Replace('\\', '/').Equals(rel, StringComparison.OrdinalIgnoreCase))
                        using (var s = e.Open()) using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }
        catch (Exception) { }
        return null;
    }

    static string Head(string comment)
    {
        return "<?xml version=\"1.0\" encoding=\"us-ascii\"?>\r\n<!-- Harsh Winter (generated): " + comment + " -->\r\n" +
               "<database xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" name=\"barbora\" xsi:noNamespaceSchemaLocation=\"../database.xsd\">\r\n";
    }

    public static void Run(Dictionary<string, WinterTextures.Entry> index, Func<string, byte[]> read, string helperDir, PakWriter pak)
    {
        var log = WinterTextures.Log;
        Lines.Clear();
        // the game's UI pictures, by name: "<icon id>.dds", and the game's UI suffixes
        // ("<icon id>_ui.dds", like game_over_8_ui.dds; "_icon", "_perk"); .png/.tif too
        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var uiFiles = new List<string>();
        foreach (var k in index.Keys)
        {
            if (!k.StartsWith("libs/ui/", StringComparison.Ordinal)) continue;
            var ext = Path.GetExtension(k);
            if (ext != ".dds" && ext != ".png" && ext != ".tif" && ext != ".tga") continue;
            uiFiles.Add(k);
            var stem = Path.GetFileNameWithoutExtension(k);
            bool inIcons = k.IndexOf("icon", StringComparison.Ordinal) >= 0 || k.IndexOf("perk", StringComparison.Ordinal) >= 0;
            foreach (var suf in new[] { "", "_ui", "_icon", "_perk", "_big", "_small" })
            {
                if (suf != "" && !stem.EndsWith(suf, StringComparison.Ordinal)) continue;
                var id = stem.Substring(0, stem.Length - suf.Length);
                string had;
                // prefer a picture in an icon/perk folder
                if (!icons.TryGetValue(id, out had) || (inIcons && had.IndexOf("icon", StringComparison.Ordinal) < 0 && had.IndexOf("perk", StringComparison.Ordinal) < 0)) icons[id] = k;
            }
        }
        // the game's perks
        var vanilla = new List<string>();
        foreach (var k in index.Keys)
            if (Regex.IsMatch(k, "^libs/tables/rpg/perk(__[^/]+)?\\.xml$") && k.IndexOf("harsh_winter", StringComparison.Ordinal) < 0)
            {
                string t;
                try { t = Encoding.UTF8.GetString(read(k)); } catch (Exception) { continue; }
                foreach (Match m in Regex.Matches(t, "<perk\\s[^>]*>")) vanilla.Add(m.Value);
            }
        var bySel = new Dictionary<string, List<string>>();
        var anyIcon = new List<string>();
        var fields = new Dictionary<string, int>();
        var dirs = new Dictionary<string, int>();
        foreach (var v in vanilla)
        {
            foreach (Match a in Regex.Matches(v, "\\s([a-z_]+)=\"")) { int n; fields.TryGetValue(a.Groups[1].Value, out n); fields[a.Groups[1].Value] = n + 1; }
            var ic = Attr(v, "icon_id");
            string path;
            if (ic == null || !icons.TryGetValue(ic, out path)) continue;
            var dir = Path.GetDirectoryName(path).Replace('\\', '/');
            int c; dirs.TryGetValue(dir, out c); dirs[dir] = c + 1;
            var sel = Selector(v);
            if (!bySel.ContainsKey(sel)) bySel[sel] = new List<string>();
            if (!bySel[sel].Contains(ic)) bySel[sel].Add(ic);
            if (!anyIcon.Contains(ic)) anyIcon.Add(ic);
        }
        string iconDir = null; int best = 0;
        foreach (var kv in dirs) if (kv.Value > best) { best = kv.Value; iconDir = kv.Key; }

        var own = OwnTable(helperDir, "Libs/Tables/rpg/perk__harsh_winter.xml");
        if (own == null) { State = "the mod's perk table wasn't found - nothing checked"; log("Winter perk icons: " + State + "."); return; }
        var ours = new List<string>();
        foreach (Match m in Regex.Matches(own, "<perk\\s[^>]*>")) ours.Add(m.Value);
        if (anyIcon.Count == 0)
        {
            State = "the game's perk icons weren't found (" + vanilla.Count + " perks in its tables, " + uiFiles.Count + " UI pictures) - the winter perks keep their icons";
            log("Winter perk icons: " + State + ".");
            // what the game has, so the icon naming can be found from this report
            int shown = 0;
            foreach (var v in vanilla)
            {
                var ic = Attr(v, "icon_id");
                if (ic == null || shown >= 4) continue;
                shown++;
                var hits = new List<string>();
                foreach (var k in index.Keys) if (k.IndexOf(ic.ToLowerInvariant(), StringComparison.Ordinal) >= 0 && hits.Count < 4) hits.Add(k);
                Lines.Add("game perk " + (Attr(v, "perk_name") ?? "?") + " uses icon_id \"" + ic + "\": " + (hits.Count > 0 ? string.Join(", ", hits.ToArray()) : "no file with that name in the game's paks"));
            }
            int n = 0;
            foreach (var k in uiFiles) if ((k.IndexOf("perk", StringComparison.Ordinal) >= 0 || k.IndexOf("skill", StringComparison.Ordinal) >= 0) && n++ < 12) Lines.Add("UI picture: " + k);
            foreach (var k in index.Keys) if (k.StartsWith("libs/ui/", StringComparison.Ordinal) && (k.EndsWith(".gfx") || k.EndsWith(".swf")) && (k.IndexOf("perk", StringComparison.Ordinal) >= 0 || k.IndexOf("skill", StringComparison.Ordinal) >= 0 || k.IndexOf("icon", StringComparison.Ordinal) >= 0) && n++ < 20) Lines.Add("UI movie: " + k);
            return;
        }
        // in the game's own icon folder only, so a same-named picture elsewhere doesn't count
        Func<string, bool> has = delegate (string ic)
        {
            string p;
            return ic != null && icons.TryGetValue(ic, out p) && (iconDir == null || Path.GetDirectoryName(p).Replace('\\', '/') == iconDir);
        };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in ours) { var ic = Attr(o, "icon_id"); if (has(ic)) used.Add(ic); }
        var changed = new List<string>();
        var swap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int ok = 0;
        foreach (var o in ours)
        {
            var name = Attr(o, "perk_name") ?? "?";
            var ic = Attr(o, "icon_id");
            if (has(ic)) { ok++; Lines.Add(name + ": " + ic + " - in the game"); continue; }
            List<string> pool;
            if (!bySel.TryGetValue(Selector(o), out pool) || pool.Count == 0) pool = anyIcon;
            string pick = null;
            foreach (var c in pool) if (!used.Contains(c)) { pick = c; break; }
            if (pick == null) pick = pool[Math.Abs(name.GetHashCode()) % pool.Count];
            used.Add(pick);
            if (ic != null) swap[ic] = pick;
            changed.Add(Regex.Replace(o, "\\sicon_id=\"[^\"]*\"", " icon_id=\"" + pick + "\""));
            Lines.Add(name + ": " + (ic ?? "(none)") + " is not in your game -> " + pick + " (a " + Selector(o) + " perk's icon)");
        }
        // perk fields the game's perks use (on most of them) that the winter perks lack
        var have = new HashSet<string>();
        foreach (var o in ours) foreach (Match a in Regex.Matches(o, "\\s([a-z_]+)=\"")) have.Add(a.Groups[1].Value);
        var missing = new List<string>();
        foreach (var kv in fields) if (!have.Contains(kv.Key) && kv.Value * 2 >= vanilla.Count) missing.Add(kv.Key);
        if (missing.Count > 0) Lines.Add("fields most of the game's perks have and the winter perks don't: " + string.Join(", ", missing.ToArray()));
        State = ok + " of " + ours.Count + " winter perk icons are in your game" + (changed.Count > 0 ? ", " + changed.Count + " replaced by the game's own" : "") +
                (iconDir != null ? " (icons in " + iconDir + ")" : "");
        log("Winter perk icons: " + State + ".");
        if (pak == null || changed.Count == 0) return;
        pak.AddText("Libs/Tables/rpg/perk__" + WinterTextures.ModId + ".xml",
            Head("winter perks whose icon isn't in this game, with one it has.") + "\t<perks version=\"1\">\r\n\t\t" + string.Join("\r\n\t\t", changed.ToArray()) + "\r\n\t</perks>\r\n</database>\r\n");
        // the perks' buffs show the same icon
        var rows = new List<string>();
        foreach (var bt in new[] { "buff__harsh_winter_perks.xml", "buff__harsh_winter.xml" })
        {
            var buffs = OwnTable(helperDir, "Libs/Tables/rpg/" + bt);
            if (buffs == null) continue;
            foreach (Match m in Regex.Matches(buffs, "<buff\\s[^>]*>"))
            {
                var ic = Attr(m.Value, "icon_id");
                string to;
                if (ic != null && swap.TryGetValue(ic, out to)) rows.Add(Regex.Replace(m.Value, "\\sicon_id=\"[^\"]*\"", " icon_id=\"" + to + "\""));
            }
        }
        if (rows.Count > 0)
            pak.AddText("Libs/Tables/rpg/buff__" + WinterTextures.ModId + "_perks.xml",
                Head("the winter perks' buffs, with the replaced icons.") + "\t<buffs version=\"1\">\r\n\t\t" + string.Join("\r\n\t\t", rows.ToArray()) + "\r\n\t</buffs>\r\n</database>\r\n");
    }

    public static void Report(StringBuilder sb)
    {
        sb.AppendLine("== Winter perk icons ==");
        sb.AppendLine(State);
        foreach (var l in Lines) sb.AppendLine("  " + l);
        sb.AppendLine();
    }
}
