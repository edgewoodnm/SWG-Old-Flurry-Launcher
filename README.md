# SWG Old Flurry Launcher

Download **OldFlurryLauncher.exe** from [the latest launcher release](https://github.com/edgewoodnm/SWG-Old-Flurry-Launcher/releases/latest).

1. Run the launcher and choose **Install launcher & shortcuts** to keep it permanently installed. No administrator account or separate .NET installation is required.
2. Click the main **Download & Install** button. The launcher downloads the separate base-client packages, verifies each SHA256, and installs the game under `%LOCALAPPDATA%/SWGOldFlurry/Game`.
3. Custom content comes from the existing [Old Flurry public assets feed](https://github.com/edgewoodnm/SWG-Old-Flurry-Assets). The same main button changes to **Update** when a required package changes, and to **Play** after successful installation and verification.
4. Click **Play**. Keep using this launcher for every session; required updates are checked before the game starts.

Existing players can choose **Use existing client** instead of downloading a fresh base. Backups of replaced custom assets remain under `%LOCALAPPDATA%/SWGOldFlurry/Installations`. Personal settings and profiles are preserved.

To keep the launcher on your Windows taskbar, open the installed launcher, right-click its taskbar icon, and select **Pin to taskbar**. Desktop and Start Menu shortcuts are created by launcher setup. Windows controls taskbar pinning.

## Updates

The launcher checks on startup, every five minutes while idle, and immediately before Play. A new asset release or corrupted required files blocks Play until a successful installation and hash check. If the manifest cannot be checked, Play stays locked and the launcher shows a retry message. This enforcement applies when starting through the launcher.

Downloads and extraction display actual bytes processed. Backup and installation display verified file counts. Each stage has its own progress bar; no simulated timer is used. A network error or checksum mismatch leaves Play locked. Interrupted base installations use a staging folder and never merge a half-installed base into an existing client. Downloaded verified ZIPs are cached for retry.

## Distribution and privacy

The base is a separate set of independently verified ZIP parts in the `base-v1.0.0` release. Source code stays small; game packages are release assets. The clean upstream runtime is pinned to SWG-Source/client-assets commit `eb107b38d6a75e43cc25d4e335493bd2d330f676`, with the deployed Old Flurry client executable and explicitly selected native archives. Owner profiles, account files, browser databases, logs, screenshots, developer overlays, private repositories and saved login/user configuration are excluded. Public connection defaults and empty personal preferences are generated on each player's PC.

Future custom content releases remain cumulative **TRE-only** packages in the existing assets repository. This launcher does not run the old upstream Git-pull updater over customized clients.

## Rollbacks and repair

**Roll Back Last Update** restores the previous bytes, removes files newly added by that update, and restores the previous local update receipt. Every completed new-launcher update has a verified backup journal. Rollback refuses to overwrite files edited since the update or use damaged backups. It prepares verified recovery copies so a failed rollback can restore the current update. Updates performed by the old launcher have no complete journal and cannot be automatically reversed by this feature.

The public manifest remains authoritative. A published rollback or corrected cumulative TRE package is detected even if its version number is lower. The main button changes to **Update**, and the required package is installed and checked before Play. Manually restoring older local files also triggers repair when they differ from the currently required public package. Local rollback does not bypass mandatory updates. To keep everyone on reverted content, publish the intended TRE snapshot in the public feed, preferably as a higher correction version.

See [UPDATE-FORMAT.md](UPDATE-FORMAT.md) for the exact release contract and publication order.

## Build and test

```powershell
dotnet run --project tests/Tests.csproj -c Release
dotnet publish src/Launcher/Launcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o download
```

The launcher targets Windows x64. Launcher 2.0 does not replace its own running executable automatically; future launcher binaries are available from this repository's releases.
