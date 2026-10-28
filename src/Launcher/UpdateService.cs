using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwgOldFlurryLauncher;

public record Transfer(string Stage, long Done, long Total)
{
    public double Percent => Total > 0 ? Math.Clamp(100.0 * Done / Total, 0, 100) : 0;
}
public record Package(string Version, string PackageUrl, long Size, string Sha256);
public record BaseManifest(string Version, List<Package> Packages);
public record Receipt(string Version, string PackageHash, Dictionary<string, string> Files);

public sealed class UpdateService : IDisposable
{
    sealed class ForwardProgress(IProgress<Transfer> target, string suffix) : IProgress<Transfer>
    {
        public void Report(Transfer value) => target.Report(value with { Stage = value.Stage + suffix });
    }
    public const string Repository = "edgewoodnm/SWG-Old-Flurry-Launcher";
    public const string AssetFeed = "https://raw.githubusercontent.com/edgewoodnm/SWG-Old-Flurry-Assets/main/manifest.json";
    public const string BaseFeed = "https://raw.githubusercontent.com/" + Repository + "/main/base-client.json";
    public static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SWGOldFlurry");
    readonly HttpClient http;
    readonly string dataRoot;
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public UpdateService(HttpMessageHandler? handler = null, string? dataRoot = null)
    {
        http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromHours(4);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OldFlurryLauncher/2.0");
        this.dataRoot = dataRoot ?? Data;
    }
    public void Dispose() => http.Dispose();

