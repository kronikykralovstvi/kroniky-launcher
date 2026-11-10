using KronikyLauncher.Mods;
using System.Text;
static class NewsFeedTests
{
    public static void Run()
    {
        var posts=NewsFeed.Parse(Encoding.UTF8.GetBytes("""{"news":[{"id":7,"title":"Nové řemeslo","body":"První řádek\nDruhý řádek","created_at":"2026-11-10"}]}"""));
        if(posts.Length!=1 || posts[0].Title!="Nové řemeslo" || posts[0].Summary!="První řádek Druhý řádek")throw new Exception("Endora news schema failed");
        if(NewsFeed.Parse(Encoding.UTF8.GetBytes("{\"news\":[]}")).Length!=0)throw new Exception("Empty news failed");
        Console.WriteLine("PASS Endora news and empty feed");
    }
}
