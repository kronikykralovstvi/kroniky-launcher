using KronikyLauncher.Mods;
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("KronikyKralovstviValidation/1.0");
if (args.Length != 1) throw new ArgumentException("Pass an isolated validation directory. The probe never starts the client.");
var release=await OfficialClient.CheckServerAsync(http,"http://176.9.102.179:25588/",default);
Console.WriteLine("Server compatibility verified for official client " + release.Version);
var executable = await OfficialClient.PrepareAsync(http, args[0], () => { }, default,release);
Console.WriteLine("Verified client (not executed): " + executable);
