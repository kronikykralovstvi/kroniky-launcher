namespace KronikyLauncher;

internal sealed partial class LauncherForm
{
    private async Task PrepareSeasonAsync(string game)
    {
        async Task<Mods.SeasonState> Read()
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            status.Text="Ověřuji aktuální roční období serveru…";
            return await Mods.SeasonState.Fetch(http,timeout.Token);
        }
        var ui=new Progress<string>(message=>status.Text=message.Length>160?message[..160]:message);
        void Report(string message)=>((IProgress<string>)ui).Report(message);
        var profile=new Mods.WinterProfile(KronikyWinterBuilder.Version,
            (g,d,r)=>Task.Run(()=>KronikyWinterBuilder.Build(g,d,r)),EnsureGameStopped);
        var state=await Read();
        for(int attempt=0;attempt<2;attempt++)
        {
            await profile.Apply(game,state.Winter,Report);
            // Generation or an official client update can span a seasonal boundary.
            var fresh=await Read();
            if(fresh.Winter==state.Winter)
            {
                releaseLabel.Text="Launcher "+AppVersion+" • "+(fresh.Winter?"zimní krajina":"krajina bez sněhu")+" • podle serveru";
                return;
            }
            state=fresh;
        }
        throw new IOException("Server právě mění období. Zkus spuštění znovu, aby krajina odpovídala kalendáři.");
    }
}
