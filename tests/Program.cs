using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Net;
using System.Net.Http;
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
string root = Path.Combine(Environment.CurrentDirectory, "work", "OldFlurryTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
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
    var baseZip=Zip("tiny-base.zip",("SwgClient_r.exe","runtime sentinel"),("sku0_client.toc","archive sentinel"),("client.cfg","base config"));
    var basePackage=new Package("1.0.0","https://github.com/"+UpdateService.Repository+"/releases/download/base-v1.0.0/base.zip",new FileInfo(baseZip).Length,await UpdateService.Hash(baseZip));
    var fixture=new FixtureHandler(new Dictionary<string,byte[]> {
        [UpdateService.BaseFeed]=JsonSerializer.SerializeToUtf8Bytes(new BaseManifest("1.0.0",[basePackage])),
        [basePackage.PackageUrl]=await File.ReadAllBytesAsync(baseZip),
        [UpdateService.AssetFeed]=JsonSerializer.SerializeToUtf8Bytes(package with {Size=new FileInfo(zip).Length,Sha256=await UpdateService.Hash(zip)}),
        [package.PackageUrl]=await File.ReadAllBytesAsync(zip)
    });
    using var networkService=new UpdateService(fixture,Path.Combine(root,"network-cache"));
    var fresh=Path.Combine(root,"network-fresh-client");
    await networkService.InstallBase(fresh,progress);
    Assert(File.ReadAllText(Path.Combine(fresh,"login.cfg")).Contains("73.127.195.38") && File.Exists(Path.Combine(fresh,"user.cfg")),"fresh installation generates public defaults locally");
    var fetched=await networkService.LatestAssets();
    await networkService.InstallAssets(fresh,fetched,progress);
    Assert(await networkService.Current(fresh,fetched,progress),"download through complete base and asset installation produces verified ready client");
    var history=new RollbackService(Path.Combine(root,"network-cache"));
    Assert(history.Available(fresh),"successful update records rollback history");
    File.WriteAllText(Path.Combine(fresh,"test.tre"),"modified after update");
    bool changedRejected=false;try{await history.Restore(fresh,progress);}catch(IOException){changedRejected=true;}
    Assert(changedRejected && File.ReadAllText(Path.Combine(fresh,"client.cfg"))=="new config","rollback refuses to overwrite later edits and leaves installation intact");
    File.WriteAllText(Path.Combine(fresh,"test.tre"),"new assets");
    await history.Restore(fresh,progress);
    Assert(!File.Exists(Path.Combine(fresh,"test.tre")) && File.ReadAllText(Path.Combine(fresh,"client.cfg"))=="base config","rollback removes new files and restores original bytes");
    Assert(!await networkService.Current(fresh,fetched,progress),"rollback cannot bypass required-update verification");
    Assert(!history.Available(fresh),"completed rollback is not offered twice");
    await networkService.InstallAssets(fresh,fetched,progress);
    Assert(await networkService.Current(fresh,fetched,progress),"launcher repairs a rolled-back installation to the public required package");
    Assert(!await networkService.Current(fresh,fetched with {Version="0.9.0"},progress),"public manifest rollback is detected even when its numeric version is older");
    await networkService.InstallAssets(fresh,fetched with {Version="0.9.0"},progress);
    Assert(await networkService.Current(fresh,fetched with {Version="0.9.0"},progress),"public release rollback installs and verifies an older required version");
    Assert(!await networkService.Current(fresh,fetched,progress),"a later forward release is detected after a public rollback");
    bool nonemptyRejected=false;
    try {await networkService.InstallBase(fresh,progress);}catch(IOException){nonemptyRejected=true;}
    Assert(nonemptyRejected,"fresh base cannot overwrite existing installation");
    var mismatch=basePackage with {Sha256=new string('0',64)};
    bool hashRejected=false;try {await networkService.Download(mismatch,progress);}catch(IOException){hashRejected=true;}
    Assert(hashRejected,"network download checksum mismatch is blocked");
    Assert(!Directory.GetFiles(Path.Combine(root,"network-cache","Downloads"),"*.partial").Any(),"failed downloads remove partial files");
    Console.WriteLine($"ALL {passed} CHECKS PASSED");
}
finally { Directory.Delete(root,true); }

sealed class FixtureHandler(Dictionary<string,byte[]> files) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        string url=request.RequestUri!.GetLeftPart(UriPartial.Path);
        return Task.FromResult(files.TryGetValue(url,out var bytes)
            ?new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)}
            :new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
