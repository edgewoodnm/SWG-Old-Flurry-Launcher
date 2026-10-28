using System.IO;
using System.Text.RegularExpressions;

namespace SwgOldFlurryLauncher;

public static class UsernameMemory
{
    public static bool RememberMostRecent(string client,string? dataRoot=null)
    {
        // The native client writes this account-named file at character selection after login.
        // Read only that local filename, never passwords, process memory or keyboard input.
        if(!Directory.Exists(client))return false;
        UpdateService.RejectLinks(client);
        var latest=new DirectoryInfo(client).EnumerateFiles("characterlist_*.txt",SearchOption.TopDirectoryOnly)
            .Where(f=>(f.Attributes&FileAttributes.ReparsePoint)==0)
            .OrderByDescending(f=>f.LastWriteTimeUtc).FirstOrDefault();
        if(latest==null)return false;
        string username=latest.Name["characterlist_".Length..^".txt".Length];
        if(!Regex.IsMatch(username,@"\A[A-Za-z0-9][A-Za-z0-9 _.-]{0,63}\z"))return false;
        FlurryLoginConfigurator.Configure(client,dataRoot,username);
        return true;
    }
}
