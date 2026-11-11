// =============================================================================
// Harsh Winter builder - one key per camping object (with "The KCD2 Camping Mod")
//
// The camping mod gives the campfire, bed and chair two keys each (set up, pack
// up). Like Honza Hryz's toggle add-on, this makes it ONE key each: the same key
// sets the object up or packs it away (Harsh Winter's commands DJBToggleCampfire,
// DJBToggleBed, DJBToggleChair, Scripts/HarshWinter/Camp.lua).
//
// The key lists are two whole game files (Libs/Config/defaultProfile.xml and
// keybindSuperactions.xml) that every key mod replaces, so they are written here
// from the files of the mods YOU have: when Unlimited Saving II (or another mod
// with its own key files) is installed, its file is the base and the camping
// keys are merged into it - so both mods' keys keep working. The result is a
// small mod, Mods\zz_harsh_winter_campkeys, named to load after them.
// Nothing is written when the camping mod isn't installed.
// C# 5 only (Windows PowerShell 5.1 compiles it).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

public static class WinterCampKeys
{
    public const string ModId = "zz_harsh_winter_campkeys";
    public static string State = "not built";
    // false in the English edition (helper\language.txt): Czech_xml.pak gets English too
    public static bool Czech = true;
    static readonly string[] Objects = { "Campfire", "Chair", "Bed" };

    static string ReadEntry(string pak, string name)
    {
        try
        {
            using (var z = ZipFile.OpenRead(pak))
                foreach (var e in z.Entries)
                    if (e.FullName.Replace('\\', '/').Equals(name, StringComparison.OrdinalIgnoreCase))
                        using (var s = e.Open()) using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }
        catch (Exception) { }
        return null;
    }

    // Each installed mod's key files: mod folder -> (profile, superactions).
    static List<string[]> Candidates(string modsDir)
    {
        var res = new List<string[]>();
        if (!Directory.Exists(modsDir)) return res;
        foreach (var mod in Directory.GetDirectories(modsDir))
        {
            string mn = Path.GetFileName(mod);
            if (mn.StartsWith("harsh_winter", StringComparison.OrdinalIgnoreCase) || mn.Equals(ModId, StringComparison.OrdinalIgnoreCase)) continue;
            var data = Path.Combine(mod, "Data");
            if (!Directory.Exists(data)) continue;
            foreach (var pak in Directory.GetFiles(data, "*.pak", SearchOption.AllDirectories))
            {
                var prof = ReadEntry(pak, "Libs/Config/defaultProfile.xml");
                if (prof == null) continue;
                res.Add(new[] { mn, prof, ReadEntry(pak, "Libs/Config/keybindSuperactions.xml") ?? "" });
            }
        }
        return res;
    }

    static string Remove(string s, string pattern) { return Regex.Replace(s, pattern, "", RegexOptions.Singleline); }

    // Place -> Toggle; the separate pack-up keys of the three objects go.
    static string Toggle(string s)
    {
        foreach (var o in Objects)
        {
            string lo = o.ToLowerInvariant();
            s = Remove(s, "[ \\t]*<superaction name=\"DJBPackUp" + o + "\"[^/>]*>.*?</superaction>[ \\t]*\\r?\\n");
            s = Remove(s, "[ \\t]*<[^<>]*\"DJBPackUp" + o + "\"[^<>]*/>[ \\t]*\\r?\\n");
            s = s.Replace("DJBPlace" + o, "DJBToggle" + o).Replace("djb_place_" + lo, "djb_toggle_" + lo);
        }
        return s;
    }

    // Brings the camping mod's key entries into another mod's key files.
    static string MergeProfile(string baseXml, string camp)
    {
        if (baseXml.Contains("DJBPlaceCampfire") || baseXml.Contains("DJBToggleCampfire")) return baseXml;
        var lines = new StringBuilder();
        foreach (Match m in Regex.Matches(camp, "[ \\t]*<action name=\"DJB[^>]*/>")) lines.Append(m.Value.Trim()).Append("\r\n\t\t");
        var at = Regex.Match(baseXml, "<actionmap name=\"player\"[^>]*>");
        if (!at.Success) return null;
        int i = at.Index + at.Length;
        return baseXml.Substring(0, i) + "\r\n\t\t<!-- The KCD2 Camping Mod (merged by Harsh Winter) -->\r\n\t\t" + lines.ToString().TrimEnd() + baseXml.Substring(i);
    }

    static string MergeSuper(string baseXml, string camp)
    {
        if (baseXml.Contains("DJBPlaceCampfire") || baseXml.Contains("DJBToggleCampfire")) return baseXml;
        var g = Regex.Match(camp, "<ui_group name=\"djb_camping\"[^>]*/>");
        var blocks = new StringBuilder();
        foreach (Match m in Regex.Matches(camp, "<superaction name=\"DJB\\w+\"[^/>]*>.*?</superaction>", RegexOptions.Singleline)) blocks.Append(m.Value).Append("\r\n\r\n    ");
        var refs = new StringBuilder();
        foreach (Match m in Regex.Matches(camp, "<superaction name=\"DJB\\w+\" />")) refs.Append("\r\n        ").Append(m.Value);
        var s = baseXml;
        var gen = Regex.Match(s, "<ui_group name=\"general\"[^>]*/>");
        if (!gen.Success || !g.Success) return null;
        s = s.Insert(gen.Index + gen.Length, "\r\n    " + g.Value);
        var first = Regex.Match(s, "\\n[ \\t]*<superaction name=\"[^\"]+\"[^/>]*>");
        if (!first.Success) return null;
        s = s.Insert(first.Index + 1, "    " + blocks.ToString());
        var helmet = Regex.Match(s, "<superaction name=\"helmet_action\" />");
        if (!helmet.Success) return null;
        return s.Insert(helmet.Index + helmet.Length, refs.ToString());
    }

