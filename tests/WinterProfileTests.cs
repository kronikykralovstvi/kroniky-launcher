using KronikyLauncher.Mods;
static class WinterProfileTests
{
 public static async Task Run()
 {
  var root=Path.Combine(Path.GetTempPath(),"kk-winter-tests-"+Guid.NewGuid());Directory.CreateDirectory(Path.Combine(root,"Mods"));Directory.CreateDirectory(Path.Combine(root,"Data"));
  File.WriteAllText(Path.Combine(root,"Mods","mod_order.txt"),"original\r\n# comment\r\n");
  var builds=0;var stopped=true;
  Task Build(string game,string output,Action<string> report){builds++;var dir=Path.Combine(output,"harsh_winter_world");Directory.CreateDirectory(Path.Combine(dir,"Data"));File.WriteAllText(Path.Combine(dir,"mod.manifest"),"manifest");File.WriteAllText(Path.Combine(dir,"Data","test.pak"),"snow");File.WriteAllText(Path.Combine(dir,"kroniky-winter-owner.txt"),WinterProfile.OwnershipTag);return Task.CompletedTask;}
  void Guard(){if(!stopped)throw new IOException("running");}
  var p=new WinterProfile("test",Build,Guard);
  await p.Apply(root,false,_=>{});if(builds!=0)throw new Exception("Built winter in summer");
  await p.Apply(root,true,_=>{});await p.Apply(root,true,_=>{});
  if(builds!=1||!Directory.Exists(Path.Combine(root,"Mods","harsh_winter_world")))throw new Exception("Winter reuse failed");
  stopped=false;try{await p.Apply(root,false,_=>{});throw new Exception("Changed running game");}catch(IOException){}stopped=true;
  await p.Apply(root,false,_=>{});if(Directory.Exists(Path.Combine(root,"Mods","harsh_winter_world")))throw new Exception("Snow still active");
  if(File.ReadAllText(Path.Combine(root,"Mods","mod_order.txt"))!="original\r\n# comment\r\n")throw new Exception("Changed unrelated mod order");
  await p.Apply(root,true,_=>{});if(builds!=1)throw new Exception("Rebuilt cached winter");
  File.WriteAllText(Path.Combine(root,"Mods","harsh_winter_world","kroniky-winter-files.json"),"broken");
  await p.Apply(root,true,_=>{});if(builds!=2)throw new Exception("Did not repair corrupt metadata");
  await new WinterProfile("next-version",Build,Guard).Apply(root,true,_=>{});if(builds!=3)throw new Exception("Did not upgrade own old version");
  var fresh=Path.Combine(Path.GetTempPath(),"kk-winter-fresh-"+Guid.NewGuid());Directory.CreateDirectory(fresh);
  await p.Apply(fresh,true,_=>{});if(!Directory.Exists(Path.Combine(fresh,"Mods","harsh_winter_world")))throw new Exception("Fresh Mods parent not created");
  File.WriteAllText(Path.Combine(root,"Mods","harsh_winter_world","kroniky-winter-owner.txt"),"foreign");
  try{await p.Apply(root,false,_=>{});throw new Exception("Touched foreign winter");}catch(InvalidDataException){}
  Console.WriteLine("PASS winter cache, season switch, mod order, foreign directory and running-game guard");
 }
}
