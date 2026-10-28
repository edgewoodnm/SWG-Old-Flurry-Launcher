using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SwgOldFlurryLauncher;

public static class SwgClientLocator
{
	public static bool IsValidClient(string? folder)
	{
		try
		{
			return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) && File.Exists(Path.Combine(folder, "SwgClient_r.exe"));
		}
		catch
		{
			return false;
		}
	}

	public static List<string> FindClients(string? savedFolder = null)
	{
		List<string> candidates = new List<string>();
		Add(savedFolder);
		string baseDirectory = AppContext.BaseDirectory;
		Add(baseDirectory);
		DirectoryInfo? parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(baseDirectory));
		if (parent != null)
		{
			Add(parent.FullName);
		}
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
		string[] array = new string[2] { folderPath, folderPath2 };
		foreach (string text in array)
		{
			if (!Directory.Exists(text))
			{
				continue;
			}
			Add(text);
			try
			{
				foreach (string item in Directory.EnumerateDirectories(text))
				{
					Add(item);
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
		DriveInfo[] drives = DriveInfo.GetDrives();
		foreach (DriveInfo driveInfo in drives)
		{
			try
			{
				if (driveInfo.IsReady && driveInfo.DriveType == DriveType.Fixed)
				{
					Add(Path.Combine(driveInfo.RootDirectory.FullName, "Games", "SWGSource"));
					Add(Path.Combine(driveInfo.RootDirectory.FullName, "Games", "SWGSource Client v3.0"));
					Add(Path.Combine(driveInfo.RootDirectory.FullName, "SWGSource"));
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
		return candidates;
		void Add(string? folder)
		{
			if (IsValidClient(folder) && !candidates.Any((string x) => string.Equals(x, folder, StringComparison.OrdinalIgnoreCase)))
			{
				candidates.Add(Path.GetFullPath(folder!));
			}
		}
	}
}
