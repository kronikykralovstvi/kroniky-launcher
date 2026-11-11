using KronikyLauncher.Mods;
using System.Text.Json;

static class SeasonTests
{
    public static void Run()
    {
        var now=DateTimeOffset.UtcNow;
        byte[] Data(string season,int month,long age=0) => JsonSerializer.SerializeToUtf8Bytes(new {schemaVersion=1,season,month,day=1,year=1403,observedAt=now.ToUnixTimeSeconds()-age,receivedAt=now.ToUnixTimeSeconds()-age});
        foreach(var row in new[]{("winter",1),("winter",2),("spring",3),("summer",8),("autumn",11),("winter",12)})
            if(SeasonState.Parse(Data(row.Item1,row.Item2),now).Winter!=(row.Item1=="winter"))throw new Exception("Season mapping");
        foreach(var data in new[]{Data("winter",8),Data("summer",8,121),Data("summer",8,-61),Data("snow",1)})
        {try{SeasonState.Parse(data,now);throw new Exception("Accepted invalid season");}catch(InvalidDataException){}}
        Console.WriteLine("PASS seasonal state freshness and calendar consistency");
    }
}
