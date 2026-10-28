# Existing custom asset update contract

Feed: `https://raw.githubusercontent.com/edgewoodnm/SWG-Old-Flurry-Assets/main/manifest.json`

```json
{
  "version": "1.0.30",
  "packageUrl": "https://github.com/edgewoodnm/SWG-Old-Flurry-Assets/releases/download/v1.0.30/SWG-Old-Flurry-Assets-v1.0.30.zip",
  "size": 330842502,
  "sha256": "716437372CE801C92DBB11BF3DF6E692859D1C061FF11EB3A70AA4AD2AB911AC"
}
```

`size` is the exact ZIP byte count. `sha256` is the SHA256 of the complete ZIP. Both upper- and lowercase hexadecimal are supported. The package URL must use HTTPS and the existing repository's GitHub Release download path. Versions use numeric dot-separated .NET Version syntax.

The v1.0.30 ZIP contains `swgsource_3.0.tre`, `old_flurry_halloween_01.tre`, `old_flurry_halloween_02.tre`, `old_flurry_halloween_03.tre`, and `client.cfg` at the ZIP root. All previous custom content remains included. The registration configuration includes login/live/preload/options/user configs, registers the source archive at priority 8 and custom TREs at 19–21, sets maxSearchPriority=21 and searchAbsolute=18. Preserve those relationships when packaging later releases. The launcher verifies and installs `client.cfg` as part of the update.

Custom assets must be inside TRE archives. Do not add loose texture, appearance, object, UI, string, terrain, executable or script payloads. `client.cfg` is the only permitted non-TRE installation payload. Retain the existing limits: no more than 1,000 ZIP entries; no extracted file larger than 104,857,600 bytes; no more than 524,288,000 bytes extracted in total. Split larger cumulative TREs while retaining registrations and priority ordering. The launcher rejects unsafe paths, duplicate names, symbolic links, corrupt packages and packages outside these limits.

Publish a higher numeric version for every correction. Upload the cumulative ZIP to a draft GitHub Release, verify the uploaded exact size and SHA256, publish the release, and only then commit the new main/manifest.json. Never point a manifest at a draft, unfinished upload or unverified replacement. Do not reuse release ZIP URLs for different bytes. Historical loose asset directories in Git are not downloaded by this launcher; only the manifest's ZIP is installed.

Every latest custom asset package is mandatory for this launcher. There is no new `forceUpdate` or `minimumVersion` field required in the existing feed. The launcher records the installed package hash plus individual installed file hashes locally and rechecks them before Play. Settings and profiles are outside that receipt and remain local.

Rollback detection compares both the version and exact package hash; it does not assume only increasing versions are valid. A manifest pointing back to an older trusted TRE-only release is therefore detected and installed. Prefer publishing a higher-numbered corrective release containing the desired reverted TRE content, so older launchers and release history remain easy to follow. Never point this new TRE-only installer at historical loose-asset ZIPs. After a local rollback, required-release verification detects the restored state and repairs it. Support recovery through `--rollback` uses per-update verified backups and a transaction journal; it cannot reconstruct complete rollback history for installations performed by the old launcher. The normal desktop UI retains one button.

# Separate base-client manifest

`base-client.json` belongs to the new launcher repository, never the existing asset feed:

```json
{
  "version": "1.0.0",
  "packages": [
    {
      "version": "1.0.0",
      "packageUrl": "https://github.com/edgewoodnm/SWG-Old-Flurry-Launcher/releases/download/base-v1.0.0/Old-Flurry-Base-1.0.0-part01.zip",
      "size": 123,
      "sha256": "<exact 64-character SHA256>"
    }
  ]
}
```

Each part is a standalone ZIP containing disjoint root-relative files, not a split binary archive requiring 7-Zip. The launcher downloads, validates and extracts all parts into one new staging directory. After the complete base passes basic validation, it generates public login defaults and empty user preferences locally and moves the staged base into the selected empty folder. It then applies the current custom TRE package from the original feed. Base packages are only used for fresh installations; future routine content updates continue through the TRE feed.

Keep each download below 2,000,000,000 bytes and each base part below 4,000,000,000 extracted bytes. Upload and verify every base part before publishing base-client.json. Do not ship owner login.cfg/user.cfg, account names, character lists, browser databases, profiles, crash dumps, saved settings or private Git history.
