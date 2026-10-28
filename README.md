# SWG Old Flurry Launcher

Download **OldFlurryLauncher.exe** from [the latest launcher release](https://github.com/edgewoodnm/SWG-Old-Flurry-Launcher/releases/latest).

1. Run the launcher. It installs itself permanently and creates one Desktop shortcut and a Start Menu shortcut automatically. No administrator account, Git, or separate .NET installation is required.
2. Click the main **Download & Install** button. The launcher downloads the separate base-client packages, verifies each SHA256, and installs the game under `%LOCALAPPDATA%/SWGOldFlurry/Game`.
3. Custom content comes from the existing [Old Flurry public assets feed](https://github.com/edgewoodnm/SWG-Old-Flurry-Assets). Required updates are applied automatically on startup and during idle checks. The same main button shows **Updating** while installing, **Update** when a retry is needed, and **Play** after successful installation and verification.
4. Click **Play**. Keep using this launcher for every session; required updates are checked before the game starts.

This is a dedicated Old Flurry client. It only recognizes `%LOCALAPPDATA%/SWGOldFlurry/Game`. It never scans for other installations, reads old selected-client paths, or attaches to a developer's existing SWG client. A previous Old Flurry client installed in that dedicated location remains recognized. Backups of replaced custom assets remain under `%LOCALAPPDATA%/SWGOldFlurry/Installations`.

To keep the launcher on your Windows taskbar, open the installed launcher, right-click its taskbar icon, and select **Pin to taskbar**. Desktop and Start Menu shortcuts are created by launcher setup. Windows controls taskbar pinning.

## Updates

The launcher checks on startup, every five minutes while idle, and immediately before Play. A new asset release or corrupted required files blocks Play until a successful installation and hash check. If the manifest cannot be checked, Play stays locked and the launcher shows a retry message. This enforcement applies when starting through the launcher.

The window has exactly one action button and a measured progress bar. The artwork is shown at full brightness with transparent control surroundings. Server status refreshes every 30 seconds using the original external-then-internal fallback: `http://73.127.195.38/flurry-status/status.json`, then `http://10.0.0.220/flurry-status/status.json`. It shows Online, Offline, or Unavailable; stale or unreachable status is never presented as Online. These are server status endpoints, not developer-PC paths. This display is informational and separate from required-update verification.

Fresh clients are configured for the public login server. Startup, update checks and Play also repair missing or incorrect `login.cfg` connection settings to `73.127.195.38:44453`, remove obsolete alternate login endpoints, and preserve unrelated local settings. Existing files are backed up locally before changes; owner configuration files are never distributed. The launcher ensures the non-personal `[Station]` game/subscription feature defaults are present in `live.cfg`, so the native client mounts its SKU-tagged archives. This repairs the 2.1 clean-client startup crash without downloading the base again. Launch success is reported only after the game process has a stable window; immediate client exits produce a startup error.

Every launch enforces real fullscreen (`windowed=false`, `borderlessWindow=false`) in both graphics configuration layers using the current player's display mode and refresh rate. For the AMD face-rendering workaround, the launcher checks for AMD physical devices with Vulkan 1.1+ and the installed 32-bit Vulkan loader. Only that supported AMD path receives the verified official DXVK 1.10.1 x32 `d3d9.dll` matching the original working client's fix (SHA256 `bd4e0b3a91f8a45e4c8ff000238b872043fea9afa7a7ba1d82e3cd18f7d65ff7`). NVIDIA/Intel-only and unsupported Vulkan systems keep their existing renderer. A different existing AMD-path renderer is backed up before replacement. The DXVK MIT license accompanies the bundled runtime. This compatibility component belongs to the launcher distribution; the public custom-assets feed remains TRE-only.

After a successful game login, the launcher remembers that player's username in `[ClientGame]` as `loginClientID` in the local `login.cfg`. The next login screen is prefilled; signing in with a different name replaces the remembered name. It uses the native account-named character-list file written at character selection and reads only the filename, never passwords, keyboard input or process memory. It watches while open and checks again next startup. Fresh QA packages contain no saved username. The native behavior is defined in [SwgCuiLoginScreen.cpp](https://github.com/SWG-Source/client-tools/blob/master/src/game/client/library/swgClientUserInterface/src/shared/page/SwgCuiLoginScreen.cpp) and [CuiCachedAvatarManager.cpp](https://github.com/SWG-Source/client-tools/blob/master/src/engine/client/library/clientUserInterface/src/shared/core/CuiCachedAvatarManager.cpp).

Downloads and extraction display actual bytes processed. Backup and installation display verified file counts. Each stage has its own progress bar; no simulated timer is used. A network error or checksum mismatch leaves Play locked. Interrupted base installations use a staging folder and never merge a half-installed base into an existing client. Downloaded verified ZIPs are cached for retry.

## Distribution and privacy

The base is a separate set of independently verified ZIP parts in the `base-v1.0.0` release. Source code stays small; game packages are release assets. The clean upstream runtime is pinned to SWG-Source/client-assets commit `eb107b38d6a75e43cc25d4e335493bd2d330f676`, with the deployed Old Flurry client executable and explicitly selected native archives. Owner profiles, account files, browser databases, logs, screenshots, developer overlays, private repositories and saved login/user configuration are excluded. Public connection defaults and empty personal preferences are generated on each player's PC.

Future custom content releases remain cumulative **TRE-only** packages in the existing assets repository. This launcher does not run the old upstream Git-pull updater over customized clients.

## Rollbacks and repair

Failed installations automatically restore attempted changes. Every completed new-launcher update has a verified backup journal. Support can run the installed launcher with `--rollback` to restore the previous bytes, remove newly added update files, and restore the previous local update receipt. Rollback refuses to overwrite later edits or use damaged backups, and prepares verified recovery copies. Recovery remains available without additional normal UI buttons. Updates performed by the old launcher have no complete journal and cannot be automatically reversed by this feature.

The public manifest remains authoritative. A published rollback or corrected cumulative TRE package is detected even if its version number is lower. The main button changes to **Update**, and the required package is installed and checked before Play. Manually restoring older local files also triggers repair when they differ from the currently required public package. Local rollback does not bypass mandatory updates. To keep everyone on reverted content, publish the intended TRE snapshot in the public feed, preferably as a higher correction version.

## QA distribution

Use the QA ZIP from the latest release. Its explicit contents are the standalone launcher, tester instructions and a launcher checksum. It includes no client directories, saved accounts, profiles, user/login configuration, screenshots, logs, machine paths or private Git data. All installation paths are resolved from the tester's own Windows profile. The bundle uses the public game-server endpoint and the same external/internal server-status endpoints as the original launcher. Keep at least 15 GB free for a new installation and its verified download cache.

See [UPDATE-FORMAT.md](UPDATE-FORMAT.md) for the exact release contract and publication order.

## Build and test

```powershell
dotnet run --project tests/Tests.csproj -c Release
dotnet publish src/Launcher/Launcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o download
```

The launcher targets Windows x64. Launcher 2.1 does not replace its own running executable automatically; future launcher binaries are available from this repository's releases. Close an older installed launcher before running a newer downloaded launcher.

## AMD screenshots (2.1.2)

AMD screenshot update — launcher 2.1.2

Restores the previously working SWG back-buffer screenshot configuration on supported AMD/Vulkan clients using the included face-fix renderer. NVIDIA and Intel-only clients keep existing screenshot settings. The original options.cfg is backed up before repair; personal login/user settings, profiles, game assets and the TRE feed are not changed by this screenshot repair.

Close the old launcher, run this updated launcher once, then launch the game normally. Existing game downloads are reused. Version 2.1.1 does not self-update its executable; installing this new launcher once is required. Thereafter the screenshot workaround is automatically checked before game launch.

This uses an established configuration workaround. Automated configuration and updater checks passed; successful screenshot capture still depends on in-game verification on each affected system.
