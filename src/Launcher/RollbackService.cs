using System.IO;
using System.Text.Json;

namespace SwgOldFlurryLauncher;

public record FileChange(string Name, string? BeforeHash, string AfterHash);
public record UpdateHistory(string Client, string Version, long CreatedUtc, string State, string? PreviousReceipt, List<FileChange> Files);

public sealed class RollbackService(string? dataRoot = null)
{
    readonly string root = Path.Combine(dataRoot ?? UpdateService.Data, "Installations");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static string Receipt(string client) => UpdateService.SafePath(client, ".old-flurry-assets.json");

    public async Task<string> Capture(string client, FlurryInstallationSession session, Package package)
    {
        var changes = new List<FileChange>();
        foreach (var file in Directory.GetFiles(session.StagingDirectory, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetRelativePath(session.StagingDirectory, file);
            string target = UpdateService.SafePath(client, name);
            string after = await UpdateService.Hash(file);
            string? before = File.Exists(target) ? await UpdateService.Hash(target) : null;
            if (before != after) changes.Add(new(name, before, after));
        }
        string? receipt = File.Exists(Receipt(client)) ? await File.ReadAllTextAsync(Receipt(client)) : null;
        var history = new UpdateHistory(Path.GetFullPath(client), package.Version, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "Prepared", receipt, changes);
        string path = Path.Combine(session.SessionDirectory, "transaction.json");
        await Save(path, history);
        return path;
    }
    static async Task Save(string path, UpdateHistory history)
    {
        UpdateService.RejectLinks(path); UpdateService.RejectLinks(path + ".tmp");
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(history, Json));
        File.Move(path + ".tmp", path, true);
    }
    public async Task Complete(string path)
    {
        var history = JsonSerializer.Deserialize<UpdateHistory>(await File.ReadAllTextAsync(path), Json)!;
        await Save(path, history with { State = "Installed" });
    }
    (string Path, UpdateHistory History)? Latest(string client)
    {
        if (!Directory.Exists(root)) return null;
        UpdateService.RejectLinks(root);
        var histories = new List<(string Path, UpdateHistory History)>();
        foreach (var directory in Directory.GetDirectories(root))
        {
            UpdateService.RejectLinks(directory);
            string path = Path.Combine(directory, "transaction.json");
            if (!File.Exists(path)) continue;
            UpdateService.RejectLinks(path);
            try
            {
                var h = JsonSerializer.Deserialize<UpdateHistory>(File.ReadAllText(path), Json);
                if (h != null && h.State == "Installed" && Path.GetFullPath(client).Equals(h.Client, StringComparison.OrdinalIgnoreCase)) histories.Add((path,h));
            }
            catch (JsonException) { }
        }
        return histories.OrderByDescending(x => x.History.CreatedUtc).Select(x => ((string Path,UpdateHistory History)?)x).FirstOrDefault();
    }
    public bool Available(string client) => Latest(client) != null;
    public async Task<string> Restore(string client, IProgress<Transfer> progress)
    {
        FlurryInstallationSafety.ValidateClientReady(client);
        var entry = Latest(client) ?? throw new IOException("There is no completed update to roll back for this client.");
        var history = entry.History;
        var backup = Path.Combine(Path.GetDirectoryName(entry.Path)!, "Backups");
        // Verify the complete plan before touching any installed file. Preserve changes made since the update.
        foreach (var change in history.Files)
        {
            string target = UpdateService.SafePath(client, change.Name);
            if (!File.Exists(target) || await UpdateService.Hash(target) != change.AfterHash)
                throw new IOException("Rollback stopped because a file changed after the update: " + change.Name + ". Repair or preserve that change first.");
            if (change.BeforeHash != null)
            {
                string original = UpdateService.SafePath(backup, change.Name);
                if (!File.Exists(original) || await UpdateService.Hash(original) != change.BeforeHash)
                    throw new IOException("Rollback backup is missing or damaged: " + change.Name);
            }
        }
        string recovery = Path.Combine(Path.GetDirectoryName(entry.Path)!, "rollback-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(recovery);
        foreach (var change in history.Files)
        {
            string copy = UpdateService.SafePath(recovery, change.Name); Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(UpdateService.SafePath(client,change.Name), copy, false);
            if (await UpdateService.Hash(copy) != change.AfterHash) throw new IOException("Rollback recovery copy failed verification.");
        }
        string? currentReceipt = File.Exists(Receipt(client)) ? await File.ReadAllTextAsync(Receipt(client)) : null;
        var attempted = new List<FileChange>();
        try
        {
            FlurryInstallationSafety.ValidateClientReady(client);
            long done = 0;
            foreach (var change in history.Files)
            {
                attempted.Add(change); string target = UpdateService.SafePath(client,change.Name);
                if (change.BeforeHash == null) File.Delete(target);
                else
                {
                    File.Copy(UpdateService.SafePath(backup,change.Name),target,true);
                    if (await UpdateService.Hash(target) != change.BeforeHash) throw new IOException("Restored file failed verification.");
                }
                progress.Report(new("Restoring previous update",++done,history.Files.Count));
            }
            await WriteReceipt(client, history.PreviousReceipt);
            await Save(entry.Path, history with {State="RolledBack"});
        }
        catch (Exception error)
        {
            var failures = new List<string>();
            foreach (var change in attempted.AsEnumerable().Reverse())
            {
                try
                {
                    string target = UpdateService.SafePath(client,change.Name);
                    File.Copy(UpdateService.SafePath(recovery,change.Name),target,true);
                    if(await UpdateService.Hash(target)!=change.AfterHash)throw new IOException("Recovery hash mismatch.");
                }
                catch(Exception e) {failures.Add(change.Name+": "+e.Message);}
            }
            try {await WriteReceipt(client,currentReceipt);}catch(Exception e){failures.Add(e.Message);}
            if(failures.Count>0)throw new IOException("Rollback failed and recovery needs attention. Recovery folder: "+recovery+". "+string.Join("; ",failures),error);
            throw new IOException("Rollback failed; the current update was restored. "+error.Message,error);
        }
        return "Previous files restored. Required release checks still apply before Play.";
    }
    static async Task WriteReceipt(string client,string? text)
    {
        string path=Receipt(client);UpdateService.RejectLinks(path+".tmp");
        if(text==null){if(File.Exists(path))File.Delete(path);return;}
        await File.WriteAllTextAsync(path+".tmp",text);File.Move(path+".tmp",path,true);
    }
}