    public static void Validate(Package p, string repository)
    {
        if (!Version.TryParse(p.Version, out _) || p.Size <= 0 || p.Size > 2_000_000_000 ||
            !Regex.IsMatch(p.Sha256 ?? "", "\\A[0-9a-fA-F]{64}\\z") ||
            !Uri.TryCreate(p.PackageUrl, UriKind.Absolute, out var u) || u.Scheme != "https" ||
            u.Host != "github.com" || !u.IsDefaultPort || u.UserInfo.Length != 0 ||
            !u.AbsolutePath.StartsWith("/" + repository + "/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid or untrusted release manifest.");
    }

    public async Task<Package> LatestAssets()
    {
        var p = await Read<Package>(AssetFeed);
        Validate(p, "edgewoodnm/SWG-Old-Flurry-Assets");
        return p;
    }
    async Task<T> Read<T>(string url)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await http.GetAsync(url + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), timeout.Token);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(timeout.Token), Json)
            ?? throw new InvalidDataException("Empty update manifest.");
    }
    public static string SafePath(string root, string name)
    {
        var parts = name.Replace('\\', '/').Split('/');
        if (parts.Any(x => x.Length == 0 || x is "." or ".." || x.EndsWith(' ') || x.EndsWith('.') ||
            x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            Regex.IsMatch(x.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Unsafe package path.");
        var full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package escapes install directory.");
        RejectLinks(full);
        return full;
    }
    public static void RejectLinks(string path)
    {
        for (string? p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked installation paths are not supported.");
    }
    public static async Task<string> Hash(string path) { await using var f = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(f)); }
    public async Task<string> Download(Package package, IProgress<Transfer> progress)
    {
        Directory.CreateDirectory(Path.Combine(dataRoot, "Downloads"));
        var path = Path.Combine(dataRoot, "Downloads", package.Sha256.ToUpperInvariant() + ".zip");
        if (File.Exists(path) && new FileInfo(path).Length == package.Size &&
            (await Hash(path)).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase)) return path;
        var partial = path + ".partial";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromHours(4));
            using var response = await http.GetAsync(package.PackageUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != package.Size) throw new IOException("Download size does not match manifest.");
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[131072]; long done = 0; int count;
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while ((count = await source.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    done += count;
                    if (done > package.Size) throw new IOException("Download exceeds declared size.");
                    await target.WriteAsync(buffer.AsMemory(0, count));
                    if (timer.ElapsedMilliseconds > 75 || done == package.Size) { progress.Report(new("Downloading", done, package.Size)); timer.Restart(); }
                }
                if (done != package.Size) throw new IOException("Incomplete download. Retry to download again.");
            }
            progress.Report(new("Verifying download", 0, package.Size));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var file = File.OpenRead(partial))
            {
                byte[] buffer = new byte[131072]; long done = 0; int n;
                while ((n = await file.ReadAsync(buffer)) > 0) { hash.AppendData(buffer, 0, n); done += n; progress.Report(new("Verifying download", done, package.Size)); }
            }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Download checksum mismatch.");
            File.Move(partial, path, true);
            return path;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    public async Task<bool> Current(string client, Package p, IProgress<Transfer> progress)
    {
        var path = Path.Combine(client, ".old-flurry-assets.json");
        if (!File.Exists(path)) return false;
        Receipt? receipt;
        try { receipt = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(path), Json); }
        catch (JsonException) { return false; }
        if (receipt == null || receipt.Version != p.Version || !receipt.PackageHash.Equals(p.Sha256, StringComparison.OrdinalIgnoreCase) || receipt.Files.Count == 0) return false;
        long total = receipt.Files.Count; long done = 0;
        foreach (var pair in receipt.Files)
        {
            var file = SafePath(client, pair.Key);
            if (!File.Exists(file) || !pair.Value.Equals(await Hash(file), StringComparison.OrdinalIgnoreCase)) return false;
            progress.Report(new("Checking installed assets", ++done, total));
        }
        return true;
    }
    public async Task InstallAssets(string client, Package package, IProgress<Transfer> progress)
    {
        FlurryInstallationSafety.ValidateClientReady(client);
        RejectLinks(client);
        var zip = await Download(package, progress);
        var session = new FlurryInstallationSession(dataRoot);
        var rollback = new RollbackService(dataRoot);
        string history = "";
        await Task.Run(() =>
        {
            // Retain the original launcher's ZIP limits, path validation, verified backups and rollback engine.
            FlurryAssetInstaller.PrepareInstallation(zip, session.StagingDirectory, package.Sha256,
                (done, total) => progress.Report(new("Extracting assets", done, total)));
            FlurryInstallationSafety.ValidateClientReady(client);
            FlurryAssetInstallationEngine.PrepareBackups(session.StagingDirectory, client, session.BackupDirectory,
                (done, total) => progress.Report(new("Backing up assets", done, total)));
        });
        history = await rollback.Capture(client, session, package);
        await Task.Run(() =>
        {
            FlurryInstallationSafety.ValidateClientReady(client);
            FlurryAssetInstallationEngine.InstallWithRollback(session.StagingDirectory, client, session.BackupDirectory,
                (done, total) => progress.Report(new("Installing and verifying assets", done, total)));
        });
        var hashes = new Dictionary<string, string>();
        foreach (var file in Directory.GetFiles(session.StagingDirectory, "*", SearchOption.AllDirectories))
            hashes.Add(Path.GetRelativePath(session.StagingDirectory, file), await Hash(file));
        // A failed operation never records success. Subsequent Play always checks actual installed bytes.
        var receiptPath = Path.Combine(client, ".old-flurry-assets.json");
        RejectLinks(receiptPath);
        RejectLinks(receiptPath + ".tmp");
        await File.WriteAllTextAsync(receiptPath + ".tmp", JsonSerializer.Serialize(new Receipt(package.Version, package.Sha256, hashes), Json));
        File.Move(receiptPath + ".tmp", receiptPath, true);
        await rollback.Complete(history);
        Directory.Delete(session.StagingDirectory, true);
    }

    public async Task InstallBase(string destination, IProgress<Transfer> progress)
    {
        RejectLinks(destination);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("Choose an empty folder for the new client.");
        var manifest = await Read<BaseManifest>(BaseFeed);
        if (!Version.TryParse(manifest.Version, out _) || manifest.Packages.Count is < 1 or > 32) throw new InvalidDataException("Invalid base-client manifest.");
        foreach (var package in manifest.Packages) Validate(package, Repository);
        long baseBytes = manifest.Packages.Sum(p => p.Size);
        var destinationDrive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!);
        if (destinationDrive.AvailableFreeSpace < baseBytes + 1_073_741_824L)
            throw new IOException("There is not enough free space for the base client and installation workspace.");
        var stage = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, ".oldflurry-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int part = 0;
            foreach (var package in manifest.Packages)
            {
                var partProgress = new ForwardProgress(progress, $" — part {++part}/{manifest.Packages.Count}");
                var zip = await Download(package, partProgress);
                await Task.Run(() => ExtractBase(zip, stage, names, partProgress));
            }
            if (!File.Exists(Path.Combine(stage, "SwgClient_r.exe")) || !File.Exists(Path.Combine(stage, "sku0_client.toc"))) throw new InvalidDataException("Base client is incomplete.");
            // Generate public connection defaults locally; no owner's login/user configuration is distributed.
            await File.WriteAllTextAsync(Path.Combine(stage, "login.cfg"), "[ClientGame]\nloginServerAddress0=73.127.195.38\nloginServerPort0=44453\n");
            await File.WriteAllTextAsync(Path.Combine(stage, "user.cfg"), "# Personal preferences are stored locally.\n");
            // Recheck after a potentially long download. Never merge a fresh base into an existing client.
            RejectLinks(destination);
            if (Directory.Exists(destination)) Directory.Delete(destination, false);
            Directory.Move(stage, destination);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public static void ExtractBase(string zip, string stage, HashSet<string> names, IProgress<Transfer> progress)
    {
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count > 10000) throw new InvalidDataException("Too many base files.");
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Name.Length == 0) continue;
            SafePath(stage, entry.FullName);
            if (!names.Add(entry.FullName.Replace('\\','/')) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || entry.Length > 2_000_000_000)
                throw new InvalidDataException("Invalid base package entry.");
            total = checked(total + entry.Length);
            if (total > 4_000_000_000) throw new InvalidDataException("Base package exceeds extraction limit.");
        }
        long done = 0; byte[] buffer = new byte[131072];
        foreach (var entry in archive.Entries)
        {
            if (entry.Name.Length == 0) continue;
            var path = SafePath(stage, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = entry.Open(); using var target = new FileStream(path, FileMode.CreateNew);
            int n; long entryDone = 0;
            while ((n = source.Read(buffer)) > 0)
            {
                entryDone += n; if (entryDone > entry.Length) throw new InvalidDataException("Expanded entry exceeds declared length.");
                target.Write(buffer, 0, n); done += n;
                progress.Report(new("Installing base client", done, total));
            }
            if (entryDone != entry.Length) throw new InvalidDataException("Truncated base entry.");
        }
    }
}
