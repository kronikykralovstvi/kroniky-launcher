using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KronikyLauncher.Mods;

public sealed class ModManifest
{
    public int SchemaVersion { get; set; }
    public string Version { get; set; } = "";
    public List<ModFile> Files { get; set; } = [];
}
public sealed class ModFile
{
    public string Path { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}
public sealed class ManagedState
{
    public string Version { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public sealed class Change
{
    public string Path { get; set; } = "";
    public bool HadOriginal { get; set; }
    public bool Remove { get; set; }
    public string OriginalSha256 { get; set; } = "";
}
public sealed class Journal
{
    public string Transaction { get; set; } = "";
    public ManagedState Previous { get; set; } = new();
    public List<Change> Changes { get; set; } = [];
}

// Files are downloaded and verified before any active file changes. The persisted
// journal supports rollback after a failed write or an interrupted process.
public sealed class ModSync
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly HttpClient http;
    private readonly Action ensureGameStopped;
    public ModSync(HttpClient http, Action ensureGameStopped) { this.http = http; this.ensureGameStopped = ensureGameStopped; }

    public static string SafePath(string root, string relative)
    {
        var parts = relative.Split('/');
        // Native KCD:MP editor indexes Data/*.pak, not the game's Mods folder.
        // Permit only our dedicated catalogue, never arbitrary base-game files.
        bool editorCatalogue = relative == "Data/kk_editor_collection.pak";
        if ((!editorCatalogue && (parts.Length < 3 || parts[0] != "Mods")) || parts.Any(p =>
            string.IsNullOrWhiteSpace(p) || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
            p.Any(c => c < 32 || "<>:\"\\|?*".Contains(c)) ||
            Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) ||
            parts[1].Equals("KcdMp", StringComparison.OrdinalIgnoreCase) || parts[1].StartsWith('.'))
            throw new InvalidDataException("Nepovolená cesta modu: " + relative);
        var fullRoot = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var result = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (!result.StartsWith(fullRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Soubor je mimo složku hry.");
        RejectLinks(fullRoot, result);
        return result;
    }
    private static void RejectLinks(string root, string path)
    {
        for (string? current = path; current != null; current = System.IO.Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Složka obsahuje odkaz/junction: " + current);
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
    }
    public static void Validate(ModManifest manifest, string root)
    {
        if (manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.Version) || manifest.Files is null || manifest.Files.Count == 0 || manifest.Files.Count > 10000)
            throw new InvalidDataException("Neplatný nebo prázdný manifest modů.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in manifest.Files)
        {
            if (f is null) throw new InvalidDataException("Prázdná definice souboru.");
            SafePath(root, f.Path);
            if (!seen.Add(f.Path) || !Regex.IsMatch(f.Sha256, "^[a-fA-F0-9]{64}$") || f.Size < 0 || f.Size > 8L * 1024 * 1024 * 1024 ||
                !ReleaseTrust.AllowedAsset(f.Url))
                throw new InvalidDataException("Neplatná definice souboru: " + f.Path);
        }
    }
    public static async Task<string> Hash(string path)
    {
        await using var s = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(s)).ToLowerInvariant();
    }
    private static void WriteJson<T>(string path, T value)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Json);
            stream.Flush(true);
        }
        File.Move(temp, path, true);
    }
    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Neplatný stav aktualizace.");
    private static void ValidateState(ManagedState state, string root)
    {
        if (state.Files is null) throw new InvalidDataException("Poškozený stav modů.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in state.Files)
        {
            SafePath(root, pair.Key);
            if (!seen.Add(pair.Key) || !Regex.IsMatch(pair.Value, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("Poškozený stav modů.");
        }
    }
    private static async Task Rollback(string root, string control, Journal journal)
    {
        if (!Regex.IsMatch(journal.Transaction, "^[a-f0-9]{32}$")) throw new InvalidDataException("Neplatná transakce.");
        ValidateState(journal.Previous, root);
        var dir = System.IO.Path.Combine(control, journal.Transaction);
        RejectLinks(root, dir);
        // Validate every backup before restoring anything.
        foreach (var c in journal.Changes)
        {
            SafePath(root, c.Path);
            if (c.HadOriginal)
            {
                var backup = SafePath(System.IO.Path.Combine(dir, "backup"), c.Path);
                if (!File.Exists(backup) || !string.Equals(await Hash(backup), c.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Chybí nebo je poškozená záloha: " + c.Path);
            }
        }
        foreach (var c in journal.Changes.AsEnumerable().Reverse())
        {
            var target = SafePath(root, c.Path);
            if (c.HadOriginal) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!); File.Copy(SafePath(System.IO.Path.Combine(dir, "backup"), c.Path), target, true); }
            else if (File.Exists(target)) File.Delete(target);
        }
        WriteJson(System.IO.Path.Combine(control, "state.json"), journal.Previous);
        File.Delete(System.IO.Path.Combine(control, "pending.json"));
    }
    public async Task<int> Install(string root, ModManifest manifest, Action<string> report)
    {
        root = System.IO.Path.GetFullPath(root);
        Validate(manifest, root);
        ensureGameStopped();
        var control = System.IO.Path.Combine(root, ".kroniky-mods");
        RejectLinks(root, control);
        Directory.CreateDirectory(control);
        RejectLinks(root, System.IO.Path.Combine(control, "lock"));
        using var gate = new FileStream(System.IO.Path.Combine(control, "lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var pending = System.IO.Path.Combine(control, "pending.json");
        var statePath = System.IO.Path.Combine(control, "state.json");
        RejectLinks(root, pending); RejectLinks(root, statePath); RejectLinks(root, statePath + ".tmp"); RejectLinks(root, pending + ".tmp");
        if (File.Exists(pending)) { report("Obnovuji přerušenou instalaci…"); await Rollback(root, control, Read<Journal>(pending)); }
        var previous = File.Exists(statePath) ? Read<ManagedState>(statePath) : new ManagedState();
        ValidateState(previous, root);
        var wanted = manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var journal = new Journal { Transaction = Guid.NewGuid().ToString("N"), Previous = previous };
        var dir = System.IO.Path.Combine(control, journal.Transaction);
        foreach (var f in manifest.Files)
        {
            var target = SafePath(root, f.Path);
            report("Kontroluji " + f.Path);
            if (File.Exists(target) && new FileInfo(target).Length == f.Size && string.Equals(await Hash(target), f.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            var staged = SafePath(System.IO.Path.Combine(dir, "staged"), f.Path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(staged)!);
            using var response = await http.GetAsync(f.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long size && size != f.Size) throw new InvalidDataException("Nesouhlasí velikost: " + f.Path);
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var dest = File.Create(staged))
            {
                byte[] buffer = new byte[128 * 1024]; long total = 0; long lastReport = -1;
                while (true)
                {
                    using var idle = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    int count = await source.ReadAsync(buffer, idle.Token);
                    if (count == 0) break;
                    total += count;
                    if (total > f.Size) throw new InvalidDataException("Soubor je větší než manifest: " + f.Path);
                    await dest.WriteAsync(buffer.AsMemory(0, count));
                    long percent = f.Size == 0 ? 100 : total * 100 / f.Size;
                    if (percent != lastReport) { report($"Stahuji {f.Path} ({percent} %)"); lastReport = percent; }
                }
                if (total != f.Size) throw new InvalidDataException("Neúplné stažení: " + f.Path);
            }
            if (!string.Equals(await Hash(staged), f.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Kontrola SHA-256 selhala: " + f.Path);
            journal.Changes.Add(new Change { Path = f.Path, HadOriginal = File.Exists(target) });
        }
        foreach (var old in previous.Files)
        {
            if (wanted.ContainsKey(old.Key)) continue;
            var target = SafePath(root, old.Key);
            if (!File.Exists(target)) continue;
            if (!string.Equals(await Hash(target), old.Value, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Dříve spravovaný soubor byl ručně změněný: " + old.Key + ". Přesuň ho mimo Mods a zkus to znovu.");
            journal.Changes.Add(new Change { Path = old.Key, HadOriginal = true, Remove = true });
        }
        ensureGameStopped();
        foreach (var c in journal.Changes.Where(c => c.HadOriginal))
        {
            var target = SafePath(root, c.Path);
            var backup = SafePath(System.IO.Path.Combine(dir, "backup"), c.Path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup)!);
            File.Copy(target, backup);
            c.OriginalSha256 = await Hash(backup);
        }
        var next = new ManagedState { Version = manifest.Version, Files = manifest.Files.ToDictionary(f => f.Path, f => f.Sha256, StringComparer.OrdinalIgnoreCase) };
        if (journal.Changes.Count == 0) { WriteJson(statePath, next); return 0; }
        WriteJson(pending, journal);
        try
        {
            ensureGameStopped();
            foreach (var c in journal.Changes)
            {
                var target = SafePath(root, c.Path);
                if (c.Remove) File.Delete(target);
                else { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!); File.Move(SafePath(System.IO.Path.Combine(dir, "staged"), c.Path), target, true); }
            }
            WriteJson(statePath, next);
            File.Delete(pending);
        }
        catch
        {
            await Rollback(root, control, journal);
            throw;
        }
        report("Mody " + manifest.Version + " jsou připravené.");
        return journal.Changes.Count;
    }
}
