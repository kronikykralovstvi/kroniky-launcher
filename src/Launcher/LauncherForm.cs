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

	private const string AppVersion = "1.0.7";

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

	private readonly Button play = new Button
	{
		Text = "HRÁT — PŘIPOJIT SE K SERVERU",
		Height = 56,
		Dock = DockStyle.Top,
		Font = new Font("Segoe UI", 11f, FontStyle.Bold)
	};

	private readonly Button update = new Button
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
		Text = "Launcher 1.0.7",
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
		try
		{
			string text = Path.Combine(AppContext.BaseDirectory, "logo.ico");
			if (File.Exists(text))
			{
				Icon = new Icon(text);
			}
		}
		catch
		{
		}
		Text = "Kroniky království";
		MinimumSize = new Size(680, 460);
		Size = new Size(900, 740);
		StartPosition = FormStartPosition.CenterScreen;
		BackColor = Color.FromArgb(8, 19, 43);
		ForeColor = Color.FromArgb(239, 225, 195);
		Font = new Font("Segoe UI", 10f);
		FormBorderStyle = FormBorderStyle.FixedSingle;
		MaximizeBox = false;
		Label value = new Label
		{
			Text = "KRONIKY KRÁLOVSTVÍ",
			Dock = DockStyle.Top,
			Height = 95,
			TextAlign = ContentAlignment.MiddleCenter,
			Font = new Font("Georgia", 25f, FontStyle.Bold),
			ForeColor = Color.FromArgb(225, 184, 83)
		};
		PictureBox pictureBox = new PictureBox
		{
			Dock = DockStyle.Top,
			Height = 130,
			SizeMode = PictureBoxSizeMode.Zoom,
			BackColor = Color.FromArgb(8, 19, 43)
		};
		try
		{
			using var logo = typeof(LauncherForm).Assembly.GetManifestResourceStream("KronikyKralovstviLauncher.logo.png");
			if (logo != null)
			{
				using var loaded = Image.FromStream(logo);
				pictureBox.Image = new Bitmap(loaded);
			}
		}
		catch
		{
		}
		Label value2 = new Label
		{
			Text = "České země • Roleplay • KCD:MP",
			Dock = DockStyle.Top,
			Height = 34,
			TextAlign = ContentAlignment.MiddleCenter,
			ForeColor = Color.FromArgb(205, 195, 176)
		};
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(70, 22, 70, 22)
		};
		play.FlatStyle = FlatStyle.Flat;
		play.BackColor = Color.FromArgb(22, 48, 91);
		play.ForeColor = Color.White;
		play.FlatAppearance.BorderColor = Color.FromArgb(225, 184, 83);
		update.FlatStyle = FlatStyle.Flat;
		update.BackColor = Color.FromArgb(15, 32, 61);
		update.ForeColor = ForeColor;
		status.Text = "Připraveno. Zadej jméno své postavy a klikni na HRÁT.";
		try
		{
			if (File.Exists(playerNameFile))
			{
				playerName.Text = File.ReadAllText(playerNameFile).Trim();
			}
		}
		catch
		{
		}
		if (string.IsNullOrWhiteSpace(playerName.Text))
		{
			playerName.Text = "Hrac";
		}
		gameFolder.Click += (_, _) => SelectGameFolder(true);
		panel.Controls.Add(gameFolder);
		panel.Controls.Add(update);
		panel.Controls.Add(new Panel
		{
			Height = 12,
			Dock = DockStyle.Top
		});
		panel.Controls.Add(play);
		panel.Controls.Add(playerName);
		panel.Controls.Add(playerNameLabel);
		panel.Controls.Add(progress);
		panel.Controls.Add(status);
		panel.Controls.Add(new Panel
		{
			Height = 16,
			Dock = DockStyle.Top
		});
		Controls.Add(panel);
		Controls.Add(value2);
		Controls.Add(pictureBox);
		Controls.Add(value);
		Controls.Add(version);
		play.Click += async (object? _, EventArgs _) =>
		{
			if (await CheckAndInstallUpdatesAsync(false)) await JoinServerAsync();
		};
		update.Click += async (object? _, EventArgs _) =>
		{
			await CheckAndInstallUpdatesAsync(showNoUpdate: true);
		};
		Shown += async (object? _, EventArgs _) =>
		{
			await CheckAndInstallUpdatesAsync(showNoUpdate: false);
		};
	}

	private Task JoinServerAsync()
	{
		try
		{
			status.Text = "Spouštím KCD:MP a připojuji k serveru…";
			string text = Path.Combine(AppContext.BaseDirectory, "KcdMp_launcher.exe");
			if (!File.Exists(text))
			{
				try
				{
					if (File.Exists(clientPathFile))
					{
						text = File.ReadAllText(clientPathFile).Trim();
					}
				}
				catch
				{
				}
			}
			if (!File.Exists(text))
			{
				string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "kcd2 mult", "KcdMp_launcher.exe");
				if (File.Exists(text2))
				{
					text = text2;
					Directory.CreateDirectory(Path.GetDirectoryName(clientPathFile)!);
					File.WriteAllText(clientPathFile, text);
				}
			}
			if (!File.Exists(text))
			{
				OpenFileDialog openFileDialog = new OpenFileDialog
				{
					Title = "Vyber KcdMp_launcher.exe ve své instalaci KCD:MP",
					Filter = "KCD:MP launcher (KcdMp_launcher.exe)|KcdMp_launcher.exe|Všechny soubory (*.*)|*.*",
					FileName = "KcdMp_launcher.exe",
					CheckFileExists = true
				};
				try
				{
					if (openFileDialog.ShowDialog(this) != DialogResult.OK)
					{
						status.Text = "Spuštění zrušeno. Vyber soubor KcdMp_launcher.exe.";
						return Task.CompletedTask;
					}
					text = openFileDialog.FileName;
					Directory.CreateDirectory(Path.GetDirectoryName(clientPathFile)!);
					File.WriteAllText(clientPathFile, text);
				}
				finally
				{
					((IDisposable)(object)openFileDialog)?.Dispose();
				}
			}
			if (!File.Exists(text))
			{
				throw new FileNotFoundException("Vybraný soubor KcdMp_launcher.exe nebyl nalezen.", text);
			}
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = text,
				UseShellExecute = false,
				ArgumentList = { "--connect", "176.9.102.179:25588" }
			};
			string text3 = playerName.Text.Trim().Replace(' ', '_');
			if (string.IsNullOrWhiteSpace(text3))
			{
				throw new InvalidOperationException("Nejdřív zadej jméno své postavy.");
			}
			if (text3.Any(char.IsControl) || text3.Contains('"') || text3.Contains('\''))
			{
				throw new InvalidOperationException("Jméno obsahuje nepovolené znaky.");
			}
			Directory.CreateDirectory(Path.GetDirectoryName(playerNameFile)!);
			File.WriteAllText(playerNameFile, text3);
			processStartInfo.ArgumentList.Add("--name");
			processStartInfo.ArgumentList.Add(text3);
			Process.Start(processStartInfo);
			status.Text = "KCD:MP se spouští a připojuje k serveru…";
		}
		catch (Exception ex)
		{
			status.Text = "Hru se nepodařilo spustit.";
			MessageBox.Show("KCD:MP se nepodařilo spustit.\n\n" + ex.Message, "Chyba spuštění", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
		return Task.CompletedTask;
	}

	private async Task<bool> CheckAndInstallUpdatesAsync(bool showNoUpdate)
    {
        if (!update.Enabled) return false;
        update.Enabled = false; play.Enabled = false; gameFolder.Enabled = false;
        progress.Visible = true; progress.Style = ProgressBarStyle.Marquee;
        try
        {
            Directory.CreateDirectory(root);
            using var response = await http.GetAsync(ManifestUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            response.EnsureSuccessStatusCode();
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Neplatný manifest launcheru.");
            if (Version.TryParse(manifest.Version, out var latest) && latest > Version.Parse(AppVersion))
            {
                await StartSelfUpdateAsync(manifest);
                return false;
            }
            var game = SelectGameFolder(false);
            if (game is null) { status.Text = "Nejdřív vyber hlavní složku Kingdom Come: Deliverance II."; return false; }
            EnsureGameStopped();
            using var modsResponse = await http.GetAsync(ModsManifestUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            modsResponse.EnsureSuccessStatusCode();
            var mods = JsonSerializer.Deserialize<Mods.ModManifest>(await modsResponse.Content.ReadAsStringAsync(), Mods.ModSync.Json)
                ?? throw new InvalidDataException("Neplatný manifest modů.");
            var sync = new Mods.ModSync(http, EnsureGameStopped);
            await sync.Install(game, mods, message => status.Text = message);
            status.Text = "Mody " + mods.Version + " jsou připravené. Můžeš hrát.";
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
		byte[] array = await http.GetByteArrayAsync(result);
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
