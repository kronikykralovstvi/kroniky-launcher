using KronikyLauncher.Mods;
using System.Text.Json;

static class OfficialClientTests
{
    public static void Run()
    {
        var hash = new string('a',64);
        object Release(string version, bool client) => new { tag_name="v"+version, draft=false, assets=new[]{new {
            name=client?$"KcdMp-{version}-win-x64.zip":$"KcdMp-server-{version}.zip",
            browser_download_url=$"https://github.com/dintech-rappy/kcd-mp-releases/releases/download/v{version}/"+(client?$"KcdMp-{version}-win-x64.zip":$"KcdMp-server-{version}.zip"),
            digest="sha256:"+hash,size=123L}}};
        var json=JsonSerializer.SerializeToUtf8Bytes(new[]{Release("0.49.1",false),Release("0.49.0",true),Release("0.48.1",true)});
        var latest=OfficialClient.SelectRelease(json);
        var launch=OfficialClient.LaunchInfo("C:/Client/KcdMp_launcher.exe","C:/Games/My KCD2","server:7777","Miroslav_Hranicar");
        if(!launch.ArgumentList.SequenceEqual(new[]{"--connect","server:7777","--name","Miroslav_Hranicar","--game","C:/Games/My KCD2"}) || launch.UseShellExecute)
            throw new Exception("selected game path missing from safe launch arguments");
        if(latest.Version!="0.49.0")throw new Exception("server-only release hid latest client");
        if(OfficialClient.PublishedProtocol("protocol 122 "+latest.Url+" "+latest.Sha256,latest)!=122)throw new Exception("protocol parse");
        foreach(var html in new[]{"protocol 122", "protocol 121 protocol 122 "+latest.Url+" "+latest.Sha256}) {
            try { OfficialClient.PublishedProtocol(html,latest); throw new Exception("unconfirmed/ambiguous protocol accepted"); }
            catch(InvalidDataException) { }
        }
        foreach(var bad in new[]{"../bad.exe","/bad.exe","C:/bad.exe","a/../../bad.exe","a:stream","a\\..\\bad.exe"})
        {
            try { OfficialClient.SafeEntry(Path.GetTempPath(),bad); }
            catch(InvalidDataException){continue;}
            throw new Exception("unsafe archive path accepted: "+bad);
        }
        var foreign=System.Text.Encoding.UTF8.GetString(json).Replace("github.com/dintech-rappy","evil.example/dintech-rappy");
        try{OfficialClient.SelectRelease(System.Text.Encoding.UTF8.GetBytes(foreign));throw new Exception("foreign source accepted");}
        catch(InvalidDataException){}
        Console.WriteLine("PASS official client selection and archive boundaries");
        Installation().GetAwaiter().GetResult();
    }

    private static async Task Installation()
    {
        using var bytes = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(bytes, System.IO.Compression.ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("KcdMp_launcher.exe").Open())) writer.Write("test executable fixture, never executed");
        var archive = bytes.ToArray();
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archive)).ToLowerInvariant();
        var json = JsonSerializer.SerializeToUtf8Bytes(new[]{new {tag_name="v0.49.0",draft=false,assets=new[]{new {
            name="KcdMp-0.49.0-win-x64.zip",size=archive.Length,digest="sha256:"+digest,
            browser_download_url="https://github.com/dintech-rappy/kcd-mp-releases/releases/download/v0.49.0/KcdMp-0.49.0-win-x64.zip"}}}});
        var handler = new FixtureHandler(json, archive);
        using var http = new HttpClient(handler);
        var root = Path.Combine(Path.GetTempPath(), "kk-client-test-"+Guid.NewGuid().ToString("N"));
        var stopped = 0;
        var first = await OfficialClient.PrepareAsync(http,root,()=>stopped++,default);
        var second = await OfficialClient.PrepareAsync(http,root,()=>stopped++,default);
        if (first != second || handler.Downloads != 1 || stopped != 2) throw new Exception("client cache or stop guard failed");
        await File.WriteAllTextAsync(first,"corrupt installation");
        var repaired = await OfficialClient.PrepareAsync(http,root,()=>stopped++,default);
        if (repaired == first || await File.ReadAllTextAsync(repaired) != "test executable fixture, never executed") throw new Exception("client repair failed");
        var rejected = false;
        try { await OfficialClient.PrepareAsync(http,root,()=>throw new InvalidOperationException("running game"),default); }
        catch(InvalidOperationException){rejected=true;}
        if (!rejected) throw new Exception("running game guard ignored");
        await File.WriteAllTextAsync(Path.Combine(root,digest+".zip"),"interrupted/corrupt cache");
        var recovered = await OfficialClient.PrepareAsync(http,root,()=>stopped++,default);
        if(handler.Downloads!=2 || recovered!=repaired)throw new Exception("corrupt archive cache did not recover");
        Console.WriteLine("PASS official client download, verified reuse, repair and running-game guard");
    }

    private sealed class FixtureHandler(byte[] metadata, byte[] archive) : HttpMessageHandler
    {
        public int Downloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            bool isMetadata = request.RequestUri!.AbsoluteUri == OfficialClient.ReleasesUrl;
            if (!isMetadata) Downloads++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent(isMetadata?metadata:archive)});
        }
    }
}
