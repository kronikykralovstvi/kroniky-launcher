using KronikyLauncher.Mods;
using System.Security.Cryptography;
using System.Text.Json;

static class SecurityTests
{
    public static void Run()
    {
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stranger=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string publicKey=Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var release=new ReleaseControl{SchemaVersion=1,Sequence=5,IssuedAt=DateTimeOffset.UtcNow,Launcher=new LauncherRelease
        {Version="1.1.0",LauncherUrl="https://github.com/kronikykralovstvi/kroniky-launcher/releases/download/v1.1.0/Launcher.exe",LauncherSha256=new string('a',64),UpdaterUrl="https://github.com/kronikykralovstvi/kroniky-launcher/releases/download/v1.0.3/KronikyUpdater.exe",UpdaterSha256=ReleaseTrust.PinnedUpdaterHash},Mods=new ModManifest{SchemaVersion=1,Version="test"}};
        byte[] Envelope(ReleaseControl r,ECDsa signer)
        {
            byte[] payload=JsonSerializer.SerializeToUtf8Bytes(r,ModSync.Json);
            return JsonSerializer.SerializeToUtf8Bytes(new SignedEnvelope{Payload=Convert.ToBase64String(payload),Signature=Convert.ToBase64String(signer.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))});
        }
        void Reject(Action action){try{action();}catch{return;}throw new Exception("Untrusted release accepted");}
        var valid=Envelope(release,key);
        if(ReleaseTrust.Verify(valid,4,publicKey).Sequence!=5)throw new Exception("Valid signature failed");
        Reject(()=>ReleaseTrust.Verify(valid,6,publicKey));
        Reject(()=>ReleaseTrust.Verify(Envelope(release,stranger),0,publicKey));
        var tampered=JsonSerializer.Deserialize<SignedEnvelope>(valid)!;
        byte[] bytes=Convert.FromBase64String(tampered.Payload);bytes[20]^=1;tampered.Payload=Convert.ToBase64String(bytes);
        Reject(()=>ReleaseTrust.Verify(JsonSerializer.SerializeToUtf8Bytes(tampered),0,publicKey));
        Reject(()=>ReleaseTrust.Verify(JsonSerializer.SerializeToUtf8Bytes(release),0,publicKey));
        release.Paused=true;if(!ReleaseTrust.Verify(Envelope(release,key),5,publicKey).Paused)throw new Exception("Pause flag lost");
        release.Launcher.UpdaterSha256=new string('b',64);Reject(()=>ReleaseTrust.Verify(Envelope(release,key),0,publicKey));
        foreach(var url in new[]{"https://github.com.attacker.test/kronikykralovstvi/kroniky-launcher/releases/download/v1/file","https://github.com/another/repo/releases/download/v1/file","http://github.com/kronikykralovstvi/kroniky-launcher/releases/download/v1/file","https://user@github.com/kronikykralovstvi/kroniky-launcher/releases/download/v1/file"})
            if(ReleaseTrust.AllowedAsset(url))throw new Exception("Forbidden download host accepted");
        Console.WriteLine("PASS: signature, tampering, foreign key, unsigned input, sequence rollback, signed maintenance, pinned updater, URL restrictions.");
    }
}
