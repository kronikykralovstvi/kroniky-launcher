using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KronikyLauncher.Mods;

public sealed class ReleaseControl
{
    public int SchemaVersion { get; set; }
    public long Sequence { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public bool Paused { get; set; }
    public string Message { get; set; } = "";
    public LauncherRelease Launcher { get; set; } = new();
    public ModManifest Mods { get; set; } = new();
}
public sealed class LauncherRelease
{
    public string Version { get; set; } = "";
    public string LauncherUrl { get; set; } = "";
    public string LauncherSha256 { get; set; } = "";
    public string UpdaterUrl { get; set; } = "";
    public string UpdaterSha256 { get; set; } = "";
}
public sealed class SignedEnvelope
{
    public string Payload { get; set; } = "";
    public string Signature { get; set; } = "";
}
public static class ReleaseTrust
{
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE55iJweP0Rtre2qMHdXLv46/dyeUhKFa4nFTjDhqbivdLlHw0G7cEvOyuH3dDEVFD5vE43fmsOZNCY9s2CEOpTg==";
    public const string RepositoryPrefix = "/kronikykralovstvi/kroniky-launcher/releases/download/";
    public const string PinnedUpdaterHash = "e89210599fdef8df1b64be85e4f99ca51fb9cc5ee940a57dafc13bae4ef8f1a4";
    public static bool AllowedAsset(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.Port == 443 && uri.UserInfo.Length == 0 &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath.StartsWith(RepositoryPrefix, StringComparison.Ordinal) &&
        !uri.AbsolutePath.Contains('%') && uri.AbsolutePath[RepositoryPrefix.Length..].Split('/').Length == 2;

    public static ReleaseControl Verify(byte[] document, long highestSequence, string publicKey = PublicKey)
    {
        if (document.Length > 2*1024*1024) throw new InvalidDataException("Příliš velký manifest.");
        var envelope=JsonSerializer.Deserialize<SignedEnvelope>(document,ModSync.Json) ?? throw new InvalidDataException("Chybí podpis aktualizace.");
        byte[] payload=Convert.FromBase64String(envelope.Payload), signature=Convert.FromBase64String(envelope.Signature);
        using var key=ECDsa.Create();key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey),out _);
        if (signature.Length!=64 || !key.VerifyData(payload,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new CryptographicException("Aktualizace nemá platný podpis Kronik. Nic se neinstalovalo.");
        var release=JsonSerializer.Deserialize<ReleaseControl>(payload,ModSync.Json) ?? throw new InvalidDataException("Neplatná aktualizace.");
        if(release.SchemaVersion!=1 || release.Sequence<Math.Max(1,highestSequence) || release.IssuedAt>DateTimeOffset.UtcNow.AddMinutes(10) || release.IssuedAt.Year<2026)
            throw new InvalidDataException("Zastaralé nebo neplatné vydání. Zkontroluj datum PC a zkus to znovu.");
        if(release.Launcher is null || release.Mods is null || !Version.TryParse(release.Launcher.Version,out _) ||
           !AllowedAsset(release.Launcher.LauncherUrl) || !Regex.IsMatch(release.Launcher.LauncherSha256,"^[a-fA-F0-9]{64}$") ||
           !AllowedAsset(release.Launcher.UpdaterUrl) || release.Launcher.UpdaterSha256!=PinnedUpdaterHash)
            throw new InvalidDataException("Nepovolený zdroj aktualizace launcheru.");
        if(release.Mods.Files is null || release.Mods.Files.Any(f=>f is null || !AllowedAsset(f.Url)))
            throw new InvalidDataException("Nepovolený zdroj modů.");
        return release;
    }
    public static async Task<byte[]> DownloadSmall(HttpClient client,string url,int limit,CancellationToken cancellation=default)
    {
        using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cancellation);response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("Příliš velká odpověď serveru.");
        await using var input=await response.Content.ReadAsStreamAsync(cancellation);using var output=new MemoryStream();
        byte[] buffer=new byte[16384];
        while(true){int count=await input.ReadAsync(buffer,cancellation);if(count==0)break;if(output.Length+count>limit)throw new InvalidDataException("Příliš velká odpověď serveru.");output.Write(buffer,0,count);}
        return output.ToArray();
    }
}
