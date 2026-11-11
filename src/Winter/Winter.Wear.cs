// Harsh Winter builder - what Henry wears (table patch for armor types + buffs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// =============================================================================
// KCD2 gives an armor type an "equip buff": a buff that is on while an item of
// that type is worn (Libs/Tables/item/armor_type.xml, EquipBuffId; the game's
// gloves use it for +2 craftsmanship). The builder gives every armor type in
// winter_wear.txt such a buff of its own. The buff carries the type's warmth
// as a small number on derived stats Henry never uses (signal-* lines), which
// the mod reads back with soul:GetDerivedStat - that is how the cold survival
// knows his layers, gloves, head covering and footwear.
//
// A type that already has a buff keeps that buff's parameters and
// implementation in the new one, so nothing the game does with it is lost.
// Everything comes from YOUR game's tables, read on your PC.
// =============================================================================
public static class WinterWear
{
    public static int LastTypes = -1;
    // Lua table for the world script: worn-buff id -> { points, hands, head, feet }.
    public static string LastLua = "{}";
    class Wear { public string Name; public double Points; public bool Hands, Head, Feet; }

    static Dictionary<string, string> Attrs(string s)
    {
        var d = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(s, "([A-Za-z_]+)=\"([^\"]*)\"")) d[m.Groups[1].Value] = m.Groups[2].Value;
        return d;
    }

    // A stable GUID from a name (the same on every PC and every build).
    public static string Guid(string name)
    {
        // Two FNV-1a 64-bit hashes (different seeds) = 16 stable bytes; no crypto library needed.
        var bytes = Encoding.UTF8.GetBytes("harsh_winter_wear:" + name);
        var h = new byte[16];
        for (int part = 0; part < 2; part++)
        {
            ulong x = part == 0 ? 14695981039346656037UL : 1099511628211UL * 31;
            foreach (var b in bytes) { x ^= b; x *= 1099511628211UL; }
            x ^= x >> 29; x *= 0xBF58476D1CE4E5B9UL; x ^= x >> 32;
            for (int i = 0; i < 8; i++) h[part * 8 + i] = (byte)(x >> (8 * i));
        }
        h[6] = (byte)((h[6] & 0x0F) | 0x40); h[8] = (byte)((h[8] & 0x3F) | 0x80);
        var hex = BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
        return hex.Substring(0, 8) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4) + "-" + hex.Substring(16, 4) + "-" + hex.Substring(20, 12);
    }

    static string Esc(string s) { return s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;"); }

    public static int Build(IEnumerable<string> names, Func<string, byte[]> read, string rulesFile, PakWriter outPak, bool list)
    {
        var log = WinterTextures.Log;
        LastTypes = 0;
        if (!File.Exists(rulesFile)) { log("(helper\\winter_wear.txt missing: Henry's clothing warmth comes from his equipped weight)"); return 0; }
        var wear = new Dictionary<string, Wear>(StringComparer.OrdinalIgnoreCase);
        var signal = new Dictionary<string, string> { { "warmth", "dai" }, { "hands", "cnp" }, { "head", "cbp" }, { "feet", "csp" } };
        foreach (var raw in File.ReadAllLines(rulesFile))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var p = Regex.Split(line, "\\s+");
            if (p[0].StartsWith("signal-", StringComparison.OrdinalIgnoreCase) && p.Length > 1) { signal[p[0].Substring(7).ToLowerInvariant()] = p[1]; continue; }
            double pts;
            if (p.Length < 2 || !double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pts)) continue;
            var w = new Wear { Name = p[0], Points = pts };
            for (int i = 2; i < p.Length; i++)
            {
                var f = p[i].ToLowerInvariant();
                if (f == "hands") w.Hands = true; else if (f == "head") w.Head = true; else if (f == "feet") w.Feet = true;
            }
            wear[w.Name] = w;
        }
        var sorted = new List<string>(names); sorted.Sort(StringComparer.Ordinal);
        // ---- the game's buffs (to keep what an existing equip buff does)
        var buffs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        string buffVersion = "1";
        foreach (var n in sorted)
        {
            if (!Regex.IsMatch(n, "^libs/tables/rpg/buff(__[^/]*)?\\.xml$") || n.Contains("harsh_winter")) continue;
            string text;
            try { text = Encoding.UTF8.GetString(read(n)); } catch (Exception) { continue; }
            var vm = Regex.Match(text, "<buffs\\s+version=\"(\\d+)\""); if (vm.Success) buffVersion = vm.Groups[1].Value;
            foreach (Match m in Regex.Matches(text, "<buff\\s([^>]*?)/?>"))
            {
                var a = Attrs(m.Groups[1].Value);
                string id; if (a.TryGetValue("buff_id", out id)) buffs[id] = a;
            }
        }
        // ---- armor types
        var rows = new List<Dictionary<string, string>>();
        string atVersion = "1";
        foreach (var n in sorted)
        {
            if (!Regex.IsMatch(n, "^libs/tables/item/armor_type(__[^/]*)?\\.xml$") || n.Contains("harsh_winter")) continue;
            string text;
            try { text = Encoding.UTF8.GetString(read(n)); } catch (Exception) { continue; }
            var vm = Regex.Match(text, "<armor_types\\s+version=\"(\\d+)\""); if (vm.Success) atVersion = vm.Groups[1].Value;
            foreach (Match m in Regex.Matches(text, "<armor_type\\s([^>]*?)/?>"))
            {
                var a = Attrs(m.Groups[1].Value);
                string nm; if (!a.TryGetValue("Name", out nm) || !a.ContainsKey("Id")) continue;
                rows.RemoveAll(r => r["Id"] == a["Id"]);    // a later patch file wins
                rows.Add(a);
            }
        }
        if (rows.Count == 0) { log("No armor_type table found - Henry's clothing warmth comes from his equipped weight."); return 0; }

        var at = new StringBuilder(); var bf = new StringBuilder();
        at.Append("<?xml version=\"1.0\" encoding=\"us-ascii\"?>\r\n<!-- Harsh Winter: a hidden 'worn' buff for each armor type (clothing warmth). Generated on your PC. -->\r\n");
        at.Append("<database xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" name=\"barbora\" xsi:noNamespaceSchemaLocation=\"../database.xsd\">\r\n\t<armor_types version=\"" + atVersion + "\">\r\n");
        bf.Append("<?xml version=\"1.0\" encoding=\"us-ascii\"?>\r\n<!-- Harsh Winter: the 'worn' buffs behind armor_type__harsh_winter_world.xml. Generated on your PC. -->\r\n");
        bf.Append("<database xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" name=\"barbora\" xsi:noNamespaceSchemaLocation=\"../database.xsd\">\r\n\t<buffs version=\"" + buffVersion + "\">\r\n");
        int tagged = 0; var kept = new List<string>(); var missing = new List<string>();
        var lua = new StringBuilder("{ ");
        foreach (var r in rows)
        {
            Wear w;
            if (!wear.TryGetValue(r["Name"], out w)) continue;
            string old; r.TryGetValue("EquipBuffId", out old);
            Dictionary<string, string> ob = null;
            if (!string.IsNullOrEmpty(old) && !buffs.TryGetValue(old, out ob)) { missing.Add(r["Name"]); continue; }   // can't keep what it did: leave it
            var parms = new List<string>();
            string vp; if (ob != null && ob.TryGetValue("buff_params", out vp) && vp.Trim().Length > 0) parms.Add(vp.Trim().TrimEnd(','));
            parms.Add(signal["warmth"] + "+" + (w.Points * 1000).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (w.Hands) parms.Add(signal["hands"] + "+1000");
            if (w.Head) parms.Add(signal["head"] + "+1000");
            if (w.Feet) parms.Add(signal["feet"] + "+1000");
            var id = Guid(r["Name"]);
            Func<string, string, string> pick = delegate (string k, string def) { string v; return ob != null && ob.TryGetValue(k, out v) && v.Length > 0 ? v : def; };
            bf.Append("\t\t<buff buff_class_id=\"" + pick("buff_class_id", "12") + "\" buff_exclusivity_id=\"" + pick("buff_exclusivity_id", "1") + "\" buff_id=\"" + id +
                "\" buff_lifetime_id=\"" + pick("buff_lifetime_id", "0") + "\" buff_name=\"harsh_winter_wear_" + r["Name"].ToLowerInvariant() + "\" buff_params=\"" + Esc(string.Join(",", parms.ToArray())) +
                "\" duration=\"" + pick("duration", "-1") + "\" implementation=\"" + pick("implementation", "Cpp:EquippableItemWeighted") + "\" is_persistent=\"" + pick("is_persistent", "true") + "\" />\r\n");
            var sb = new StringBuilder("\t\t<armor_type");
            var keys = new List<string>(r.Keys); keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) if (k != "EquipBuffId") sb.Append(" " + k + "=\"" + Esc(r[k]) + "\"");
            sb.Append(" EquipBuffId=\"" + id + "\" />\r\n");
            at.Append(sb.ToString());
            if (ob != null) kept.Add(r["Name"]);
            tagged++;
            lua.Append("[\"" + id + "\"] = { " + w.Points.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + (w.Hands ? "true" : "false") + ", " +
                (w.Head ? "true" : "false") + ", " + (w.Feet ? "true" : "false") + " }, ");
        }
        at.Append("\t</armor_types>\r\n</database>\r\n");
        bf.Append("\t</buffs>\r\n</database>\r\n");
        log(string.Format("Clothing warmth: {0} armor types tagged ({1} keep their own buff too){2}.", tagged, kept.Count,
            missing.Count > 0 ? "; left alone (their buff isn't in the tables): " + string.Join(", ", missing.ToArray()) : ""));
        if (list || tagged == 0) return 0;
        LastTypes = tagged;
        LastLua = lua.Append("}").ToString();
        outPak.AddText("Libs/Tables/item/armor_type__" + WinterTextures.ModId + ".xml", at.ToString());
        outPak.AddText("Libs/Tables/rpg/buff__" + WinterTextures.ModId + ".xml", bf.ToString());
        return 0;
    }
}
