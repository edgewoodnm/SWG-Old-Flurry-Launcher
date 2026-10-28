using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SwgOldFlurryLauncher;

public static class AmdScreenshotConfiguration
{
    // Options are outside the asset receipt, so launch-time repair does not invalidate TRE checksums.
    public static void Configure(string client,string? dataRoot=null)
    {
        string path=UpdateService.SafePath(client,"options.cfg");
        byte[]? original=File.Exists(path)?File.ReadAllBytes(path):null;
        if(original!=null&&original.Contains((byte)0))throw new InvalidDataException("Unsupported screenshot configuration encoding.");
        string text=original==null?"":new UTF8Encoding(false,true).GetString(original).TrimStart('\uFEFF');
        var lines=new List<string>();bool direct=false,added=false;
        foreach(string line in Regex.Split(text,"\r\n|\n|\r"))
        {
            var header=Regex.Match(line,@"^\s*\[([^\]]+)\]\s*(?:[#;].*)?$");
            if(header.Success)
            {
                direct=header.Groups[1].Value.Equals("Direct3d9",StringComparison.OrdinalIgnoreCase);lines.Add(line);
                if(direct&&!added){lines.Add("screenShotBackBuffer=true");lines.Add("doNotLockBackBuffer=false");added=true;}
                continue;
            }
            if(direct&&Regex.IsMatch(line,@"^\s*(screenShotBackBuffer|doNotLockBackBuffer)\s*=",RegexOptions.IgnoreCase))continue;
            lines.Add(line);
        }
        if(!added){lines.Add("");lines.Add("[Direct3d9]");lines.Add("screenShotBackBuffer=true");lines.Add("doNotLockBackBuffer=false");}
        string updated=string.Join("\n",lines).TrimEnd('\n')+"\n";
        if(updated==text)return;
        if(original!=null)
        {
            string backups=Path.Combine(dataRoot??UpdateService.Data,"RuntimeBackups");UpdateService.RejectLinks(backups);Directory.CreateDirectory(backups);
            File.WriteAllBytes(Path.Combine(backups,"amd-screenshot-"+Guid.NewGuid().ToString("N")+".cfg"),original);
        }
        string temp=UpdateService.SafePath(client,".flurry-screenshot-"+Guid.NewGuid().ToString("N")+".tmp");
        try{File.WriteAllText(temp,updated,new UTF8Encoding(false));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
