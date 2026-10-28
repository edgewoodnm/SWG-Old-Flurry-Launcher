using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SwgOldFlurryLauncher;

public static class RuntimeConfiguration
{
    public const string FeatureDefaults="[Station]\ngameFeatures=0xffffffff\nsubscriptionFeatures=0x01\n";
    public static void Repair(string client,string? dataRoot=null)
    {
        FullscreenConfiguration.Configure(client,dataRoot);
        string path=UpdateService.SafePath(client,"live.cfg");
        if(!File.Exists(path))throw new IOException("Old Flurry live.cfg is missing. The base client needs repair.");
        byte[] original=File.ReadAllBytes(path);if(original.Contains((byte)0))throw new InvalidDataException("Unsupported live.cfg encoding.");
        string text=new UTF8Encoding(false,true).GetString(original).TrimStart('\uFEFF');
        var lines=new List<string>();bool station=false,added=false;
        foreach(string line in Regex.Split(text,"\r\n|\n|\r"))
        {
            var section=Regex.Match(line,@"^\s*\[([^\]]+)\]\s*(?:[#;].*)?$");
            if(section.Success)
            {
                station=section.Groups[1].Value.Equals("Station",StringComparison.OrdinalIgnoreCase);lines.Add(line);
                if(station&&!added){lines.Add("gameFeatures=0xffffffff");lines.Add("subscriptionFeatures=0x01");added=true;}
                continue;
            }
            if(station&&Regex.IsMatch(line,@"^\s*(gameFeatures|subscriptionFeatures)\s*=",RegexOptions.IgnoreCase))continue;
            lines.Add(line);
        }
        if(!added){lines.Add("");lines.AddRange(FeatureDefaults.TrimEnd('\n').Split('\n'));}
        string updated=string.Join("\n",lines).TrimEnd('\n')+"\n";
        if(updated==text)return;
        string backups=Path.Combine(dataRoot??UpdateService.Data,"RuntimeBackups");UpdateService.RejectLinks(backups);Directory.CreateDirectory(backups);
        File.WriteAllBytes(Path.Combine(backups,"live-"+Guid.NewGuid().ToString("N")+".cfg"),original);
        string temp=UpdateService.SafePath(client,".flurry-runtime-"+Guid.NewGuid().ToString("N")+".tmp");
        try{File.WriteAllText(temp,updated,new UTF8Encoding(false));File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