    const string Manifest = "<?xml version=\"1.0\" encoding=\"us-ascii\"?>\r\n<kcd_mod>\r\n\t<info>\r\n\t\t<name>Harsh Winter - camping keys (generated)</name>\r\n\t\t<modid>zz_harsh_winter_campkeys</modid>\r\n\t\t<description>One key per camping object (set up / pack up), merged with the key files of the mods you have. Generated by Harsh Winter's Build winter.bat.</description>\r\n\t\t<author>Harsh Winter</author>\r\n\t\t<version>4.5.1</version>\r\n\t\t<created_on>{date}</created_on>\r\n\t</info>\r\n</kcd_mod>\r\n";

    static string Loc(bool cz)
    {
        string[][] rows = {
            new[] { "ui_keybinds_djb_toggle_campfire", "Set up / pack up campfire", "Rozdělat / uhasit a sbalit oheň" },
            new[] { "ui_keybinds_djb_toggle_chair", "Set up / pack up chair", "Postavit / sbalit stoličku" },
            new[] { "ui_keybinds_djb_toggle_bed", "Set up / pack up bed", "Ustlat / sbalit lože" },
        };
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Table>\r\n");
        foreach (var r in rows) sb.Append("  <Row><Cell>" + r[0] + "</Cell><Cell>" + r[1] + "</Cell><Cell>" + (cz ? r[2] : r[1]) + "</Cell></Row>\r\n");
        return sb.Append("</Table>\r\n").ToString();
    }

    public static void Build(string modsDir, bool list)
    {
        var log = WinterTextures.Log;
        var outDir = Path.Combine(modsDir, ModId);
        var cands = Candidates(modsDir);
        string[] camp = null, other = null;
        foreach (var c in cands)
        {
            if (c[1].Contains("DJBPlaceCampfire")) { if (camp == null || c[0].IndexOf("zdjbcamping_mod", StringComparison.OrdinalIgnoreCase) >= 0) camp = c; }
            else if (!c[1].Contains("DJBToggleCampfire")) other = (other == null || c[0].IndexOf("unlimited", StringComparison.OrdinalIgnoreCase) >= 0 || c[0].IndexOf("saving", StringComparison.OrdinalIgnoreCase) >= 0) ? c : other;
        }
        if (camp == null)
        {
            State = "The KCD2 Camping Mod not installed - no camping keys";
            log("Camping keys: " + State + ".");
            if (!list && Directory.Exists(outDir)) { try { Directory.Delete(outDir, true); } catch (Exception) { } }
            return;
        }
        string prof = camp[1], sup = camp[2], baseName = camp[0];
        if (other != null)
        {
            var mp = MergeProfile(other[1], camp[1]);
            var ms = other[2] != "" ? MergeSuper(other[2], camp[2]) : camp[2];
            if (mp != null && ms != null) { prof = mp; sup = ms; baseName = other[0] + " + " + camp[0]; }
            else log("  (could not merge with " + other[0] + "'s key files - the camping mod's are used)");
        }
        prof = Toggle(prof); sup = Toggle(sup);
        State = "one key per object, key files from " + baseName;
        log("Camping keys: " + State + " -> Mods\\" + ModId);
        if (list) return;
        Directory.CreateDirectory(Path.Combine(outDir, "Data"));
        Directory.CreateDirectory(Path.Combine(outDir, "Localization"));
        using (var pak = new PakWriter(Path.Combine(outDir, "Data", ModId + ".pak")))
        {
            pak.AddText("Libs/Config/defaultProfile.xml", prof);
            pak.AddText("Libs/Config/keybindSuperactions.xml", sup);
        }
        foreach (var lang in new[] { "Czech", "English", "German", "French", "Spanish", "Italian", "Polish", "Russian", "Chineses", "Chineset", "Japanese", "Korean", "Turkish", "Ukrainian", "Portuguese" })
            using (var pak = new PakWriter(Path.Combine(outDir, "Localization", lang + "_xml.pak")))
                pak.AddText("text__" + ModId + ".xml", Loc(Czech && lang == "Czech"));
        File.WriteAllText(Path.Combine(outDir, "mod.manifest"), Manifest.Replace("{date}", DateTime.Now.ToString("dd.MM.yyyy")), Encoding.ASCII);
        if (File.Exists(Path.Combine(modsDir, "mod_order.txt")))
            log("  You use Mods\\mod_order.txt: add " + ModId + " to it, AFTER zdjbcamping_mod (and after Unlimited Saving II).");
        foreach (var c in cands)
            if (c[1].Contains("DJBToggleCampfire")) log("  " + c[0] + " (the toggle add-on) isn't needed any more - Harsh Winter does it; you can remove it.");
    }
}
