using KronikyLauncher.Mods;
using System.Net;

static class SeasonAvailabilityTests
{
    sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("{\"error\":\"Server season unavailable\"}")});
    }
    public static async Task Run()
    {
        using var http=new HttpClient(new Offline());
        var state=await SeasonState.FetchAvailable(http,CancellationToken.None);
        if(state!=null)throw new Exception("Unavailable calendar must not invent a season");
        Console.WriteLine("PASS season 503 is unavailable, not a launch failure or invented season");
    }
}
