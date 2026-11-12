using KronikyLauncher.Mods;
using System.Text;
static class NewsFeedTests
{
    public static void Run()
    {
        var posts=NewsFeed.Parse(Encoding.UTF8.GetBytes("""{"news":[{"id":7,"title":"Nové řemeslo","body":"První řádek\nDruhý řádek","created_at":"2026-11-10"}]}"""));
        if(posts.Length!=1 || posts[0].Title!="Nové řemeslo" || posts[0].Summary!="První řádek Druhý řádek")throw new Exception("Endora news schema failed");
        if(NewsFeed.Parse(Encoding.UTF8.GetBytes("{\"news\":[]}")).Length!=0)throw new Exception("Empty news failed");
        foreach(var url in new[]{"https://evil.example/banner.png","file:///C:/secret.png","https://kronikykralovstvi.cz@evil.example/a.png"})
            if(NewsFeed.Parse(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new{news=new[]{new{title="Test",body="Text",imageUrl=url}}})))[0].ImageUrl!=null)throw new Exception("Unsafe news image accepted");
        var banner=NewsFeed.Parse(Encoding.UTF8.GetBytes("""{"news":[{"title":"Otevření","body":"14:00","imageUrl":"/spusteni-serveru-20261114.png"}]}"""))[0];
        if(banner.ImageUrl!="https://kronikykralovstvi.cz/spusteni-serveru-20261114.png")throw new Exception("News banner missing");
        Console.WriteLine("PASS Endora news and empty feed");
    }
}
