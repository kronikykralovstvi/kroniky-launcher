using System.Text.Json;

namespace KronikyLauncher.Mods;

public sealed class SeasonState
{
    public int SchemaVersion { get; set; }
    public string Season { get; set; } = "";
    public int Month { get; set; }
    public int Day { get; set; }
    public int Year { get; set; }
    public long ObservedAt { get; set; }
    public long ReceivedAt { get; set; }
    public bool Winter => Season == "winter";
    public static SeasonState Parse(byte[] data,DateTimeOffset now)
    {
        try
        {
            var s=JsonSerializer.Deserialize<SeasonState>(data,ModSync.Json) ?? throw new InvalidDataException();
            var expected=s.Month is 12 or 1 or 2?"winter":s.Month is >=3 and <=5?"spring":s.Month is >=6 and <=8?"summer":"autumn";
            int[] lengths=[31,28,31,30,31,30,31,31,30,31,30,31];
            var at=now.ToUnixTimeSeconds();
            if(s.SchemaVersion!=1 || s.Month<1 || s.Month>12 || s.Year<1403 || s.Year>9999 ||
               s.Day<1 || s.Day>lengths[s.Month-1] || s.Season!=expected ||
               at-s.ObservedAt>120 || s.ObservedAt-at>60 || at-s.ReceivedAt>120 || s.ReceivedAt-at>60)
                throw new InvalidDataException();
            return s;
        }
        catch(Exception e) when(e is JsonException or InvalidDataException)
        {throw new InvalidDataException("Server neposkytl aktuální ověřitelnou sezónu. Zkus to znovu, až bude server dostupný.",e);}
    }
    public static async Task<SeasonState> Fetch(HttpClient http,CancellationToken ct)
    {
        var data=await ReleaseTrust.DownloadSmall(http,"https://kronikykralovstvi.cz/season.php?t="+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),8192,ct);
        return Parse(data,DateTimeOffset.UtcNow);
    }
}
