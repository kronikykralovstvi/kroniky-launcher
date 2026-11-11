using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

public static class KronikyWinterBuilder
{
    public const string Version="4.5.1-kk-textures-1";
    // Output is a new isolated directory, never the active Mods folder.
    public static void Build(string game,string destination,Action<string> log)
    {
        if(Directory.Exists(destination))throw new IOException("Winter build destination already exists.");
        Directory.CreateDirectory(destination);
        var helper=Path.Combine(destination,"helper");Directory.CreateDirectory(helper);
        foreach(var name in new[]{"snow_detail.bin","winter_rules.txt"})
        {
            using(var source=typeof(KronikyWinterBuilder).Assembly.GetManifestResourceStream("Winter."+name))
            using(var output=File.Create(Path.Combine(helper,name)))source.CopyTo(output);
        }
        WinterTextures.Log=log;
        if(WinterTextures.Run("textures",game,helper,destination,1.0f,"native",false,false,true)!=0)
            throw new IOException("Generování zimních textur se nezdařilo.");
        var result=Path.Combine(destination,WinterTextures.ModId);
        var paks=Directory.GetFiles(result,"*.pak",SearchOption.AllDirectories);
        if(paks.Length==0)throw new IOException("Generátor nevytvořil zimní textury.");
        foreach(var pak in paks)
        using(var archive=ZipFile.OpenRead(pak))
        foreach(var entry in archive.Entries)
        {
            var path=entry.FullName.Replace('\\','/');
            if(path.StartsWith("Scripts/",StringComparison.OrdinalIgnoreCase)||path.StartsWith("Libs/",StringComparison.OrdinalIgnoreCase))
                throw new IOException("Zimní balíček obsahuje nepovolenou změnu herních systémů: "+path);
        }
        File.WriteAllText(Path.Combine(result,"kroniky-winter-owner.txt"),"kroniky-seasonal-textures");
    }
}
