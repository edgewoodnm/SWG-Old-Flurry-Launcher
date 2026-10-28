using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public static class FlurryLoginConfigurator
{
    public const string PublicHost="73.127.195.38";
    public const int PublicPort=44453;
    public static string DefaultConfiguration => "[ClientGame]\nloginServerAddress0="+PublicHost+"\nloginServerPort0="+PublicPort+"\n";

    public static string Configure(string clientDirectory,string? dataRoot=null,string? rememberUsername=null)
    {
        if(rememberUsername!=null&&!Regex.IsMatch(rememberUsername,@"\A[A-Za-z0-9][A-Za-z0-9 _.-]{0,63}\z"))throw new ArgumentException("Unsupported login name.");
        if(string.IsNullOrWhiteSpace(clientDirectory))throw new ArgumentException("Client directory is missing.");
        string full=Path.GetFullPath(clientDirectory);
        if(!Directory.Exists(full))throw new DirectoryNotFoundException("Old Flurry client directory is missing.");
        string path=Path.Combine(full,"login.cfg");
        SwgOldFlurryLauncher.UpdateService.RejectLinks(path);
        byte[]? original=File.Exists(path)?File.ReadAllBytes(path):null;
        var encoding=new UTF8Encoding(false,true);
        string text="";
        if(original!=null)
        {
            if(Array.IndexOf(original,(byte)0)>=0)throw new InvalidDataException("Unsupported login.cfg text encoding.");
            try{text=encoding.GetString(original).TrimStart('\uFEFF');}
            catch(DecoderFallbackException){throw new InvalidDataException("login.cfg is not valid UTF-8 text.");}
        }
        var lines=new List<string>();bool inClient=false,added=false;
        foreach(string line in Regex.Split(text,"\\r\\n|\\n|\\r"))
        {
            var section=Regex.Match(line,@"^\s*\[([^\]]+)\]\s*(?:[#;].*)?$");
            if(section.Success)
            {
                inClient=section.Groups[1].Value.Equals("ClientGame",StringComparison.OrdinalIgnoreCase);
                lines.Add(line);
                if(inClient&&!added)
                {
                    lines.Add("loginServerAddress0="+PublicHost);lines.Add("loginServerPort0="+PublicPort);added=true;
                    if(rememberUsername!=null)lines.Add("loginClientID="+rememberUsername);
                }
                continue;
            }
            if(inClient&&Regex.IsMatch(line,@"^\s*loginServer(?:Address|Port)\d+\s*=",RegexOptions.IgnoreCase))continue;
            if(inClient&&rememberUsername!=null&&Regex.IsMatch(line,@"^\s*loginClientID\s*=",RegexOptions.IgnoreCase))continue;
            lines.Add(line);
        }
        if(!added)
        {
            if(lines.Count>0&&lines[^1].Length>0)lines.Add("");
            lines.Add("[ClientGame]");lines.Add("loginServerAddress0="+PublicHost);lines.Add("loginServerPort0="+PublicPort);
            if(rememberUsername!=null)lines.Add("loginClientID="+rememberUsername);
        }
        string updated=string.Join("\n",lines).TrimEnd('\n')+"\n";
        if(original!=null&&updated==text)return "Public login configuration is already correct.";
        if(original!=null)
        {
            string backup=Path.Combine(dataRoot??SwgOldFlurryLauncher.UpdateService.Data,"LoginBackups");
            SwgOldFlurryLauncher.UpdateService.RejectLinks(backup);Directory.CreateDirectory(backup);
            File.WriteAllBytes(Path.Combine(backup,"login-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+"-"+Guid.NewGuid().ToString("N")+".cfg"),original);
        }
        string temp=Path.Combine(full,".flurry-login-"+Guid.NewGuid().ToString("N")+".tmp");
        try{File.WriteAllText(temp,updated,encoding);File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
        return "Public login connection configured.";
    }
}
