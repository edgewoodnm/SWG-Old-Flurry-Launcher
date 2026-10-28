using System.IO;

namespace SwgOldFlurryLauncher;

public static class ManagedClient
{
    public static string Location(string? dataRoot = null) => Path.Combine(dataRoot ?? UpdateService.Data, "Game");
    public static bool Installed(string? dataRoot = null)
    {
        // Only the dedicated install location is eligible. Never read SelectedClient.txt or scan other installations.
        string folder = Location(dataRoot);
        UpdateService.RejectLinks(folder);
        return File.Exists(Path.Combine(folder,"SwgClient_r.exe")) && File.Exists(Path.Combine(folder,"sku0_client.toc"));
    }
}
