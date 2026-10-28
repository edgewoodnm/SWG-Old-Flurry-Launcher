using System.Diagnostics;
using System.IO;

namespace SwgOldFlurryLauncher;

public record GameLaunchResult(int ProcessId,bool WindowOpened);
public static class GameLaunchService
{
    public static async Task<GameLaunchResult> Start(string client)
    {
        RuntimeConfiguration.Repair(client);
        await Task.Run(()=>RendererCompatibility.Prepare(client));
        string exe=UpdateService.SafePath(client,"SwgClient_r.exe");
        if(!File.Exists(exe))throw new IOException("The installed Old Flurry executable is missing.");
        using var process=Process.Start(new ProcessStartInfo(exe){WorkingDirectory=client,UseShellExecute=true})??throw new IOException("Windows did not start the game.");
        bool window=await ObserveStartup(process,TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(5));
        return new(process.Id,window);
    }
    public static async Task<bool> ObserveStartup(Process process,TimeSpan timeout,TimeSpan settle)
    {
        var elapsed=Stopwatch.StartNew();TimeSpan? firstWindow=null;
        while(elapsed.Elapsed<timeout)
        {
            process.Refresh();
            if(process.HasExited)throw new IOException($"Old Flurry exited during startup (0x{process.ExitCode:X8}). The game did not open. Check the client crash report before retrying.");
            if(process.MainWindowHandle!=IntPtr.Zero)
            {
                firstWindow??=elapsed.Elapsed;
                if(elapsed.Elapsed-firstWindow.Value>=settle)return true;
            }
            else firstWindow=null;
            await Task.Delay(200);
        }
        process.Refresh();
        if(process.HasExited)throw new IOException($"Old Flurry exited during startup (0x{process.ExitCode:X8}). The game did not open.");
        return false;
    }
}
