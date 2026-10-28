using System;
using System.IO;

namespace SwgOldFlurryLauncher;

public sealed class FlurryInstallationSession
{
	public string SessionDirectory { get; }

	public string StagingDirectory { get; }

	public string BackupDirectory { get; }

	public FlurryInstallationSession(string? dataRoot = null)
	{
		string path = Path.Combine(dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SWGOldFlurry"), "Installations");
		string path2 = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		SessionDirectory = Path.Combine(path, path2);
		StagingDirectory = Path.Combine(SessionDirectory, "Staging");
		BackupDirectory = Path.Combine(SessionDirectory, "Backups");
	}
}
