using System.Text.Json;
using System.Text.RegularExpressions;

namespace KronikyLauncher.Mods;

public sealed record NewsEntry(string Title, string Summary, string? ImageUrl = null);
public static class NewsFeed
{
    public const string Website = "https://kronikykralovstvi.cz/";
    public static NewsEntry[] Parse(byte[] data)
    {
        using var document=JsonDocument.Parse(data);
        return document.RootElement.GetProperty("news").EnumerateArray().Take(2)
            .Select(post=>new NewsEntry(Compact(post.GetProperty("title").GetString(),120),
                Compact(post.GetProperty("body").GetString(),240),
                post.TryGetProperty("imageUrl",out var image)&&image.ValueKind==JsonValueKind.String?SafeImage(image.GetString()):null)).ToArray();
    }
    private static string? SafeImage(string? value)
    {
        if(string.IsNullOrWhiteSpace(value)||!Uri.TryCreate(new Uri(Website),value,out var uri))return null;
        return uri.Scheme=="https"&&uri.Host=="kronikykralovstvi.cz"&&uri.Port==443&&uri.UserInfo.Length==0
            &&uri.AbsolutePath.EndsWith(".png",StringComparison.OrdinalIgnoreCase)?uri.AbsoluteUri:null;
    }
    private static string Compact(string? text,int limit)
    {
        var value=Regex.Replace(text??"",@"\s+"," ").Trim();
        return value.Length>limit?value[..limit]+"…":value;
    }
}
