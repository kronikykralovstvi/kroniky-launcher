using System.Collections.Generic;

namespace KronikyLauncher;

internal sealed class UpdateManifest
{
	public string Version { get; set; } = "1.0.2";

	public string LauncherUrl { get; set; } = "";

	public string LauncherSha256 { get; set; } = "";

	public string UpdaterUrl { get; set; } = "";

	public string UpdaterSha256 { get; set; } = "";

	public List<UpdateFile> Files { get; set; } = new List<UpdateFile>();
}
