using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KronikyLauncher;

internal sealed partial class LauncherForm : Form
{
	private const string ManifestUrl = "https://raw.githubusercontent.com/kronikykralovstvi/kroniky-launcher/main/manifest.json";

	private const string ServerAddress = "176.9.102.179:25588";

	private const string JoinUri = "kcdmp://connect/176.9.102.179:25588";

	private const string AppVersion = "1.1.4";

	private readonly Label status = new Label
	{
		AutoSize = false,
		Height = 48,
		Dock = DockStyle.Top,
		TextAlign = ContentAlignment.MiddleCenter
	};

	private readonly ProgressBar progress = new ProgressBar
	{
		Dock = DockStyle.Top,
		Height = 16,
		Visible = false
	};

	private readonly Button play = new RoyalButton
	{
		Gold = true,
        Text = "HRÁT — PŘIPOJIT SE K SERVERU",
		Height = 56,
		Dock = DockStyle.Top,
		Font = new Font("Segoe UI", 11f, FontStyle.Bold)
	};

	private readonly Button update = new RoyalButton
	{
		Text = "Zkontrolovat aktualizace",
		Height = 38,
		Dock = DockStyle.Top
	};

	private readonly Label playerNameLabel = new Label
	{
		Text = "Jméno postavy (bez mezer):",
		Height = 28,
		Dock = DockStyle.Top,
		TextAlign = ContentAlignment.MiddleLeft
	};

	private readonly TextBox playerName = new TextBox
	{
		Height = 32,
		Dock = DockStyle.Top,
		MaxLength = 24
	};

	private readonly string playerNameFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KronikyKralovstvi", "playername.txt");

	private readonly string clientPathFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KronikyKralovstvi", "clientpath.txt");

	private readonly Label version = new Label
	{
		Text = "Launcher 1.1.4",
		AutoSize = false,
		Height = 26,
		Dock = DockStyle.Bottom,
		TextAlign = ContentAlignment.MiddleRight
	};

	private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KronikyKralovstvi");

