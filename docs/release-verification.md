# Release candidate verification

## Integrated feature candidate

The Windows Release build passed with zero warnings and errors. The test run passed 243 tests and skipped four.

The signed Windows `0.1.1` MSIX with account Home, Mac import, and podcasts passed the historical Roaming AppData upgrade and cold relaunch against the signed `6fabd52` baseline. The package contains the independent Web Home helper and the podcast schema. SHA-256 `aeacf65ca83f9c2bd1432f3d6816378a84a68cbf8324a71c7c8997668e9056bd`.

## Earlier desktop-port candidates

The Windows `0.1.1` x64 candidate used native SQLite 3.54.0. The Release build with transitive NuGet auditing had no warnings or errors. The test run passed 223 tests and skipped four.

| Candidate | Observed result | SHA-256 |
| --- | --- | --- |
| Windows signed MSIX | Installed the `0.1.0` baseline, launched and closed both builds, preserved the library and queue across upgrade, and removed the test package. The smoke used isolated data and cache paths. | `bfba48cb5f3dfd5983bc65ff5bd7798eda6f39853cabeef7aa00e2b3294b6d10` |

A separate Windows credential probe migrated synthetic legacy data to Credential Manager, verified the round trip, and deleted the temporary entry. The historical Windows test installed the signed pre-port `6fabd52` package over a synthetic database at the default Roaming AppData path, then upgraded to the signed `0.1.1` candidate. The old launch, upgrade launch, and cold relaunch each wrote to the same physical database without a package-private main database. Favorites, playlists and duplicate entry order, history, notes, bookmarks, and duplicate queue order survived the upgrade and removal. The test used the current Windows profile after confirming the absence of a Muses package, app data, and account entries. The test package and fixture were removed.

## Automated checks

Run the Release build and tests with an artifacts directory outside the checkout. Use absolute `MUSES_DATA_HOME` and `MUSES_CACHE_HOME` paths in a separate temporary directory for local app runs. The .NET suite checks the MSIX identity and artwork references, Windows release asset selection by architecture, and SQLite and queue behavior.

The CI workflow builds and tests on Windows Server 2025 x64. It runs an offline check of the packaged media tools and uploads the unsigned MSIX with its release metadata. [CI workflow](../.github/workflows/ci.yml)

### Download and update rehearsal

The CI package artifact contains the MSIX, `SHA256SUMS`, and `release-asset-plan.json`. The asset plan records the intended GitHub Release URL, version, architecture and SHA-256. Use the unsigned CI package for inspection. A production package goes through separate timestamped signing and trust verification. [Building Muses](../INSTALL.md#windows-11-x64)

On Windows, generate the checksum and download plan from the exact signed MSIX. Use the expected certificate subject for `$publisher` and the package version for `$version`.

```powershell
py -3 scripts/prepare-release-assets.py $version `
  --windows $windowsMsix --publisher $publisher `
  --output $outputDir
```

The app's stable update check looks for a published `vX.Y.Z` GitHub release and its matching x64 package. For a distribution rehearsal, download the staged assets, verify their SHA-256 values, install the older package, and install the newer package over it. Confirm the application version and retained library and queue. GitHub release publication and the Project-Muses website download links are separate release actions.

### Windows package smoke test

On a Windows 11 x64 desktop test account, start with no installed `xiaotwu.Muses` package. Use Windows PowerShell and Python 3 with `sqlite3`. Build or obtain the signed packages and trust the signer as described in [Building Muses](../INSTALL.md). Both packages must use the same identity and the candidate must have a higher version.

For an independent clean system, stage the signed candidate and authentic pre-port package using the [Windows 11 guest verification steps](windows11-clean-verification.md). The Sandbox runner checks first playback and resume separately from historical AppData upgrade, with only the test transcript written back to the host.

```powershell
powershell.exe -NoProfile -File scripts/test-msix.ps1 `
  -Package (Join-Path $packageDir 'Muses-0.1.1-win-x64.msix') `
  -PreviousPackage (Join-Path $packageDir 'Muses-0.1.0-win-x64.msix')
```

Omit `-PreviousPackage` to check only fresh install, launch and removal. The script sets temporary data and cache roots and leaves signing trust to the test machine setup. CI's unsigned Windows artifact is for packaging checks. Run the signed lifecycle check on the Windows 11 desktop.

For the historical default Roaming AppData path, add `-HistoricalRoaming` and use a signed pre-port build for `-PreviousPackage`. The script requires a profile without an existing Muses installation, data, or account entries. It seeds the pre-port schema, launches without a data-path override, verifies writes to the original database on both versions and after a cold restart, and removes its fixture.

Add `-MediaSmokeVideoId <public-YouTube-video-ID>` to exercise first playback, seek, saved position and paused restoration using the actual installed package. The smoke launches with a process-local system PATH and clears media tool overrides. Use a public video longer than 20 seconds and an available audio output.

The Windows isolated-path check requires a previous build that supports the `MUSES_DATA_HOME` and `MUSES_CACHE_HOME` overrides. The historical Roaming AppData mode supports the pre-port baseline.

## Clean desktop acceptance

Use a clean Windows 11 x64 desktop VM or disposable profile. Record the OS build, desktop session, package version and SHA-256, runtime and media tool versions, and observed results.

| Check | What to record | Owner or dependency |
| --- | --- | --- |
| Install and launch | Package name and architecture, Muses in Start, native window decorations, and normal exit. | Packaging and Windows integration |
| Historical Windows upgrade | In one disposable profile, populate the earlier signed MSIX, quit it, install the candidate over it, and relaunch from Start. Verify the same package family and the old library and queue. In a separate clean profile, repeat with a fixture from the unpackaged `%APPDATA%\Muses` location and launch the candidate without `MUSES_DATA_HOME`. | Packaging and persistence |
| Library and queue | Use distinct YouTube IDs, a favorite, an ordered playlist, history, a note and a queue with a current track and Up Next entries. Check values and order after upgrade and relaunch. | Persistence and playback |
| Media and host integration | Run the prerequisite check, then resolve and play public YouTube audio through yt-dlp and mpv, seek, test failure and retry, and open WebView2 IFrame video separately. Check SMTC, Credential Manager and the notification-area icon in a Windows desktop session. Use a test account for consent and credential checks. [Audio prerequisite check](../INSTALL.md#verify-audio-prerequisites) | Media and Windows integration |
| Visual and interaction comparison | Capture idle and playing states on Windows and compare them against the Polyhymnia reference at `7ce577588f3a8fdd4b8f31fc9c01b541291ffdd3`. Exercise navigation, queue, cover and vinyl Now Playing, keyboard focus, reduced motion and high contrast. | App UI and Windows integration |

The Windows in-place upgrade keeps the existing MSIX family and AppData virtualization behavior. The package-private and unpackaged database checks cover distinct Windows storage cases. [Microsoft MSIX desktop behavior](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)
