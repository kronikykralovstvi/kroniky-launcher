using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KronikyLauncher.Mods;

public sealed class WinterProfile(string version,Func<string,string,Action<string>,Task> build,Action ensureStopped)
{
    public const string OwnershipTag="kroniky-seasonal-textures";
    const string Mod="harsh_winter_world";
    const string Owner="kroniky-winter-owner.txt";
    const string Metadata="kroniky-winter-files.json";
    sealed class State
    {
        public string Fingerprint {get;set;}="";
        public Dictionary<string,string> Hashes {get;set;}=[];
    }
    static void Safe(string root,string path)
    {
        root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        path=Path.GetFullPath(path);
        if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Winter path escaped game directory.");
        for(var p=path;p!=null;p=Path.GetDirectoryName(p))
        {
            if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Zimní složka obsahuje odkaz nebo junction.");
            if(p.Equals(root,StringComparison.OrdinalIgnoreCase))break;
        }
    }
    string Fingerprint(string game)
    {
        var files=new List<string>{version};
        foreach(var dir in new[]{"Data","Engine"})
        if(Directory.Exists(Path.Combine(game,dir)))
        foreach(var p in Directory.EnumerateFiles(Path.Combine(game,dir),"*.pak",SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            if(Path.GetFileName(p).StartsWith("kk_",StringComparison.OrdinalIgnoreCase))continue;
            var f=new FileInfo(p);files.Add(Path.GetRelativePath(game,p)+":"+f.Length+":"+f.LastWriteTimeUtc.Ticks);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",files))));
    }
    void Owned(string path)
    {
        if(!File.Exists(Path.Combine(path,Owner))||File.ReadAllText(Path.Combine(path,Owner))!=OwnershipTag)
            throw new InvalidDataException("Složku "+path+" nevytvořila tato verze launcheru. Je zachovaná; její převzetí musí vyřešit správce.");
    }
    async Task<State> Inventory(string game,string path,string fingerprint)
    {
        var state=new State{Fingerprint=fingerprint};
        foreach(var p in Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories))
        {Safe(game,p);if(Path.GetFileName(p)==Metadata)continue;state.Hashes[Path.GetRelativePath(path,p)]=await ModSync.Hash(p);}
        if(!state.Hashes.ContainsKey("mod.manifest")||!state.Hashes.Keys.Any(p=>p.EndsWith(".pak",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Neúplný zimní balíček.");
        return state;
    }
    async Task<bool> Valid(string game,string path,string fingerprint)
    {
        Owned(path);
        var meta=Path.Combine(path,Metadata);Safe(game,meta);
        if(!File.Exists(meta))return false;
        try
        {
            var state=JsonSerializer.Deserialize<State>(File.ReadAllText(meta),ModSync.Json);
            if(state==null||state.Hashes==null||state.Fingerprint!=fingerprint)return false;
            var actual=await Inventory(game,path,fingerprint);
            return state.Hashes.Count==actual.Hashes.Count && state.Hashes.All(p=>actual.Hashes.TryGetValue(p.Key,out var h)&&h==p.Value);
        }
        catch(Exception e) when(e is JsonException or InvalidDataException or FileNotFoundException or DirectoryNotFoundException){return false;}
    }
    static void Order(string game,string storage,bool enabled)
    {
        var file=Path.Combine(game,"Mods","mod_order.txt");Safe(game,file);
        if(!File.Exists(file))return;
        var original=File.ReadAllText(file);var lines=original.Split('\n');
        bool present=lines.Any(l=>l.Trim().Equals(Mod,StringComparison.OrdinalIgnoreCase));
        if(present==enabled)return;
        var text=enabled?original+(original.EndsWith('\n')||original.Length==0?"":"\r\n")+Mod+"\r\n":string.Join("\n",lines.Where(l=>!l.Trim().Equals(Mod,StringComparison.OrdinalIgnoreCase)));
        File.WriteAllText(Path.Combine(storage,"mod-order-backup-"+Guid.NewGuid()+".txt"),original);
        var temp=file+".kroniky.tmp";Safe(game,temp);File.WriteAllText(temp,text,new UTF8Encoding(false));File.Move(temp,file,true);
    }
    public async Task Apply(string game,bool winter,Action<string> report)
    {
        ensureStopped();game=Path.GetFullPath(game);
        var storage=Path.Combine(game,".kroniky-winter");var active=Path.Combine(game,"Mods",Mod);var cache=Path.Combine(storage,"cache");
        foreach(var p in new[]{storage,active,cache})Safe(game,p);
        Directory.CreateDirectory(storage);
        var lockPath=Path.Combine(storage,"switch.lock");Safe(game,lockPath);
        using var gate=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(Directory.Exists(active))Owned(active);
        if(Directory.Exists(cache))Owned(cache);
        if(!winter)
        {
            if(Directory.Exists(active))
            {
                ensureStopped();
                if(Directory.Exists(cache))Directory.Move(cache,Path.Combine(storage,"retired-"+Guid.NewGuid()));
                Directory.Move(active,cache);
            }
            Order(game,storage,false);report("Jaro / léto / podzim: zasněžené textury jsou vypnuté.");return;
        }
        var fingerprint=Fingerprint(game);
        if(Directory.Exists(active)&&await Valid(game,active,fingerprint)){ensureStopped();Order(game,storage,true);return;}
        if(Directory.Exists(cache)&&!await Valid(game,cache,fingerprint)){ensureStopped();Directory.Move(cache,Path.Combine(storage,"retired-"+Guid.NewGuid()));}
        if(!Directory.Exists(cache))
        {
            report("Poprvé připravuji zimní krajinu z tvé hry. Může to trvat několik minut…");
            var staging=Path.Combine(storage,"build-"+Guid.NewGuid());Safe(game,staging);
            await build(game,staging,report);
            var result=Path.Combine(staging,Mod);Safe(game,result);Owned(result);
            var state=await Inventory(game,result,fingerprint);
            File.WriteAllText(Path.Combine(result,Metadata),JsonSerializer.Serialize(state,ModSync.Json));
            ensureStopped();Directory.Move(result,cache);
        }
        ensureStopped();
        // Each directory rename is atomic. Interrupted states are recognized next launch.
        Directory.CreateDirectory(Path.Combine(game,"Mods"));
        if(Directory.Exists(active))Directory.Move(active,Path.Combine(storage,"retired-"+Guid.NewGuid()));
        Directory.Move(cache,active);Order(game,storage,true);
        report("Zimní krajina je připravená podle kalendáře serveru.");
    }
}
