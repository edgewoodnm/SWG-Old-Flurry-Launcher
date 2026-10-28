using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace SwgOldFlurryLauncher;

public static class FlurryInstallationSafety
{
	public static void ValidateClientReady(string clientDirectory)
	{
		if (string.IsNullOrWhiteSpace(clientDirectory))
		{
			throw new InvalidOperationException("Please select your SWG client folder first.");
		}
		string fullPath = Path.GetFullPath(clientDirectory);
		if (!Directory.Exists(fullPath) || !File.Exists(Path.Combine(fullPath, "SwgClient_r.exe")))
		{
			throw new InvalidOperationException("The selected folder is not a valid SWG client.");
		}
		Process[] processes = Process.GetProcesses();
		foreach (Process process in processes)
		{
			using (process)
			{
				try
				{
					string processName = process.ProcessName;
					if (processName.Equals("SwgClient_r", StringComparison.OrdinalIgnoreCase) || processName.Equals("SwgClient", StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidOperationException("Please close Star Wars Galaxies before installing Old Flurry assets.");
					}
				}
				catch (Win32Exception)
				{
					throw new InvalidOperationException("Unable to verify whether SWG is running. Installation stopped.");
				}
				catch (InvalidOperationException ex2) when (ex2.Message.Contains("has exited", StringComparison.OrdinalIgnoreCase))
				{
				}
			}
		}
	}
}
