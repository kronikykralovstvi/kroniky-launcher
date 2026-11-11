// Harsh Winter builder - NPC winter clothing (table patch for clothing presets).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// =============================================================================
// NPC winter clothing: a table patch for the game's clothing presets.
//
// Every NPC outfit in KCD2 is a clothing preset: a list of item GUIDs
// (Libs/Tables/item/clothing_preset.xml). The builder adds winter layers to
// each preset, picking for each slot in winter_clothing.txt (head, coat, hands,
// neck) the first category that
//   * the preset doesn't already cover (an NPC with a hood keeps it),
//   * is worn by enough vanilla outfits of the same gender, and
//   * appears together, in Warhorse's own outfits, with EVERY garment this
//     outfit already has (at least min-together times).
// The last rule is the safety net: only layer combinations the game already
// uses are created, so nothing clips through armour, and monks' habits or
// noble gowns don't get a peasant's coat. The item within a category is the
// one nearest in price to the outfit's own items (a beggar gets a cheap hood,
// a burgher a fine one), varied per outfit.
//
// The patch lists each changed outfit's full item list (original + winter),
// so it reads the same whether the game appends to or replaces patched lists.
// =============================================================================
public static class WinterClothing
{
    public static int LastChanged = -1;
    class Item { public string Id, Name, Cat, Text; public double Price; public bool Generic; }
    class Preset { public string Id, Name, Gender; public List<string> Items = new List<string>(); public List<string> Cats = new List<string>(); }
    class Want { public string Slot, Gender; public string[] Cats; }
    class Upgrade { public string Gender; public string[] From, To; }

