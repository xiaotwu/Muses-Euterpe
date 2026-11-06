# Release candidate verification

## Integrated feature candidate

Release builds passed with zero warnings and errors. Windows passed 243 tests and skipped four Linux-specific tests. Ubuntu 26.04 under WSL2 passed 245 tests and skipped the two cases that require a Secret Service provider in the test session.

The signed Windows `0.1.1` MSIX with account Home, Mac import, and podcasts passed the historical Roaming AppData upgrade and cold relaunch against the signed `6fabd52` baseline. The package contains the independent Web Home helper and the podcast schema. SHA-256 `aeacf65ca83f9c2bd1432f3d6816378a84a68cbf8324a71c7c8997668e9056bd`.

## Earlier desktop-port candidates

The `0.1.1` x64 candidates used native SQLite 3.54.0. Release builds with transitive NuGet auditing had no warnings or errors. Windows passed 223 tests and skipped four Linux-specific tests. Ubuntu 26.04 passed all 227 tests across separate D-Bus sessions with and without Secret Service.

| Candidate | Observed result | SHA-256 |
| --- | --- | --- |
| Windows signed MSIX | Installed the `0.1.0` baseline, launched and closed both builds, preserved the library and queue across upgrade, and removed the test package. The smoke used isolated data and cache paths. | `bfba48cb5f3dfd5983bc65ff5bd7798eda6f39853cabeef7aa00e2b3294b6d10` |
| Ubuntu 24.04 x64 `.deb` | Passed fresh APT install and launch under Xvfb, followed by upgrade from the legacy XDG location and schema, and removal. | `824d3bb2c3eab642e29af511b2555d166d15b65373852a67b35812250dd184e0` |

A separate Windows credential probe migrated synthetic legacy data to Credential Manager, verified the round trip, and deleted the temporary entry. The historical Windows test installed the signed pre-port `6fabd52` package over a synthetic database at the default Roaming AppData path, then upgraded to the signed `0.1.1` candidate. The old launch, upgrade launch, and cold relaunch each wrote to the same physical database without a package-private main database. Favorites, playlists and duplicate entry order, history, notes, bookmarks, and duplicate queue order survived the upgrade and removal. The test used the current Windows profile after confirming the absence of a Muses package, app data, and account entries. The test package and fixture were removed.

The Ubuntu 24.04 package run used Xvfb. The IFrame and Linux desktop integration results came from Ubuntu 26.04 under WSL2.

## Automated checks

Run the Release build and tests with an artifacts directory outside the checkout. Use absolute `MUSES_DATA_HOME` and `MUSES_CACHE_HOME` paths in a separate temporary directory for local app runs. The .NET suite checks the MSIX identity and artwork references, release asset selection for each platform and architecture, and SQLite and queue behavior.

The [CI workflow](../.github/workflows/ci.yml) builds on Ubuntu 24.04 x64 and Windows Server 2025 x64. It runs an offline check of the installed media tools and uploads both packages as workflow artifacts. The Ubuntu job uses Xvfb to exercise fresh installation, launch, upgrade and removal. Its baseline and candidate come from the same source revision. The upgrade fixture reproduces the pre-port library and queue column layout from `6fabd52` in the previous XDG config location.

### Download and update rehearsal

Each CI package artifact contains its installer, `SHA256SUMS`, and `release-asset-plan.json`. The asset plan records the intended GitHub Release URL, version, architecture and SHA-256. An unsigned Windows CI package is for inspection. A production Windows package goes through the separate timestamped signing and trust verification in [Building Muses](../INSTALL.md#windows-11-x64).

On Linux or WSL, combine both exact package files into a checksum and download plan. Use the expected certificate subject for `$publisher` and the version of both files for `$version`.

```sh
python3 scripts/prepare-release-assets.py "$version" \
  --windows "$windows_msix" --linux "$linux_deb" --publisher "$publisher" \
  --output "$output_dir"
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

### Ubuntu package smoke test

On Ubuntu 24.04 x64, start with no `muses` package installed. From the repository root, use an X11 session or run under Xvfb with a private D-Bus session. The smoke test uses APT for install and removal, Python 3 for the SQLite fixture and `python3-xlib` to observe and close the installed app window. Run the test as the desktop user with `sudo` available for APT.

```sh
sudo apt-get update
sudo apt-get install -y dbus xvfb xauth python3-xlib
package_dir=$(mktemp -d)
bash scripts/package-deb.sh 0.1.0 "$package_dir"
bash scripts/package-deb.sh 0.1.1 "$package_dir"
xvfb-run -a dbus-run-session -- bash scripts/test-deb.sh \
  "$package_dir/Muses-0.1.1-linux-x64.deb" \
  "$package_dir/Muses-0.1.0-linux-x64.deb"
```

Omit the second package to check fresh install, launch and removal from the normal XDG data location. With an older package, the test creates the legacy XDG config database and verifies that the upgraded app writes the migrated library in the XDG data folder while the old database stays intact. Both checks use temporary XDG directories and a private D-Bus session. Set `MUSES_SMOKE_VIDEO_ID` to a public YouTube video ID to exercise live first playback and resume with the package media tools. Use an available audio output for the live check and a separate desktop session under XWayland for compositor and desktop integration checks.

The Windows isolated-path check requires a previous build that supports the `MUSES_DATA_HOME` and `MUSES_CACHE_HOME` overrides. The historical Roaming AppData mode supports the pre-port baseline.

## Clean desktop acceptance

Use clean Windows 11 x64 and Ubuntu 24.04 LTS x64 desktop VMs or disposable profiles. Record the OS build, desktop session and display server, package version and SHA-256, runtime and media tool versions, and observed results.

| Check | What to record | Owner or dependency |
| --- | --- | --- |
| Install and launch | Package name and architecture, Muses in Start or the application menu, native window decorations, and normal exit. | Packaging and desktop hosts |
| Historical Windows upgrade | In one disposable profile, populate the earlier signed MSIX, quit it, install the candidate over it, and relaunch from Start. Verify the same package family and the old library and queue. In a separate clean profile, repeat with a fixture from the unpackaged `%APPDATA%\Muses` location and launch the candidate without `MUSES_DATA_HOME`. | Packaging and persistence |
| Historical Linux upgrade | In a disposable profile, seed a development database under `$HOME/.config/Muses` with no preferred XDG data database. Launch the installed candidate without `MUSES_DATA_HOME` and verify it uses the existing data. Repeat with a prior `.deb` when one is available. After removal, verify the XDG data and cache remain. | Packaging, persistence and Linux integration |
| Library and queue | Use distinct YouTube IDs, a favorite, an ordered playlist, history, a note and a queue with a current track and Up Next entries. Check values and order after upgrade and relaunch. | Persistence and playback |
| Media and host integration | Run [the prerequisite check](../INSTALL.md#verify-audio-prerequisites), then resolve and play public YouTube audio through yt-dlp and mpv, seek, test failure and retry, and open IFrame video separately. Check Windows SMTC and Credential Manager, plus Linux MPRIS, desktop credential storage, video and tray availability in a real desktop session. Use a test account for consent and credential checks. | Media and Linux integration |
| Visual and interaction comparison | Capture idle and playing states on both hosts and against the Polyhymnia reference at `7ce577588f3a8fdd4b8f31fc9c01b541291ffdd3`. Exercise navigation, queue, cover and vinyl Now Playing, keyboard focus, reduced motion and high contrast. | Shared UI and desktop hosts |

The Windows in-place upgrade keeps the existing MSIX family and AppData virtualization behavior. The package-private and unpackaged database checks cover distinct Windows storage cases. [Microsoft MSIX desktop behavior](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)
