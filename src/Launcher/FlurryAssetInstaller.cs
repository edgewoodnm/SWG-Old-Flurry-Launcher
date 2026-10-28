using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SwgOldFlurryLauncher;

public static class FlurryAssetInstaller
{
	private const int MaximumZipEntries = 1000;

	private const long MaximumSingleFileBytes = 104857600L;

	private const long MaximumTotalBytes = 524288000L;

	private static void RejectReparsePoints(string path)
	{
		string text = Path.GetFullPath(path);
		while (!string.IsNullOrEmpty(text))
		{
			if ((File.Exists(text) || Directory.Exists(text)) && (File.GetAttributes(text) & FileAttributes.ReparsePoint) != FileAttributes.None)
			{
				throw new InvalidDataException("Symbolic links and junctions are not allowed: " + text);
			}
			string? directoryName = Path.GetDirectoryName(text);
			if (!string.IsNullOrEmpty(directoryName) && !(directoryName == text))
			{
				text = directoryName;
				continue;
			}
			break;
		}
	}

	private static void VerifyTrustedAssetZip(string zipPath, string expectedHash)
	{
		if (string.IsNullOrWhiteSpace(expectedHash) || !Regex.IsMatch(expectedHash, "\\A[0-9a-fA-F]{64}\\z"))
		{
			throw new InvalidDataException("Invalid expected SHA256 checksum.");
		}
		using SHA256 sHA = SHA256.Create();
		using FileStream inputStream = File.OpenRead(zipPath);
		if (!string.Equals(Convert.ToHexString(sHA.ComputeHash(inputStream)), expectedHash, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Asset ZIP failed trusted SHA256 verification.");
		}
	}

	private static void ValidateWindowsZipPath(string relative)
	{
		if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\0'))
		{
			throw new InvalidDataException("Unsafe ZIP filename: " + relative);
		}
		string[] array = relative.Replace('/', '\\').Split('\\');
		foreach (string text in array)
		{
			if (string.IsNullOrWhiteSpace(text) || text == "." || text == ".." || text.EndsWith(" ", StringComparison.Ordinal) || text.EndsWith(".", StringComparison.Ordinal))
			{
				throw new InvalidDataException("Unsafe ZIP path component: " + text);
			}
			if (text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			{
				throw new InvalidDataException("Invalid ZIP filename: " + text);
			}
			string text2 = text.Split('.')[0].ToUpperInvariant();
			switch (text2)
			{
			default:
				if (text2.Length != 4 || (!text2.StartsWith("COM", StringComparison.Ordinal) && !text2.StartsWith("LPT", StringComparison.Ordinal)) || text2[3] < '1' || text2[3] > '9')
				{
					continue;
				}
				break;
			case "CON":
			case "PRN":
			case "AUX":
			case "NUL":
			case "CONIN$":
			case "CONOUT$":
				break;
			}
			throw new InvalidDataException("Reserved Windows filename: " + text);
		}
	}

	private static void ValidateArchiveLimits(ZipArchive archive)
	{
		if (archive.Entries.Count > 1000)
		{
			throw new InvalidDataException("ZIP contains too many entries.");
		}
		long num = 0L;
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			if (!string.IsNullOrEmpty(entry.Name))
			{
				if (entry.Length > 104857600)
				{
					throw new InvalidDataException("ZIP file exceeds size limit: " + entry.FullName);
				}
				if (entry.Length < 0 || num > 524288000 - entry.Length)
				{
					throw new InvalidDataException("ZIP exceeds total extracted size limit.");
				}
				num += entry.Length;
			}
		}
	}

	public static string PrepareInstallation(string zipPath, string stagingDirectory, string expectedHash, Action<long, long>? progress = null)
	{
		if (!File.Exists(zipPath))
		{
			throw new FileNotFoundException("Asset ZIP not found.", zipPath);
		}
		VerifyTrustedAssetZip(zipPath, expectedHash);
		RejectReparsePoints(stagingDirectory);
		if (Directory.Exists(stagingDirectory))
		{
			throw new IOException("Staging folder already exists. Installation preparation stopped.");
		}
		string text = Path.GetFullPath(stagingDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<ZipArchiveEntry> list = new List<ZipArchiveEntry>();
		using (ZipArchive zipArchive = ZipFile.OpenRead(zipPath))
		{
			ValidateArchiveLimits(zipArchive);
			foreach (ZipArchiveEntry entry in zipArchive.Entries)
			{
				if (!string.IsNullOrEmpty(entry.Name))
				{
					if (!entry.FullName.EndsWith(".tre", StringComparison.OrdinalIgnoreCase) && !entry.FullName.Equals("client.cfg", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Asset updates may contain only TRE archives and client.cfg.");
                    string text2 = entry.FullName.Replace('/', '\\');
					ValidateWindowsZipPath(text2);
					if (Path.IsPathRooted(text2) || text2.Contains(':') || text2.Split('\\').Contains("..") || text2.Split('\\').Contains(".") || text2.Contains('\0'))
					{
						throw new InvalidDataException("Unsafe ZIP path: " + text2);
					}
					if (!hashSet.Add(text2))
					{
						throw new InvalidDataException("Duplicate ZIP path: " + text2);
					}
					if (!Path.GetFullPath(Path.Combine(text, text2)).StartsWith(text, StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidDataException("ZIP entry escapes staging folder.");
					}
					if (((entry.ExternalAttributes >> 16) & 0xF000) == 40960)
					{
						throw new InvalidDataException("Symbolic links are not allowed.");
					}
					list.Add(entry);
				}
			}
			Directory.CreateDirectory(stagingDirectory);
			long extracted = 0;
            long total = list.Sum(e => e.Length);
            foreach (ZipArchiveEntry item in list)
			{
				string path = item.FullName.Replace('/', '\\');
				string text3 = Path.Combine(text, path);
				RejectReparsePoints(text3);
				Directory.CreateDirectory(Path.GetDirectoryName(text3)!);
				using var source = item.Open();
                using var target = new FileStream(text3, FileMode.CreateNew);
                byte[] buffer = new byte[131072]; int n; long entryBytes = 0;
                while ((n = source.Read(buffer, 0, buffer.Length)) > 0) {
                    entryBytes += n;
                    if (entryBytes > item.Length) throw new InvalidDataException("Expanded entry exceeds declared length.");
                    target.Write(buffer, 0, n); extracted += n; progress?.Invoke(extracted, total);
                }
                if (entryBytes != item.Length) throw new InvalidDataException("Truncated archive entry.");
			}
		}
		return "INSTALLATION PREPARATION COMPLETE" + Environment.NewLine + "Extracted files: " + list.Count + Environment.NewLine + "Staging folder: " + stagingDirectory + Environment.NewLine + "No SWG client files were modified.";
	}

	public static string PreviewInstallation(string zipPath, string clientDirectory, string expectedHash)
	{
		if (!File.Exists(zipPath))
		{
			throw new FileNotFoundException("Asset ZIP not found.", zipPath);
		}
		VerifyTrustedAssetZip(zipPath, expectedHash);
		RejectReparsePoints(clientDirectory);
		if (!Directory.Exists(clientDirectory))
		{
			throw new DirectoryNotFoundException("Client folder not found.");
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		string text = Path.GetFullPath(clientDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		using ZipArchive zipArchive = ZipFile.OpenRead(zipPath);
		ValidateArchiveLimits(zipArchive);
		foreach (ZipArchiveEntry entry in zipArchive.Entries)
		{
			if (string.IsNullOrEmpty(entry.Name))
			{
				continue;
			}
			if (!entry.FullName.EndsWith(".tre", StringComparison.OrdinalIgnoreCase) && !entry.FullName.Equals("client.cfg", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Asset updates may contain only TRE archives and client.cfg.");
                    string text2 = entry.FullName.Replace('/', '\\');
			ValidateWindowsZipPath(text2);
			if (Path.IsPathRooted(text2) || text2.Contains(':') || text2.Split('\\').Contains(".."))
			{
				throw new InvalidDataException("Unsafe ZIP path: " + text2);
			}
			if (!hashSet.Add(text2))
			{
				throw new InvalidDataException("Duplicate ZIP path: " + text2);
			}
			string fullPath = Path.GetFullPath(Path.Combine(text, text2));
			if (!fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("ZIP path escapes client folder.");
			}
			RejectReparsePoints(fullPath);
			if (!File.Exists(fullPath))
			{
				num3++;
				continue;
			}
			using SHA256 sHA = SHA256.Create();
			using Stream inputStream = entry.Open();
			byte[] inArray = sHA.ComputeHash(inputStream);
			using FileStream inputStream2 = File.OpenRead(fullPath);
			byte[] inArray2 = sHA.ComputeHash(inputStream2);
			if (Convert.ToHexString(inArray) == Convert.ToHexString(inArray2))
			{
				num++;
			}
			else
			{
				num2++;
			}
		}
		return "OLD FLURRY ASSET INSTALLATION PREVIEW" + Environment.NewLine + "Identical files: " + num + Environment.NewLine + "Different files: " + num2 + Environment.NewLine + "Missing files: " + num3 + Environment.NewLine + "No files were modified.";
	}
}
