using Microsoft.Win32;
using System.Diagnostics;

namespace KronikyLauncher;

internal sealed partial class LauncherForm
{
    private const string ModsManifestUrl = "https://raw.githubusercontent.com/kronikykralovstvi/kroniky-launcher/main/mods-manifest.json";
    private readonly Button gameFolder = new() { Text = "Vybrat / změnit složku hry pro mody", Dock = DockStyle.Top, Height = 38 };
    private string GamePathFile => Path.Combine(root, "gamepath.txt");

    private static bool IsGameRoot(string path)
    {
        try
        {
            var bin = Path.Combine(path, "Bin");
            return Directory.Exists(bin) && (File.Exists(Path.Combine(bin, "KingdomCome.exe")) ||
                Directory.EnumerateDirectories(bin).Any(d => File.Exists(Path.Combine(d, "KingdomCome.exe"))));
        }
        catch { return false; }
    }

    private string? SelectGameFolder(bool force)
    {
        if (!force)
        {
            if (File.Exists(GamePathFile))
            {
                var saved = File.ReadAllText(GamePathFile).Trim();
                if (IsGameRoot(saved)) return saved;
            }
            // Steam registry plus common library locations; the picker covers GOG
            // and custom Steam libraries without guessing a KCDMP installation path.
            var candidates = new List<string>();
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (steam != null) candidates.Add(Path.Combine(steam, "steamapps", "common", "KingdomComeDeliverance2"));
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                candidates.Add(Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common", "KingdomComeDeliverance2"));
            var detected = candidates.Where(IsGameRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (detected.Count == 1) return SaveGameFolder(detected[0]);
        }
        using var picker = new FolderBrowserDialog { Description = "Vyber hlavní složku Kingdom Come: Deliverance II (obsahuje Bin a Data).", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (picker.ShowDialog(this) != DialogResult.OK) return null;
        if (!IsGameRoot(picker.SelectedPath))
        {
            MessageBox.Show(this, "Tady není Bin/…/KingdomCome.exe. Vyber hlavní složku hry, ne Mods ani složku KCD:MP.", "Složka hry");
            return null;
        }
        return SaveGameFolder(picker.SelectedPath);
    }
    private string SaveGameFolder(string path)
    {
        Directory.CreateDirectory(root);
        path = Path.GetFullPath(path);
        File.WriteAllText(GamePathFile, path);
        gameFolder.Text = "Složka hry: " + path;
        return path;
    }
    private static void EnsureGameStopped()
    {
        var processes = Process.GetProcessesByName("KingdomCome");
        try { if (processes.Length > 0) throw new IOException("Nejdřív úplně ukonči Kingdom Come: Deliverance II. Mody nelze měnit za běhu hry."); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
