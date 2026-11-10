using KronikyLauncher.Mods;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static string Sha(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Fails(Func<Task> action) { try { await action(); } catch { return; } throw new Exception("Expected rejection"); }
static ModManifest Manifest(params (string path, string body)[] rows) => new() { SchemaVersion = 1, Version = "test", Files = rows.Select(r => new ModFile { Path = r.path, Url = "https://github.com/kronikykralovstvi/kroniky-launcher/releases/download/test/" + Uri.EscapeDataString(r.path.Replace("/", "_")), Size = Encoding.UTF8.GetByteCount(r.body), Sha256 = Sha(r.body) }).ToList() };
SecurityTests.Run();
NewsFeedTests.Run();
var root = Path.Combine(Path.GetTempPath(), "kk-mod-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var handler = new FakeHandler(); using var http = new HttpClient(handler);
var sync = new ModSync(http, () => { });
string a = "Mods/test/a.pak", b = "Mods/test/b.pak";
handler.Files[a] = "one"; handler.Files[b] = "two";
Directory.CreateDirectory(Path.Combine(root, "Mods/test"));
await File.WriteAllTextAsync(Path.Combine(root, "Mods/test/personal.txt"), "keep");
Assert(await sync.Install(root, Manifest((a,"one"),(b,"two")), _=>{}) == 2,"first install");
int requests = handler.Requests;
Assert(await sync.Install(root, Manifest((a,"one"),(b,"two")), _=>{}) == 0,"idempotent");
Assert(handler.Requests == requests,"unchanged files must not download");
handler.Files[a] = "three";
Assert(await sync.Install(root, Manifest((a,"three")), _=>{}) == 2,"update + obsolete file");
Assert(!File.Exists(Path.Combine(root,b)),"owned obsolete removed");
Assert(File.ReadAllText(Path.Combine(root,"Mods/test/personal.txt")) == "keep","unrelated file preserved");
handler.Files[a] = "evil!";
await Fails(()=>sync.Install(root,Manifest((a,"good!")),_=>{}));
Assert(File.ReadAllText(Path.Combine(root,a))=="three","bad hash preserves installed file");
handler.Files[a] = "new"; handler.Files[b]="two";
Directory.CreateDirectory(Path.Combine(root,b)); // commit fails after first replacement
await Fails(()=>sync.Install(root,Manifest((a,"new"),(b,"two")),_=>{}));
Assert(File.ReadAllText(Path.Combine(root,a))=="three","rollback restores first file");
Assert(!File.Exists(Path.Combine(root,".kroniky-mods/pending.json")),"rollback journal cleared");
Directory.Delete(Path.Combine(root,b));
await File.WriteAllTextAsync(Path.Combine(root,a),"personal edit");
await Fails(()=>sync.Install(root,Manifest((b,"two")),_=>{}));
Assert(File.ReadAllText(Path.Combine(root,a))=="personal edit","edited obsolete protected");
foreach(var path in new[]{"../outside", "Mods/x/../../bad", "Mods/x/C:/bad", "Mods/x/a\\b", "Mods/x/NUL.txt", "Mods/x/a.", "Mods/KcdMp/file", "Mods/.hidden/file", "mods/x/file", "Data/Tables.pak", "Data/other.pak", "Data/kk_editor_collection.pak/extra", "Data/../kk_editor_collection.pak", "data/kk_editor_collection.pak"})
    await Fails(()=>sync.Install(root,Manifest((path,"x")),_=>{}));
string editor = "Data/kk_editor_collection.pak";
Assert(ModSync.SafePath(root, editor)==Path.Combine(root,"Data","kk_editor_collection.pak"), "only dedicated editor catalogue allowed");
var duplicate=Manifest((a,"one"),(a.ToUpperInvariant().Replace("MODS/","Mods/"),"one"));
await Fails(()=>sync.Install(root,duplicate,_=>{}));
var running = new ModSync(http,()=>throw new IOException("game running"));
requests=handler.Requests;
await Fails(()=>running.Install(root,Manifest((a,"new")),_=>{}));
Assert(handler.Requests==requests,"running game blocks downloads");
// Simulate a process crash with a persisted journal and a partially replaced file.
var control=Path.Combine(root,".kroniky-mods"); var tx=Guid.NewGuid().ToString("N");
var backup=Path.Combine(control,tx,"backup",a);Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.WriteAllText(backup,"three");
var previous=JsonSerializer.Deserialize<ManagedState>(File.ReadAllText(Path.Combine(control,"state.json")),ModSync.Json)!;
File.WriteAllText(Path.Combine(control,"pending.json"),JsonSerializer.Serialize(new Journal{Transaction=tx,Previous=previous,Changes=[new Change{Path=a,HadOriginal=true,OriginalSha256=Sha("three")}]},ModSync.Json));
handler.Files[a]="three";
Assert(await sync.Install(root,Manifest((a,"three")),_=>{})==0,"crash recovery before synchronization");
Assert(File.ReadAllText(Path.Combine(root,a))=="three","recovered content");
// The editor catalogue must use the same verified transaction path as mods.
var editorRoot = Path.Combine(root, "editor-fixture");
Directory.CreateDirectory(Path.Combine(editorRoot, "Data"));
File.WriteAllText(Path.Combine(editorRoot, "Data/Tables.pak"), "base game");
handler.Files[editor] = "catalogue v1";
Assert(await sync.Install(editorRoot, Manifest((editor,"catalogue v1")), _=>{}) == 1, "editor install");
Assert(await sync.Install(editorRoot, Manifest((editor,"catalogue v1")), _=>{}) == 0, "editor no-op");
handler.Files[editor] = "corrupt data";
await Fails(()=>sync.Install(editorRoot, Manifest((editor,"catalogue v2")), _=>{}));
Assert(File.ReadAllText(Path.Combine(editorRoot,editor))=="catalogue v1", "editor bad hash preserves catalogue");
handler.Files[editor] = "catalogue v2";
Assert(await sync.Install(editorRoot, Manifest((editor,"catalogue v2")), _=>{}) == 1, "editor update");
Assert(File.ReadAllText(Path.Combine(editorRoot,"Data/Tables.pak"))=="base game", "base game preserved");
Console.WriteLine("PASS: install, delta/no-op, obsolete ownership, unrelated files, hash failure, rollback, crash recovery, path validation, duplicate paths, running game.");
Console.WriteLine("Fixtures: "+root);

sealed class FakeHandler : HttpMessageHandler
{
    public Dictionary<string,string> Files {get;} = new();
    public int Requests {get;private set;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        Requests++;
        var asset=request.RequestUri!.AbsolutePath.Split('/')[^1];
        var path=Files.Keys.SingleOrDefault(p=>Uri.EscapeDataString(p.Replace("/", "_"))==asset);
        return Task.FromResult(path is not null && Files.TryGetValue(path,out var body)
            ? new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Encoding.UTF8.GetBytes(body))}
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
