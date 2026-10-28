using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SwgOldFlurryLauncher;

if (args.Length > 0 && args[0] == "--preview")
{
    Exception? failure = null;
    var thread = new Thread(() => {
        try {
            var app = new System.Windows.Application();
            var window = new LauncherWindow();
            var view = (System.Windows.FrameworkElement)window.Content;
            view.Measure(new System.Windows.Size(1020,690)); view.Arrange(new System.Windows.Rect(0,0,1020,690)); view.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1020,690,96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(view); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(args[1]); encoder.Save(file); window.Close(); app.Shutdown();
        } catch(Exception e) { failure=e; }
    });
    thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
    if(failure!=null)throw failure;
    Console.WriteLine("PREVIEW_RENDERED");return;
}

if (args.Length > 0 && args[0] == "--integration")
{
    var destination=Path.GetFullPath(args[3]);
    if(Directory.Exists(destination))throw new IOException("Integration target must be new.");
    Directory.CreateDirectory(destination);
    var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach(var zipPart in Directory.GetFiles(args[1],"*.zip").Order())
    {
        UpdateService.ExtractBase(zipPart,destination,names,new Progress<Transfer>());
        Console.WriteLine("BASE_EXTRACTED "+Path.GetFileName(zipPart));
    }
    var inventory=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(File.ReadAllText(Path.Combine(args[1],"BASE-FILE-SHA256.json")))!;
    foreach(var pair in inventory)
        if(!pair.Value.GetProperty("sha256").GetString()!.Equals(await UpdateService.Hash(Path.Combine(destination,pair.Key)),StringComparison.OrdinalIgnoreCase))throw new Exception("Base hash mismatch: "+pair.Key);
    Console.WriteLine("BASE_ALL_HASHES_VERIFIED "+inventory.Count);
    var stage=destination+"-assets-stage";var backup=destination+"-assets-backup";
    var assetHash=await UpdateService.Hash(args[2]);
    FlurryAssetInstaller.PrepareInstallation(args[2],stage,assetHash);
    FlurryAssetInstallationEngine.PrepareBackups(stage,destination,backup);
    FlurryAssetInstallationEngine.InstallWithRollback(stage,destination,backup);
    foreach(var file in Directory.GetFiles(stage))
        if(await UpdateService.Hash(file)!=await UpdateService.Hash(Path.Combine(destination,Path.GetFileName(file))))throw new Exception("Asset mismatch");
    foreach(var cfg in new[]{"client.cfg","live.cfg"})
    foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(destination,cfg)),@"(?m)^\s*search(?:Tree|TOC)_\S+\s*=\s*([^\r\n]+)"))
        if(!File.Exists(Path.Combine(destination,match.Groups[1].Value.Trim())))throw new Exception("Missing registered archive");
    var report=new {passed=true,base_files=inventory.Count,asset_files=Directory.GetFiles(stage).Length,base_file_hashes_verified=true,all_registered_archives_present=true,real_asset_package_installed=true};
    await File.WriteAllTextAsync(args[4],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("FRESH_BASE_AND_V1_0_30_INTEGRATION_PASSED");return;
}

