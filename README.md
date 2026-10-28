# SWG Old Flurry Launcher

Download **OldFlurryLauncher.exe** from [the latest launcher release](https://github.com/edgewoodnm/SWG-Old-Flurry-Launcher/releases/latest).

1. Run the launcher and choose **Install launcher & shortcuts** to keep it permanently installed. No administrator account or separate .NET installation is required.
2. Choose **Install new client**, then an empty folder. The launcher downloads the separate base-client packages, verifies each SHA256, and installs them.
3. Custom content comes from the existing [Old Flurry public assets feed](https://github.com/edgewoodnm/SWG-Old-Flurry-Assets). Click **Check / Update** when required.
4. Choose **Play**. Keep using this launcher for every session; required updates are checked before the game starts.

Existing players can choose **Use existing client** instead of downloading a fresh base. Backups of replaced custom assets remain under `%LOCALAPPDATA%/SWGOldFlurry/Installations`. Personal settings and profiles are preserved.

To keep the launcher on your Windows taskbar, open the installed launcher, right-click its taskbar icon, and select **Pin to taskbar**. Desktop and Start Menu shortcuts are created by launcher setup. Windows controls taskbar pinning.

## Updates

The launcher checks on startup, every five minutes while idle, and immediately before Play. A new asset release or corrupted required files blocks Play until a successful installation and hash check. If the manifest cannot be checked, Play stays locked and the launcher shows a retry message. This enforcement applies when starting through the launcher.

Downloads and extraction display actual bytes processed. Backup and installation display verified file counts. Each stage has its own progress bar; no simulated timer is used. A network error or checksum mismatch leaves Play locked. Interrupted base installations use a staging folder and never merge a half-installed base into an existing client. Downloaded verified ZIPs are cached for retry.

## Distribution and privacy

The base is a separate set of independently verified ZIP parts in the `base-v1.0.0` release. Source code stays small; game packages are release assets. The clean upstream runtime is pinned to SWG-Source/client-assets commit `eb107b38d6a75e43cc25d4e335493bd2d330f676`, with the deployed Old Flurry client executable and explicitly selected native archives. Owner profiles, account files, browser databases, logs, screenshots, developer overlays, private repositories and saved login/user configuration are excluded. Public connection defaults and empty personal preferences are generated on each player's PC.

Future custom content releases remain cumulative **TRE-only** packages in the existing assets repository. This launcher does not run the old upstream Git-pull updater over customized clients.

See [UPDATE-FORMAT.md](UPDATE-FORMAT.md) for the exact release contract and publication order.

## Build and test

```powershell
dotnet run --project tests/Tests.csproj -c Release
dotnet publish src/Launcher/Launcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o download
```

The launcher targets Windows x64. Launcher 2.0 does not replace its own running executable automatically; future launcher binaries are available from this repository's releases.