	private readonly HttpClient http = new HttpClient(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.All
	})
	{
		Timeout = TimeSpan.FromMinutes(30)
	};

    public LauncherForm()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KronikyKralovstviLauncher/1.1.4");
        InitializeDesign();
        try { if(File.Exists(playerNameFile))playerName.Text=File.ReadAllText(playerNameFile).Trim(); } catch { }
        play.Click+=async(_,_)=>{if(await CheckAndInstallUpdatesAsync(false))await JoinServerAsync();};
        update.Click+=async(_,_)=>await CheckAndInstallUpdatesAsync(true);
        gameFolder.Click+=(_,_)=>SelectGameFolder(true);
        foreach(var card in newsCards)card.Click+=(_,_)=>OpenLink(card.ArticleUrl??Website+"novinky/");
        Shown+=async(_,_)=>await Task.WhenAll(RefreshNews(),CheckAndInstallUpdatesAsync(false));
    }

    private async Task JoinServerAsync()
    {
        if (!play.Enabled) return;
        play.Enabled=false; update.Enabled=false; gameFolder.Enabled=false;
        progress.Visible=true; progress.Style=ProgressBarStyle.Marquee;
        try
        {
            string name=playerName.Text.Trim().Replace(' ','_');
            if(string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) || name.Contains('"') || name.Contains('\''))
                throw new InvalidOperationException("Zadej platné jméno své postavy.");
            EnsureGameStopped();
            var game=File.ReadAllText(GamePathFile).Trim();
            if(!IsGameRoot(game))throw new InvalidOperationException("Vybraná instalace hry již není dostupná. Otevři Nastavení hry.");
            status.Text="Ověřuji kompatibilitu aktuálního KCD:MP se serverem…";
            Mods.OfficialClientRelease release;
            using(var checkTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                release=await Mods.OfficialClient.CheckServerAsync(http,"http://"+ServerAddress+"/",checkTimeout.Token);
            status.Text="Kontroluji a připravuji oficiální aktualizaci KCD:MP…";
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var executable=await Mods.OfficialClient.PrepareAsync(http,Path.Combine(root,"official-client"),EnsureGameStopped,timeout.Token,release);
            EnsureGameStopped();
            Directory.CreateDirectory(root);
            File.WriteAllText(playerNameFile,name);
            var start=Mods.OfficialClient.LaunchInfo(executable,game,ServerAddress,name);
            Process.Start(start);
            status.Text="Ověřený KCD:MP se spouští a připojuje k serveru…";
        }
        catch(Exception ex)
        {
            status.Text="Spuštění zastaveno; předchozí instalace zůstává zachovaná.";
            MessageBox.Show(this,ex.Message,"KCD:MP aktualizace",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        finally
        {
            progress.Visible=false; play.Enabled=true; update.Enabled=true; gameFolder.Enabled=true;
        }
    }
	private async Task<bool> CheckAndInstallUpdatesAsync(bool showNoUpdate)
    {
        if (!update.Enabled) return false;
        update.Enabled = false; play.Enabled = false; gameFolder.Enabled = false;
        progress.Visible = true; progress.Style = ProgressBarStyle.Marquee;
        try
        {
            Directory.CreateDirectory(root);
            var stampPath=Path.Combine(root,"trusted-sequence.txt");
            long seen=File.Exists(stampPath)?long.Parse(File.ReadAllText(stampPath)):0;
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var signed=await Mods.ReleaseTrust.DownloadSmall(http,"https://raw.githubusercontent.com/kronikykralovstvi/kroniky-launcher/main/trusted-release.json?t="+DateTimeOffset.UtcNow.ToUnixTimeSeconds(),2*1024*1024,timeout.Token);
            var trusted=Mods.ReleaseTrust.Verify(signed,seen);
            File.WriteAllText(stampPath+".tmp",trusted.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.Move(stampPath+".tmp",stampPath,true);
            if(trusted.Paused){status.Text=string.IsNullOrWhiteSpace(trusted.Message)?"Administrace dočasně pozastavila distribuci.":trusted.Message;return false;}
            var manifest=new UpdateManifest{Version=trusted.Launcher.Version,LauncherUrl=trusted.Launcher.LauncherUrl,LauncherSha256=trusted.Launcher.LauncherSha256,UpdaterUrl=trusted.Launcher.UpdaterUrl,UpdaterSha256=trusted.Launcher.UpdaterSha256};
            if (Version.Parse(manifest.Version) > Version.Parse(AppVersion))
            {
                await StartSelfUpdateAsync(manifest);
                return false;
            }
            var game = SelectGameFolder(false);
            if (game is null) { status.Text = "Nejdřív vyber hlavní složku Kingdom Come: Deliverance II."; return false; }
            EnsureGameStopped();
            var mods=trusted.Mods;
            var sync = new Mods.ModSync(http, EnsureGameStopped);
            await sync.Install(game, mods, message => status.Text = message);
            status.Text = "Mody " + mods.Version + " jsou připravené. Můžeš hrát.";
            releaseLabel.Text="Launcher "+AppVersion+" • mody "+mods.Version+" • podpis ověřen";
            if (showNoUpdate) MessageBox.Show(this, "Mody jsou zkontrolované a nainstalované.", "Kroniky království");
            return true;
        }
        catch (Exception ex)
        {
            status.Text = "Kontrola modů selhala. Připojení nebylo spuštěné.";
            MessageBox.Show(this, "Aktualizaci se nepodařilo dokončit.\n\n" + ex.Message + "\n\nZkontroluj internet, složku hry a volné místo. Potom zkus aktualizaci znovu.", "Aktualizace modů", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        finally
        {
            progress.Visible = false; update.Enabled = true; play.Enabled = true; gameFolder.Enabled = true;
        }
    }

	private async Task StartSelfUpdateAsync(UpdateManifest manifest)
	{
		if (!Uri.TryCreate(manifest.UpdaterUrl, UriKind.Absolute, out Uri? result) || result.Scheme != Uri.UriSchemeHttps || !Uri.TryCreate(manifest.LauncherUrl, UriKind.Absolute, out Uri? launcherUri) || launcherUri.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidDataException("URL aktualizace musí používat HTTPS.");
		}
		if (manifest.UpdaterSha256.Length != 64 || manifest.LauncherSha256.Length != 64)
		{
			throw new InvalidDataException("SHA-256 v manifestu nemá správnou délku.");
		}
		Directory.CreateDirectory(root);
		string updaterPath = Path.Combine(root, "KronikyUpdater.exe");
		byte[] array = await Mods.ReleaseTrust.DownloadSmall(http, result!.AbsoluteUri, 100*1024*1024);
		if (!Convert.ToHexString(SHA256.HashData(array)).Equals(manifest.UpdaterSha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Kontrola SHA-256 aktualizačního programu selhala.");
		}
		string tempUpdater = updaterPath + ".download";
		await File.WriteAllBytesAsync(tempUpdater, array);
		File.Move(tempUpdater, updaterPath, overwrite: true);
		string? processPath = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(processPath))
		{
			throw new InvalidOperationException("Nelze určit cestu k launcheru.");
		}
		string[] array2 = new string[4] { "--update", processPath, launcherUri.AbsoluteUri, manifest.LauncherSha256 };
		ProcessStartInfo processStartInfo = new ProcessStartInfo(updaterPath)
		{
			UseShellExecute = false
		};
		string[] array3 = array2;
		foreach (string item in array3)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		if (Process.Start(processStartInfo) == null)
		{
			throw new InvalidOperationException("Aktualizační program se nepodařilo spustit.");
		}
		File.WriteAllText(playerNameFile,playerName.Text.Trim());
        status.Text = "Instaluji novou verzi launcheru…";
		await Task.Delay(300);
		Environment.Exit(0);
	}

	private static void ValidateRelativePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal) || path.Contains(':') || path.Contains('\\'))
		{
			throw new InvalidDataException("Nepovolená relativní cesta v manifestu: " + path);
		}
	}

	private static async Task<bool> HasSha256Async(string path, string expected)
	{
		if (string.IsNullOrWhiteSpace(expected) || expected.Length != 64)
		{
			return false;
		}
		bool result;
		await using (FileStream stream = File.OpenRead(path))
		{
			string text = Convert.ToHexString(await SHA256.HashDataAsync(stream));
			result = text.Equals(expected, StringComparison.OrdinalIgnoreCase);
		}
		return result;
	}
}
