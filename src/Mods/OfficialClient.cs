using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Security.Cryptography;

namespace KronikyLauncher.Mods;

public sealed record OfficialClientRelease(string Version, string Url, string Sha256, long Size);

public static class OfficialClient
{
    public const string ReleasesUrl = "https://api.github.com/repos/dintech-rappy/kcd-mp-releases/releases?per_page=100";

    public static int PublishedProtocol(string html, OfficialClientRelease release)
    {
        // The official download page publishes the wire protocol beside its
        // current build. Tie it to this exact asset, never just a patch number.
        if (!html.Contains(release.Url, StringComparison.Ordinal) || !html.Contains(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Oficiální stránka ještě nepotvrdila toto vydání KCD:MP. Zkus aktualizaci později.");
        var matches=Regex.Matches(html,@"\bprotocol\s+(\d+)\b",RegexOptions.IgnoreCase);
        var values=matches.Select(m=>int.Parse(m.Groups[1].Value)).Distinct().ToArray();
        if(values.Length!=1)throw new InvalidDataException("Nelze ověřit protokol oficiálního klienta KCD:MP.");
        return values[0];
    }

    public static async Task<OfficialClientRelease> CheckServerAsync(HttpClient http, string serverUrl, CancellationToken token)
    {
        var release=SelectRelease(await ReleaseTrust.DownloadSmall(http,ReleasesUrl,8*1024*1024,token));
        var page=System.Text.Encoding.UTF8.GetString(await ReleaseTrust.DownloadSmall(http,"https://kcd-mp.com/download",2*1024*1024,token));
        var protocol=PublishedProtocol(page,release);
        using var server=JsonDocument.Parse(await ReleaseTrust.DownloadSmall(http,serverUrl,256*1024,token));
        if(server.RootElement.GetProperty("protocol").GetInt32()!=protocol)
            throw new InvalidOperationException("Server a aktuální KCD:MP používají rozdílný protokol. Je potřeba dokončit aktualizaci serveru; připojení bylo zastaveno.");
        return release;
    }

    public static async Task<string> PrepareAsync(HttpClient http, string root, Action ensureStopped, CancellationToken token, OfficialClientRelease? checkedRelease=null)
    {
        root = Path.GetFullPath(root);
        var release = checkedRelease ?? SelectRelease(await ReleaseTrust.DownloadSmall(http, ReleasesUrl, 8*1024*1024, token));
        Directory.CreateDirectory(root);
        // Keep the downloaded archive: its official digest is checked on every
        // launch. Extract into a fresh directory so a failed update never alters
        // the previous installation or the user's separate KCD:MP installation.
        var archivePath = Path.Combine(root, release.Sha256 + ".zip");
        if (!File.Exists(archivePath))
        {
            var temporary = archivePath + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(token);
                await using (var target = File.Create(temporary))
                {
                    var buffer = new byte[81920]; long written = 0; int count;
                    while ((count = await source.ReadAsync(buffer, token)) > 0)
                    {
                        written += count;
                        if (written > release.Size) throw new InvalidDataException("Klient překročil očekávanou velikost.");
                        await target.WriteAsync(buffer.AsMemory(0, count), token);
                    }
                    if (written != release.Size) throw new InvalidDataException("Stažení klienta není úplné.");
                }
                await VerifyAsync(temporary, release, token);
                File.Move(temporary, archivePath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        await VerifyAsync(archivePath, release, token);
        ensureStopped();
        var pointer = Path.Combine(root, "client-" + release.Sha256 + ".txt");
        if (File.Exists(pointer))
        {
            var relative = (await File.ReadAllTextAsync(pointer, token)).Trim();
            var previous = SafeEntry(root, relative);
            if (Directory.Exists(previous) && await MatchesArchiveAsync(archivePath, previous, token))
            {
                var launches = Directory.GetFiles(previous, "KcdMp_launcher.exe", SearchOption.AllDirectories);
                if (launches.Length == 1) return launches[0];
            }
        }
        var destination = Path.Combine(root, "client-" + release.Version + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        try
        {
            using var zip = ZipFile.OpenRead(archivePath);
            long expanded = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                var path = SafeEntry(destination, entry.FullName);
                if (!seen.Add(path) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("Duplicitní cesta nebo odkaz v archivu klienta.");
                expanded += entry.Length;
                if (expanded > 4L*1024*1024*1024) throw new InvalidDataException("Rozbalený klient je příliš velký.");
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path);
            }
            var executables = Directory.GetFiles(destination, "KcdMp_launcher.exe", SearchOption.AllDirectories);
            if (executables.Length != 1) throw new InvalidDataException("Archiv neobsahuje jednoznačný launcher KCD:MP.");
            await File.WriteAllTextAsync(pointer + ".tmp", Path.GetFileName(destination), token);
            File.Move(pointer + ".tmp", pointer, true);
            return executables[0];
        }
        catch
        {
            if (Path.GetFullPath(destination).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(destination, true);
            throw;
        }
    }

    private static async Task<bool> MatchesArchiveAsync(string archive, string directory, CancellationToken token)
    {
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            var target = SafeEntry(directory, entry.FullName);
            if (!File.Exists(target) || new FileInfo(target).Length != entry.Length) return false;
            await using var installed = File.OpenRead(target);
            await using var original = entry.Open();
            var installedHash = await SHA256.HashDataAsync(installed, token);
            var originalHash = await SHA256.HashDataAsync(original, token);
            if (!installedHash.SequenceEqual(originalHash)) return false;
        }
        return true;
    }

    private static async Task VerifyAsync(string path, OfficialClientRelease release, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        if (file.Length != release.Size || !Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Kontrolní součet klienta KCD:MP nesouhlasí. Archiv nebyl spuštěn.");
    }

    // The newest release can contain only the server. Never infer a client URL
    // from the server version or accept a mirror supplied by release text.
    public static OfficialClientRelease SelectRelease(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var candidates = new List<OfficialClientRelease>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() ||
                (release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())) continue;
            var version = release.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
            if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) continue;
            var name = $"KcdMp-{version}-win-x64.zip";
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != name) continue;
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (url != $"https://github.com/dintech-rappy/kcd-mp-releases/releases/download/v{version}/{name}")
                    throw new InvalidDataException("Neplatný zdroj klienta KCD:MP.");
                var digest = asset.GetProperty("digest").GetString() ?? "";
                var size = asset.GetProperty("size").GetInt64();
                if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$") || size <= 0 || size > 2L*1024*1024*1024)
                    throw new InvalidDataException("Chybí platný kontrolní součet nebo velikost klienta.");
                candidates.Add(new(version, url, digest[7..].ToLowerInvariant(), size));
            }
        }
        return candidates.OrderByDescending(x => Version.Parse(x.Version)).FirstOrDefault()
            ?? throw new InvalidDataException("Oficiální vydání neobsahují klienta pro Windows.");
    }

    public static string SafeEntry(string root, string entry)
    {
        var parts = entry.Replace('\\', '/').Split('/');
        if (string.IsNullOrWhiteSpace(entry) || entry.StartsWith('/') || entry.StartsWith('\\') ||
            entry.Contains(':') || parts.Any(p => p is ".." or "." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException("Nebezpečná cesta v archivu klienta.");
        var boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(boundary, entry.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cesta opouští složku klienta.");
        return target;
    }
}
