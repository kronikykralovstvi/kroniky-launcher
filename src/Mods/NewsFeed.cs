using System.Text.Json;
using System.Text.RegularExpressions;

namespace KronikyLauncher.Mods;

public sealed record NewsEntry(string Title, string Summary);
public static class NewsFeed
{
    public const string Website = "https://kronikykralovstvi.cz/";
    public static NewsEntry[] Parse(byte[] data)
    {
        using var document=JsonDocument.Parse(data);
        return document.RootElement.GetProperty("news").EnumerateArray().Take(2)
            .Select(post=>new NewsEntry(Compact(post.GetProperty("title").GetString(),120),
                Compact(post.GetProperty("body").GetString(),240))).ToArray();
    }
    private static string Compact(string? text,int limit)
    {
        var value=Regex.Replace(text??"",@"\s+"," ").Trim();
        return value.Length>limit?value[..limit]+"…":value;
    }
}