int passed = 0;
void Assert(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException) { Assert(true, name); return; } throw new Exception("Accepted: " + name); }
string root = Path.Combine(Path.GetTempPath(), "OldFlurryTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
string Zip(string name, params (string, string)[] entries)
{
    var path = Path.Combine(root, name); using var z = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var (file, content) in entries) { using var writer = new StreamWriter(z.CreateEntry(file).Open()); writer.Write(content); } return path;
}
var progress = new Progress<Transfer>();
using var service = new UpdateService();
try
{
    var package = new Package("1.0.30", "https://github.com/edgewoodnm/SWG-Old-Flurry-Assets/releases/download/v1.0.30/update.zip", 10, new string('A', 64));
    UpdateService.Validate(package, "edgewoodnm/SWG-Old-Flurry-Assets"); Assert(true, "existing four-field manifest accepted");
    Reject(() => UpdateService.Validate(package with { PackageUrl = "https://evil.example/update.zip" }, "edgewoodnm/SWG-Old-Flurry-Assets"), "untrusted host rejected");
    Reject(() => UpdateService.Validate(package with { Size = -1 }, "edgewoodnm/SWG-Old-Flurry-Assets"), "invalid size rejected");
    Reject(() => UpdateService.SafePath(root, "../escape"), "traversal rejected");
    Reject(() => UpdateService.SafePath(root, "C:\\escape"), "absolute path rejected");
    Reject(() => UpdateService.SafePath(root, "CON.txt"), "Windows reserved path rejected");
    var loose = Zip("loose.zip", ("texture/test.dds", "loose"));
    Reject(() => FlurryAssetInstaller.PrepareInstallation(loose, Path.Combine(root, "loose"), UpdateService.Hash(loose).Result), "loose assets rejected");
    var zip = Zip("assets.zip", ("test.tre", "new assets"), ("client.cfg", "new config"));
    var staging = Path.Combine(root, "stage"); var client = Path.Combine(root, "client"); var backup = Path.Combine(root, "backup"); Directory.CreateDirectory(client);
    File.WriteAllText(Path.Combine(client, "SwgClient_r.exe"), "test sentinel"); File.WriteAllText(Path.Combine(client, "test.tre"), "original assets"); File.WriteAllText(Path.Combine(client, "login.cfg"), "private sentinel");
    Reject(() => FlurryAssetInstaller.PrepareInstallation(zip, staging, new string('0',64)), "checksum failure rejected before extraction");
    long extractionDone=0, extractionTotal=0;
    FlurryAssetInstaller.PrepareInstallation(zip, staging, await UpdateService.Hash(zip), (done,total) => { extractionDone=done;extractionTotal=total; });
    Assert(extractionDone==extractionTotal && extractionTotal>0, "extraction reports actual bytes");
    FlurryAssetInstallationEngine.PrepareBackups(staging, client, backup);
    long filesDone=0;
    FlurryAssetInstallationEngine.InstallWithRollback(staging, client, backup, (done,total) => filesDone=done);
    Assert(File.ReadAllText(Path.Combine(client,"test.tre"))=="new assets" && filesDone==2, "assets installed with measured completion");
    Assert(File.ReadAllText(Path.Combine(backup,"test.tre"))=="original assets", "original bytes retained in backup");
    Assert(File.ReadAllText(Path.Combine(client,"login.cfg"))=="private sentinel", "private config preserved");
    var hashes = new Dictionary<string,string> { ["test.tre"] = await UpdateService.Hash(Path.Combine(client,"test.tre")) };
    await File.WriteAllTextAsync(Path.Combine(client,".old-flurry-assets.json"),JsonSerializer.Serialize(new Receipt(package.Version,package.Sha256,hashes)));
    Assert(await service.Current(client,package,progress),"matching receipt and bytes unlock update gate");
    File.WriteAllText(Path.Combine(client,"test.tre"),"corrupt");
    Assert(!await service.Current(client,package,progress),"tampered installed bytes block Play");
    Assert(!await service.Current(client,package with {Version="1.0.31"},progress),"new required version blocks Play");
    var badZip=Zip("bad-path.zip",("../escape.exe","x"));
    Reject(()=>UpdateService.ExtractBase(badZip,Path.Combine(root,"base"),new(StringComparer.OrdinalIgnoreCase),progress),"base traversal rejected");
    var duplicate=Zip("duplicate.zip",("test.dll","1"),("TEST.dll","2"));
    Reject(()=>UpdateService.ExtractBase(duplicate,Path.Combine(root,"base"),new(StringComparer.OrdinalIgnoreCase),progress),"case-colliding base entries rejected");
    var rollbackStage=Path.Combine(root,"rollback-stage");var rollbackClient=Path.Combine(root,"rollback-client");var rollbackBackup=Path.Combine(root,"rollback-backup");
    Directory.CreateDirectory(rollbackStage);Directory.CreateDirectory(rollbackClient);
    File.WriteAllText(Path.Combine(rollbackClient,"SwgClient_r.exe"),"sentinel");
    File.WriteAllText(Path.Combine(rollbackStage,"a.tre"),"new");File.WriteAllText(Path.Combine(rollbackStage,"b.tre"),"new");
    File.WriteAllText(Path.Combine(rollbackClient,"a.tre"),"original");
    FlurryAssetInstallationEngine.PrepareBackups(rollbackStage,rollbackClient,rollbackBackup);
    Directory.CreateDirectory(Path.Combine(rollbackClient,"b.tre"));
    Reject(()=>FlurryAssetInstallationEngine.InstallWithRollback(rollbackStage,rollbackClient,rollbackBackup),"copy failure triggers rollback");
    Assert(File.ReadAllText(Path.Combine(rollbackClient,"a.tre"))=="original","rollback restores previously replaced bytes");
    Console.WriteLine($"ALL {passed} CHECKS PASSED");
}
finally { Directory.Delete(root,true); }
