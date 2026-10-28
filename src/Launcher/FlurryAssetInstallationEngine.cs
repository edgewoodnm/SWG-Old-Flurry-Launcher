using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace SwgOldFlurryLauncher;

public static class FlurryAssetInstallationEngine
{
	public static string PrepareBackups(string stagingDirectory, string clientDirectory, string backupDirectory, Action<long, long>? progress = null)
	{
		if (!Directory.Exists(stagingDirectory))
		{
			throw new DirectoryNotFoundException("Staging folder missing.");
		}
		if (!Directory.Exists(clientDirectory))
		{
			throw new DirectoryNotFoundException("Client folder missing.");
		}
		if (Directory.Exists(backupDirectory))
		{
			throw new IOException("Backup folder already exists.");
		}
		string fullPath = Path.GetFullPath(stagingDirectory);
		string fullPath2 = Path.GetFullPath(clientDirectory);
		string fullPath3 = Path.GetFullPath(backupDirectory);
		RejectReparsePoints(fullPath);
		RejectReparsePoints(fullPath2);
		RejectReparsePoints(fullPath3);
		ValidateSeparateFolders(fullPath, fullPath2, fullPath3);
		Directory.CreateDirectory(fullPath3);
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		string[] files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
		long backupDone = 0;
        foreach (string path in files)
		{
			string relativePath = Path.GetRelativePath(fullPath, path);
			string fileName = Path.GetFileName(relativePath);
			if (fileName.Equals("login.cfg", StringComparison.OrdinalIgnoreCase) || fileName.Equals("SwgClient_r.exe", StringComparison.OrdinalIgnoreCase) || fileName.Equals("UpdateSwgClient.bat", StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException("Protected SWG client file cannot be installed: " + relativePath);
			}
			RejectReparsePoints(path);
			string text = Path.Combine(fullPath2, relativePath);
			RejectReparsePoints(text);
			if (!File.Exists(text))
			{
				num3++;
                progress?.Invoke(++backupDone, files.Length);
				continue;
			}
			string text2 = HashFile(path);
			string text3 = HashFile(text);
			if (text2 == text3)
			{
				num2++;
                progress?.Invoke(++backupDone, files.Length);
				continue;
			}
			string text4 = Path.Combine(fullPath3, relativePath);
			RejectReparsePoints(text4);
			Directory.CreateDirectory(Path.GetDirectoryName(text4)!);
			File.Copy(text, text4, overwrite: false);
			if (HashFile(text4) != text3)
			{
				throw new IOException("Backup verification failed: " + relativePath);
			}
			num++;
            progress?.Invoke(++backupDone, files.Length);
		}
		return "ASSET BACKUP PREPARATION COMPLETE" + Environment.NewLine + "Verified backups: " + num + Environment.NewLine + "Identical files: " + num2 + Environment.NewLine + "Missing files: " + num3 + Environment.NewLine + "Backup folder: " + fullPath3 + Environment.NewLine + "No client assets were replaced.";
	}

	public static string InstallWithRollback(string stagingDirectory, string clientDirectory, string backupDirectory, Action<long, long>? progress = null)
	{
		string fullPath = Path.GetFullPath(stagingDirectory);
		string fullPath2 = Path.GetFullPath(clientDirectory);
		string fullPath3 = Path.GetFullPath(backupDirectory);
		RejectReparsePoints(fullPath);
		RejectReparsePoints(fullPath2);
		RejectReparsePoints(fullPath3);
		ValidateSeparateFolders(fullPath, fullPath2, fullPath3);
		if (!Directory.Exists(fullPath) || !Directory.Exists(fullPath2) || !Directory.Exists(fullPath3))
		{
			throw new DirectoryNotFoundException("Staging, client, or backup folder is missing.");
		}
		if (string.IsNullOrWhiteSpace(clientDirectory))
		{
			throw new IOException("Client folder is required.");
		}
		if (!File.Exists(Path.Combine(fullPath2, "SwgClient_r.exe")) && !File.Exists(Path.Combine(fullPath2, "swgclient_r.exe")))
		{
			throw new IOException("Selected folder does not contain the expected SWG client executable.");
		}
		List<(string, string, string, string)> list = new List<(string, string, string, string)>();
		int num = 0;
		string[] files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
		foreach (string text in files)
		{
			string relativePath = Path.GetRelativePath(fullPath, text);
			string fileName = Path.GetFileName(relativePath);
			if (fileName.Equals("login.cfg", StringComparison.OrdinalIgnoreCase) || fileName.Equals("SwgClient_r.exe", StringComparison.OrdinalIgnoreCase) || fileName.Equals("UpdateSwgClient.bat", StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException("Protected SWG client file cannot be installed: " + relativePath);
			}
			RejectReparsePoints(text);
			string fullPath4 = Path.GetFullPath(Path.Combine(fullPath2, relativePath));
			string fullPath5 = Path.GetFullPath(Path.Combine(fullPath3, relativePath));
			if (!fullPath4.StartsWith(fullPath2 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException("Unsafe destination path.");
			}
			RejectReparsePoints(fullPath4);
			RejectReparsePoints(fullPath5);
			string text2 = HashFile(text);
			if (!File.Exists(fullPath4))
			{
				list.Add((text, fullPath4, fullPath5, text2));
				continue;
			}
			string text3 = HashFile(fullPath4);
			if (text2 == text3)
			{
				num++;
				continue;
			}
			if (!File.Exists(fullPath5) || HashFile(fullPath5) != text3)
			{
				throw new IOException("Missing or invalid backup: " + relativePath);
			}
			list.Add((text, fullPath4, fullPath5, text2));
		}
		List<(string, string, bool)> list2 = new List<(string, string, bool)>();
		try
		{
			long installed = 0;
            foreach (var item2 in list)
			{
				bool item = File.Exists(item2.Item2);
				list2.Add((item2.Item2, item2.Item3, item));
				Directory.CreateDirectory(Path.GetDirectoryName(item2.Item2)!);
				File.Copy(item2.Item1, item2.Item2, overwrite: true);
				if (HashFile(item2.Item2) != item2.Item4)
				{
					throw new IOException("Installed asset verification failed.");
				}
                progress?.Invoke(++installed, list.Count);
			}
		}
		catch (Exception ex)
		{
			List<string> list3 = new List<string>();
			for (int num2 = list2.Count - 1; num2 >= 0; num2--)
			{
				try
				{
					if (!list2[num2].Item3)
					{
						if (File.Exists(list2[num2].Item1))
						{
							File.Delete(list2[num2].Item1);
						}
					}
					else
					{
						File.Copy(list2[num2].Item2, list2[num2].Item1, overwrite: true);
						if (HashFile(list2[num2].Item1) != HashFile(list2[num2].Item2))
						{
							throw new IOException("Rollback hash mismatch.");
						}
					}
				}
				catch (Exception ex2)
				{
					list3.Add(ex2.Message);
				}
			}
			if (list3.Count > 0)
			{
				throw new IOException("Installation failed and rollback was incomplete: " + ex.Message + " | " + string.Join("; ", list3), ex);
			}
			throw new IOException("Installation failed. All attempted changes were restored: " + ex.Message, ex);
		}
		return "ASSET INSTALLATION COMPLETE" + Environment.NewLine + "Updated files: " + list.Count + Environment.NewLine + "Identical files skipped: " + num + Environment.NewLine + "Backups retained at: " + fullPath3;
	}

	private static void ValidateSeparateFolders(string staging, string client, string backup)
	{
		string[] array = new string[3] { staging, client, backup };
		for (int i = 0; i < array.Length; i++)
		{
			for (int j = i + 1; j < array.Length; j++)
			{
				string text = Path.TrimEndingDirectorySeparator(Path.GetFullPath(array[i]));
				string text2 = Path.TrimEndingDirectorySeparator(Path.GetFullPath(array[j]));
				if (string.Equals(text, text2, StringComparison.OrdinalIgnoreCase) || text.StartsWith(text2 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || text2.StartsWith(text + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					throw new IOException("Staging, client, and backup folders must not overlap.");
				}
			}
		}
	}

	private static void RejectReparsePoints(string path)
	{
		string text = Path.GetFullPath(path);
		while (!string.IsNullOrEmpty(text))
		{
			if ((File.Exists(text) || Directory.Exists(text)) && (File.GetAttributes(text) & FileAttributes.ReparsePoint) != FileAttributes.None)
			{
				throw new IOException("Unsafe symbolic link or junction: " + text);
			}
			string? directoryName = Path.GetDirectoryName(text);
			if (!string.IsNullOrEmpty(directoryName) && !string.Equals(directoryName, text, StringComparison.OrdinalIgnoreCase))
			{
				text = directoryName;
				continue;
			}
			break;
		}
	}

	private static string HashFile(string path)
	{
		using SHA256 sHA = SHA256.Create();
		using FileStream inputStream = File.OpenRead(path);
		return Convert.ToHexString(sHA.ComputeHash(inputStream));
	}
}
