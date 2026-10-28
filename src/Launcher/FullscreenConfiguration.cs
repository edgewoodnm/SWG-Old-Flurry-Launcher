using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SwgOldFlurryLauncher;

public static class FullscreenConfiguration
{
    [StructLayout(LayoutKind.Explicit,Size=220)] struct DisplayMode
    {
        [FieldOffset(68)]public ushort Size;
        [FieldOffset(172)]public int Width;
        [FieldOffset(176)]public int Height;
        [FieldOffset(184)]public int Hertz;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool EnumDisplaySettings(string? device,int mode,ref DisplayMode settings);
    public static void Configure(string client,string? dataRoot=null,int? width=null,int? height=null,int? hertz=null)
    {
        var mode=new DisplayMode{Size=220};bool current=EnumDisplaySettings(null,-1,ref mode);
        int w=width??(current&&mode.Width>=640?mode.Width:1280),h=height??(current&&mode.Height>=480?mode.Height:720),hz=hertz??(current&&mode.Hertz>=30?mode.Hertz:60);
        var settings=new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ClientGraphics"]=new(StringComparer.OrdinalIgnoreCase){["windowed"]="false",["borderlessWindow"]="false",["screenWidth"]=w.ToString(),["screenHeight"]=h.ToString()},
            ["Direct3d9"]=new(StringComparer.OrdinalIgnoreCase){["fullscreenRefreshRate"]=hz.ToString()}
        };
        foreach(string name in new[]{"options.cfg","user.cfg"})
        {
            string path=UpdateService.SafePath(client,name);byte[]? original=File.Exists(path)?File.ReadAllBytes(path):null;
            if(original!=null&&original.Contains((byte)0))throw new InvalidDataException("Unsupported graphics configuration encoding.");
            string text=original==null?"":new UTF8Encoding(false,true).GetString(original).TrimStart('\uFEFF');
            var lines=new List<string>();var added=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string section="";
            foreach(string line in Regex.Split(text,"\r\n|\n|\r"))
            {
                var header=Regex.Match(line,@"^\s*\[([^\]]+)\]\s*(?:[#;].*)?$");
                if(header.Success)
                {
                    section=header.Groups[1].Value;lines.Add(line);
                    if(settings.TryGetValue(section,out var values)&&added.Add(section))foreach(var p in values)lines.Add(p.Key+"="+p.Value);
                    continue;
                }
                var key=Regex.Match(line,@"^\s*([^=\s]+)\s*=");
                if(settings.TryGetValue(section,out var keys)&&key.Success&&keys.ContainsKey(key.Groups[1].Value))continue;
                lines.Add(line);
            }
            foreach(var s in settings)if(added.Add(s.Key)){lines.Add("");lines.Add("["+s.Key+"]");foreach(var p in s.Value)lines.Add(p.Key+"="+p.Value);}
            string updated=string.Join("\n",lines).TrimEnd('\n')+"\n";if(updated==text)continue;
            if(original!=null)
            {
                string backup=Path.Combine(dataRoot??UpdateService.Data,"RuntimeBackups");UpdateService.RejectLinks(backup);Directory.CreateDirectory(backup);
                File.WriteAllBytes(Path.Combine(backup,Path.GetFileNameWithoutExtension(name)+"-"+Guid.NewGuid().ToString("N")+".cfg"),original);
            }
            string temp=UpdateService.SafePath(client,".flurry-fullscreen-"+Guid.NewGuid().ToString("N")+".tmp");
            try{File.WriteAllText(temp,updated,new UTF8Encoding(false));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
