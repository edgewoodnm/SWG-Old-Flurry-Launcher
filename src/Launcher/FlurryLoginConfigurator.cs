using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public static class FlurryLoginConfigurator
{
	public const string PublicHost = "73.127.195.38";

	public const int PublicPort = 44453;

	public static string Configure(string clientDirectory)
	{
		if (string.IsNullOrWhiteSpace(clientDirectory))
		{
			throw new ArgumentException("Client directory is missing.");
		}
		string fullPath = Path.GetFullPath(clientDirectory);
		string text = Path.Combine(fullPath, "login.cfg");
		if (!File.Exists(text))
		{
			throw new FileNotFoundException("SWG login.cfg was not found.", text);
		}
		if ((File.GetAttributes(text) & FileAttributes.ReparsePoint) != FileAttributes.None)
		{
			throw new IOException("Refusing to modify a linked login.cfg file.");
		}
		byte[] array = File.ReadAllBytes(text);
		if (Array.IndexOf(array, (byte)0) >= 0)
		{
			throw new InvalidDataException("Unsupported login.cfg text encoding.");
		}
		Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
		string text2;
		try
		{
			text2 = encoding.GetString(array);
		}
		catch (DecoderFallbackException)
		{
			throw new InvalidDataException("login.cfg is not valid UTF-8 text.");
		}
		if (Regex.Matches(text2, "(?m)^(?<prefix>[ \\t]*loginServerAddress0[ \\t]*=[ \\t]*)[^\\r\\n]*").Count != 1 || Regex.Matches(text2, "(?m)^(?<prefix>[ \\t]*loginServerPort0[ \\t]*=[ \\t]*)[^\\r\\n]*").Count != 1)
		{
			throw new InvalidDataException("login.cfg must contain exactly one loginServerAddress0 and one loginServerPort0 setting.");
		}
		string input = Regex.Replace(text2, "(?m)^(?<prefix>[ \\t]*loginServerAddress0[ \\t]*=[ \\t]*)[^\\r\\n]*", (Match match) => match.Groups["prefix"].Value + "73.127.195.38");
		input = Regex.Replace(input, "(?m)^(?<prefix>[ \\t]*loginServerPort0[ \\t]*=[ \\t]*)[^\\r\\n]*", (Match match) => match.Groups["prefix"].Value + 44453);
		if (input == text2)
		{
			return "Public login configuration is already correct.";
		}
		string text3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SWGOldFlurry", "LoginBackups");
		Directory.CreateDirectory(text3);
		string text4 = Path.Combine(text3, "login-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N") + ".cfg");
		File.WriteAllBytes(text4, array);
		string text5 = Path.Combine(fullPath, ".flurry-login-" + Guid.NewGuid().ToString("N") + ".tmp");
		try
		{
			File.WriteAllBytes(text5, encoding.GetBytes(input));
			File.Move(text5, text, overwrite: true);
		}
		finally
		{
			if (File.Exists(text5))
			{
				File.Delete(text5);
			}
		}
		return "Public login server configured: 73.127.195.38:" + 44453 + Environment.NewLine + "Original configuration backup: " + text4;
	}
}
