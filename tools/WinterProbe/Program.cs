using KronikyLauncher.Mods;
using System.Diagnostics;
if(args[0]!="--verify-switch") {KronikyWinterBuilder.Build(args[0],args[1],Console.WriteLine);return;}
var game=Path.GetFullPath(args[1]);var prepared=Path.GetFullPath(args[2]);
if(!prepared.StartsWith(Path.Combine(game,".kroniky-winter","build-"),StringComparison.OrdinalIgnoreCase))throw new Exception("Probe output must be within the game's isolated build directory.");
void Guard(){var ps=Process.GetProcessesByName("KingdomCome");try{if(ps.Length>0)throw new IOException("Game is running");}finally{foreach(var p in ps)p.Dispose();}}
Guard();
var profile=new WinterProfile(KronikyWinterBuilder.Version,(g,d,r)=>{Directory.Move(prepared,d);return Task.CompletedTask;},Guard);
await profile.Apply(game,true,Console.WriteLine);
if(!Directory.Exists(Path.Combine(game,"Mods","harsh_winter_world")))throw new Exception("Winter activation failed");
await profile.Apply(game,false,Console.WriteLine);
if(Directory.Exists(Path.Combine(game,"Mods","harsh_winter_world")))throw new Exception("Spring deactivation failed");
using var http=new HttpClient();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
var actual=await SeasonState.Fetch(http,timeout.Token);
await profile.Apply(game,actual.Winter,Console.WriteLine);
Console.WriteLine("PASS real generated winter activation/deactivation; current server profile: "+actual.Season);
