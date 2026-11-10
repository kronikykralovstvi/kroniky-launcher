using KronikyLauncher.Mods;
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("KronikyKralovstviValidation/1.0");
if (args.Length != 1) throw new ArgumentException("Pass an isolated validation directory. The probe never starts the client.");
var executable = await OfficialClient.PrepareAsync(http, args[0], () => { }, default);
Console.WriteLine("Verified client (not executed): " + executable);