    static Dictionary<string, string> Attrs(string s)
    {
        var d = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(s, "([A-Za-z_]+)=\"([^\"]*)\"")) d[m.Groups[1].Value] = m.Groups[2].Value;
        return d;
    }

    public static string Category(string clothing)
    {
        var c = (clothing ?? "").ToLowerInvariant();
        var m = Regex.Match(c, "^([a-z_]+?)(?=\\d|_m\\d|_m[a-z]|$)");
        return m.Success ? m.Groups[1].Value : c;
    }

    static string Get(Dictionary<string, string> d, string k) { string v; return d.TryGetValue(k, out v) ? v : ""; }

    public static int Build(IEnumerable<string> names, Func<string, byte[]> read, string rulesFile, PakWriter outPak, bool list)
    {
        var log = WinterTextures.Log;
        // ---- rules
        var wants = new List<Want>();
        var upgrades = new List<Upgrade>();
        var skipPreset = new List<string>(); var skipItem = new List<string>();
        int minTogether = 3, minWorn = 3;
        foreach (var raw in File.ReadAllLines(rulesFile))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var p = Regex.Split(line.ToLowerInvariant(), "\\s+");
            if (p[0] == "skip-preset") { for (int i = 1; i < p.Length; i++) skipPreset.Add(p[i]); }
            else if (p[0] == "skip-item") { for (int i = 1; i < p.Length; i++) skipItem.Add(p[i]); }
            else if (p[0] == "min-together" && p.Length > 1) int.TryParse(p[1], out minTogether);
            else if (p[0] == "min-worn" && p.Length > 1) int.TryParse(p[1], out minWorn);
            else if (p[0] == "upgrade" && p.Length >= 5 && Array.IndexOf(p, "->") > 2)
            {
                int arrow = Array.IndexOf(p, "->");
                var from = new string[arrow - 2]; Array.Copy(p, 2, from, 0, from.Length);
                var to = new string[p.Length - arrow - 1]; Array.Copy(p, arrow + 1, to, 0, to.Length);
                upgrades.Add(new Upgrade { Gender = p[1], From = from, To = to });
            }
            else if (p.Length >= 3) { var cats = new string[p.Length - 2]; Array.Copy(p, 2, cats, 0, cats.Length); wants.Add(new Want { Slot = p[0], Gender = p[1], Cats = cats }); }
        }
        // ---- items
        var items = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        var presets = new List<Preset>();
        string version = "2";
        var sorted = new List<string>(names); sorted.Sort(StringComparer.Ordinal);
        foreach (var n in sorted)
        {
            if (!Regex.IsMatch(n, "^libs/tables/item/item(__[^/]*)?\\.xml$")) continue;
            string text;
            try { text = Encoding.UTF8.GetString(read(n)); } catch (Exception) { continue; }
            foreach (Match m in Regex.Matches(text, "<(Armor|Hood|Helmet)\\s([^>]*?)/?>"))
            {
                var a = Attrs(m.Groups[2].Value);
                var id = Get(a, "Id");
                if (id.Length == 0 || items.ContainsKey(id)) continue;
                double price; double.TryParse(Get(a, "Price"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out price);
                // Generic variants are numbered (Hood07_m04); named ones (Gloves05_mRadzig) belong to one character.
                bool generic = Regex.IsMatch(Get(a, "Clothing"), "_m\\d+$", RegexOptions.IgnoreCase);
                items[id] = new Item { Id = id, Name = Get(a, "Name"), Cat = Category(Get(a, "Clothing")), Price = price, Generic = generic,
                    Text = (Get(a, "Name") + " " + Get(a, "UIInfo") + " " + Get(a, "Clothing")).ToLowerInvariant() };
            }
        }
        // ---- presets
        foreach (var n in sorted)
        {
            if (!Regex.IsMatch(n, "^libs/tables/item/clothing_preset(__[^/]*)?\\.xml$")) continue;
            if (n.Contains("harsh_winter")) continue;
            string text;
            try { text = Encoding.UTF8.GetString(read(n)); } catch (Exception) { continue; }
            var vm = Regex.Match(text, "<clothing_presets\\s+version=\"(\\d+)\"");
            if (vm.Success) version = vm.Groups[1].Value;
            foreach (Match m in Regex.Matches(text, "<clothing_preset\\s([^>]*?)(/>|>(.*?)</clothing_preset>)", RegexOptions.Singleline))
            {
                var a = Attrs(m.Groups[1].Value);
                var pr = new Preset { Id = Get(a, "clothing_preset_id"), Name = Get(a, "clothing_preset_name"), Gender = Get(a, "gender").ToLowerInvariant() };
                if (pr.Id.Length == 0) continue;
                foreach (Match g in Regex.Matches(m.Groups[3].Value, "<Guid>\\s*([^<\\s]+)\\s*</Guid>")) pr.Items.Add(g.Groups[1].Value);
                foreach (var it in pr.Items) { Item x; if (items.TryGetValue(it, out x) && !pr.Cats.Contains(x.Cat)) pr.Cats.Add(x.Cat); }
                presets.Add(pr);
            }
        }
        log(string.Format("Clothing: {0} items, {1} outfits read.", items.Count, presets.Count));
        if (items.Count == 0 || presets.Count == 0) { log("No clothing tables found - NPC clothing skipped."); return 0; }

        // ---- statistics from Warhorse's own outfits
        var together = new Dictionary<string, int>();
        var worn = new Dictionary<string, int>();                    // gender|cat -> outfits
        var wornBy = new Dictionary<string, List<Item>>();           // gender|cat -> items worn by that gender
        var seen = new HashSet<string>();
        foreach (var pr in presets)
        {
            for (int i = 0; i < pr.Cats.Count; i++)
            {
                Inc(worn, pr.Gender + "|" + pr.Cats[i]);
                for (int j = i + 1; j < pr.Cats.Count; j++) Inc(together, Pair(pr.Cats[i], pr.Cats[j]));
            }
            foreach (var it in pr.Items)
            {
                Item x;
                if (!items.TryGetValue(it, out x) || !seen.Add(pr.Gender + "|" + it)) continue;
                List<Item> l; var k = pr.Gender + "|" + x.Cat;
                if (!wornBy.TryGetValue(k, out l)) { l = new List<Item>(); wornBy[k] = l; }
                if (x.Generic && !Matches(x.Text, skipItem)) l.Add(x);
            }
        }

        // ---- decide
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"us-ascii\"?>\r\n");
        sb.Append("<!-- Harsh Winter: winter layers for NPC outfits. Generated on your PC from your own game tables by helper\\Build winter.bat.\r\n");
        sb.Append("     Each outfit lists its original items followed by the added winter items. -->\r\n");
        sb.Append("<database xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" name=\"barbora\" xsi:noNamespaceSchemaLocation=\"../database.xsd\">\r\n");
        sb.Append("\t<clothing_presets version=\"" + version + "\">\r\n");
        int changed = 0, skippedPresets = 0;
        var added = new SortedDictionary<string, int>();
        var samples = new List<string>();
        foreach (var pr in presets)
        {
            if ((pr.Gender != "male" && pr.Gender != "female") || pr.Cats.Count == 0 || Matches(pr.Name.ToLowerInvariant(), skipPreset)) { skippedPresets++; continue; }
            var cats = new List<string>(pr.Cats);
            var extra = new List<Item>();
            double wealth = Wealth(pr, items);
            // Upgrades first: summer pieces swapped for their winter kind (shoes -> boots).
            var outItems = new List<string>(pr.Items);
            foreach (var up in upgrades)
            {
                if (up.Gender != pr.Gender && up.Gender != "any") continue;
                string fromCat = null;
                foreach (var f in up.From) if (cats.Contains(f)) { fromCat = f; break; }
                if (fromCat == null) continue;
                bool already = false;
                foreach (var t in up.To) if (cats.Contains(t)) already = true;
                if (already) continue;
                foreach (var t in up.To)
                {
                    List<Item> cand;
                    if (Count(worn, pr.Gender + "|" + t) < minWorn || !wornBy.TryGetValue(pr.Gender + "|" + t, out cand) || cand.Count == 0) continue;
                    bool ok = true;
                    foreach (var have in cats) if (have != fromCat && Count(together, Pair(have, t)) < minTogether) { ok = false; break; }
                    if (!ok) continue;
                    var pick = Choose(cand, wealth, pr.Id + "up" + t);
                    outItems.RemoveAll(it => { Item x; return items.TryGetValue(it, out x) && x.Cat == fromCat; });
                    cats.Remove(fromCat); cats.Add(t);
                    extra.Add(pick);
                    int k; added.TryGetValue("upgrade " + fromCat + "->" + t, out k); added["upgrade " + fromCat + "->" + t] = k + 1;
                    break;
                }
            }
            foreach (var w in wants)
            {
                if (w.Gender != pr.Gender && w.Gender != "any") continue;
                bool covered = false;
                foreach (var c in w.Cats) if (cats.Contains(c)) covered = true;
                if (covered) continue;
                foreach (var c in w.Cats)
                {
                    List<Item> cand;
                    if (Count(worn, pr.Gender + "|" + c) < minWorn || !wornBy.TryGetValue(pr.Gender + "|" + c, out cand) || cand.Count == 0) continue;
                    bool ok = true;
                    foreach (var have in cats) if (Count(together, Pair(have, c)) < minTogether) { ok = false; break; }
                    if (!ok) continue;
                    var pick = Choose(cand, wealth, pr.Id + w.Slot);
                    extra.Add(pick); cats.Add(c);
                    int k; added.TryGetValue(w.Slot + " " + c, out k); added[w.Slot + " " + c] = k + 1;
                    break;
                }
            }
            if (extra.Count == 0) continue;
            changed++;
            sb.Append("\t\t<clothing_preset clothing_preset_id=\"" + pr.Id + "\" clothing_preset_name=\"" + pr.Name + "\">\r\n\t\t\t<Items>\r\n");
            foreach (var it in outItems) sb.Append("\t\t\t\t<Guid>" + it + "</Guid>\r\n");
            var names2 = new List<string>();
            foreach (var x in extra) { sb.Append("\t\t\t\t<Guid>" + x.Id + "</Guid>\r\n"); names2.Add(x.Name); }
            sb.Append("\t\t\t</Items>\r\n\t\t</clothing_preset>\r\n");
            if (samples.Count < 25) samples.Add("  " + pr.Name + " (" + pr.Gender + "): + " + string.Join(", ", names2.ToArray()));
        }
        sb.Append("\t</clothing_presets>\r\n</database>\r\n");
        var parts = new List<string>();
        foreach (var kv in added) parts.Add(kv.Key + " " + kv.Value);
        log(string.Format("Winter clothing for {0} of {1} outfits ({2} skipped: tests, previews, animals, Henry). Added: {3}",
            changed, presets.Count, skippedPresets, string.Join(", ", parts.ToArray())));
        if (list) { log("Examples:"); foreach (var s in samples) log(s); return 0; }

        LastChanged = changed;
        outPak.AddText("Libs/Tables/item/clothing_preset__" + WinterTextures.ModId + ".xml", sb.ToString());
        log("NPC winter clothing: table patch written.");
        return 0;
    }

    static string Pair(string a, string b) { return string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a; }
    static void Inc(Dictionary<string, int> d, string k) { int v; d.TryGetValue(k, out v); d[k] = v + 1; }
    static int Count(Dictionary<string, int> d, string k) { int v; return d.TryGetValue(k, out v) ? v : 0; }
    static bool Matches(string text, List<string> words) { foreach (var w in words) if (text.Contains(w)) return true; return false; }

    static double Wealth(Preset pr, Dictionary<string, Item> items)
    {
        var prices = new List<double>();
        foreach (var it in pr.Items) { Item x; if (items.TryGetValue(it, out x) && x.Price > 0) prices.Add(x.Price); }
        if (prices.Count == 0) return 500;
        prices.Sort();
        return prices[prices.Count / 2];
    }

    // Nearest in price to the outfit, varied among the 4 nearest by the outfit id.
    static Item Choose(List<Item> cand, double wealth, string seed)
    {
        var l = new List<Item>(cand);
        double lw = Math.Log(wealth + 1);
        l.Sort(delegate (Item a, Item b)
        {
            int c = Math.Abs(Math.Log(a.Price + 1) - lw).CompareTo(Math.Abs(Math.Log(b.Price + 1) - lw));
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        });
        int n = Math.Min(4, l.Count);
        uint h = 2166136261;
        foreach (var ch in seed) { h ^= ch; h *= 16777619; }
        return l[(int)(h % (uint)n)];
    }
}
