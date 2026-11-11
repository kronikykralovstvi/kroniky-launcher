// =============================================================================
// Harsh Winter builder - snow footprints at every footstep.
//
// The game plays a "material effect" for every footstep of the player, NPCs
// and horses, chosen by the surface stepped on (Libs/MaterialEffects/FXLibs/
// footsteps_*.xml: sound + optional particles/decals). The builder reads your
// game's own versions of those files and, for every snowy outdoor surface in
// winter_footprints.txt, adds
//   * a snow footprint (or hoofprint) decal at real size (a 27 cm shoe, a
//     14 cm hoof): compacted snow with cavity shading and a soft 3 cm hollow,
//     feathered into the snow, fading after a while; left/right shoes and two
//     horseshoes picked at random,
//   * a small puff of kicked-up snow,
//   * optionally the gravel footstep sound instead of grass/soil/mud, which is
//     the game's closest thing to the crunch of snow.
// Everything else in those files is kept exactly as the game has it.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public static class WinterFootprints
{
    public static int LastEffects;
    public static string LastMode = "script";

    class Rules
    {
        public List<string> Snowy = new List<string>();
        public string Crunch = "gravel";
        public string Prints = "script";   // script: the main mod lays depth-matched prints; engine: fixed prints here
        public Dictionary<string, int> Life = new Dictionary<string, int> { { "player", 150 }, { "npc", 90 }, { "horse", 150 } };
        public float Human = 0.18f, Hoof = 0.13f;   // decal radius (half its width) in metres
    }

    static Rules Read(string file)
    {
        var r = new Rules();
        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var p = Regex.Split(line.ToLowerInvariant(), "\\s+");
            if (p[0] == "snowy") { for (int i = 1; i < p.Length; i++) r.Snowy.Add(p[i]); }
            else if (p[0] == "crunch-sound" && p.Length > 1) r.Crunch = p[1];
            else if (p[0] == "prints" && p.Length > 1 && (p[1] == "script" || p[1] == "engine")) r.Prints = p[1];
            else if (p[0] == "lifetime" && p.Length > 2) { int v; if (int.TryParse(p[2], out v)) r.Life[p[1]] = v; }
            else if (p[0] == "size" && p.Length > 2)
            {
                float v; if (float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) { if (p[1] == "human") r.Human = v; else if (p[1] == "hoof") r.Hoof = v; }
            }
        }
        return r;
    }

    // All surfaces the game's footstep libraries know (longest first, for name parsing).
    static readonly string[] Surfaces = { "woodstairs", "grass_tall", "grassdry", "mudwater", "bushes", "forest", "fabric", "gravel", "thatch", "grass", "water", "rock", "soil", "wood", "mud" };

    // "jmp_grassdryq" -> grassdry, "forest_canter" -> forest, "mudd" -> mud.
    public static string SurfaceOf(string effect, out bool jump)
    {
        var n = effect.ToLowerInvariant();
        jump = n.StartsWith("jmp_");
        if (jump) n = n.Substring(4);
        n = Regex.Replace(n, "_(canter|gallop|trot)$", "");
        foreach (var s in Surfaces)
            if (n.StartsWith(s))
            {
                var rest = n.Substring(s.Length);
                if (rest == "" || rest == "q" || rest == "d" || rest == "dq") return s;
            }
        return null;
    }

    static readonly string[] Files = { "footsteps_player.xml", "footsteps_npc.xml", "footsteps_horse_player.xml", "footsteps_horse_npc.xml" };

    public static int Build(IEnumerable<string> names, Func<string, byte[]> read, string rulesFile, PakWriter outPak, bool list)
    {
        var log = WinterTextures.Log;
        if (!File.Exists(rulesFile)) { log("Footprint rules not found: " + rulesFile); return 1; }
        var rules = Read(rulesFile);
        LastMode = rules.Prints;
        if (rules.Snowy.Count == 0) { log("  footprints: off (no \"snowy\" surfaces in winter_footprints.txt)"); LastEffects = 0; return 0; }
        var have = new Dictionary<string, string>();
        foreach (var n in names)
            if (n.StartsWith("libs/materialeffects/fxlibs/")) have[n.Substring("libs/materialeffects/fxlibs/".Length)] = n;
        int total = 0;
        foreach (var file in Files)
        {
            string key;
            if (!have.TryGetValue(file, out key)) { log("  footprints: the game has no " + file + " - skipped"); continue; }
            string text;
            try { text = Encoding.UTF8.GetString(read(key)); } catch (Exception ex) { log("  footprints: can't read " + file + ": " + ex.Message); continue; }
            bool horse = file.Contains("horse");
            string who = horse ? "horse" : (file.Contains("player") ? "player" : "npc");
            var triggers = new HashSet<string>();
            foreach (Match m in Regex.Matches(text, "trigger=\"([^\"]+)\"")) triggers.Add(m.Groups[1].Value);
            int changed = 0, sounds = 0;
            string patched = Regex.Replace(text, "<Effect\\s+name=\"([^\"]+)\"\\s*>(.*?)</Effect>", delegate (Match m)
            {
                bool jump;
                string surface = SurfaceOf(m.Groups[1].Value, out jump);
                if (surface == null || !rules.Snowy.Contains(surface)) return m.Value;
                string body = m.Groups[2].Value;
                // The crunch: swap this surface's footstep trigger for the gravel one, if the game has it.
                if (rules.Crunch != "off" && surface != rules.Crunch)
                {
                    body = Regex.Replace(body, "trigger=\"([^\"]+)\"", delegate (Match t)
                    {
                        string tr = t.Groups[1].Value;
                        string swapped = Regex.Replace(tr, "_" + Regex.Escape(surface) + "(?=_|$)", "_" + rules.Crunch);
                        if (swapped != tr && triggers.Contains(swapped)) { sounds++; return "trigger=\"" + swapped + "\""; }
                        return t.Value;
                    });
                }
                var sb = new StringBuilder(body.TrimEnd());
                float size = horse ? rules.Hoof : rules.Human;
                if (jump) size *= 1.08f;
                // engine: fixed medium-depth prints in the footstep effect itself (left/right
                // shoes, two horseshoes; a RandEffect plays one of its children at random).
                // script (default): no decal here - the main mod lays prints that follow the
                // snow depth; the footstep keeps the kicked-up snow and the crunch.
                string kick = horse ? "harsh_winter.snow_kick_hoof" : (jump ? "harsh_winter.snow_kick_land" : "harsh_winter.snow_kick");
                sb.Append("\n\t<!-- Harsh Winter: kicked-up snow" + (rules.Prints == "engine" ? " and a print pressed into the snow" : "") + " -->\n");
                if (rules.Prints == "engine")
                {
                    string[] mats = horse
                        ? new[] { "Materials/harsh_winter/snow_hoofprint_a_m", "Materials/harsh_winter/snow_hoofprint_b_m" }
                        : new[] { "Materials/harsh_winter/snow_footprint_l_m", "Materials/harsh_winter/snow_footprint_r_m" };
                    sb.Append("\t<RandEffect>\n");
                    foreach (var mat in mats)
                        sb.Append("\t\t<Decal minscale=\"" + F(size * 0.97f) + "\" maxscale=\"" + F(size * 1.03f) + "\" growTime=\"0.05\" lifetime=\"" + rules.Life[who] + "\">\n\t\t\t<Material>" + mat + "</Material>\n\t\t</Decal>\n");
                    sb.Append("\t</RandEffect>\n");
                }
                sb.Append("\t<RandEffect>\n\t\t<Particle>\n\t\t\t<Name direction=\"normal\" minscale=\"0.8\" maxscale=\"1.1\" maxscaledist=\"40\">" + kick + "</Name>\n\t\t</Particle>\n\t</RandEffect>\n");
                changed++;
                return "<Effect name=\"" + m.Groups[1].Value + "\">" + sb.ToString() + "</Effect>";
            }, RegexOptions.Singleline);
            total += changed;
            log(string.Format("  footprints: {0}: {1} footstep effects get kicked-up snow{4}, {2} sounds swapped to {3}", file, changed, sounds, rules.Crunch, rules.Prints == "engine" ? " and prints" : ""));
            if (!list && outPak != null && changed > 0)
            {
                // Keep the game's path casing for the override.
                outPak.Add("Libs/MaterialEffects/FXLibs/" + file, Encoding.UTF8.GetBytes(patched));
            }
        }
        LastEffects = total;
        if (total == 0) log("  footprints: no footstep effects found - nothing written");
        return 0;
    }

    static string F(float v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
}
