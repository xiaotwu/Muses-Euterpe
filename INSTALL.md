# Building Muses

Euterpe targets Windows 11 x64.

## From source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Put mpv and yt-dlp on PATH for playback, and follow the [yt-dlp EJS guide](https://github.com/yt-dlp/yt-dlp/wiki/EJS) for the supported JavaScript runtime.

```powershell
git clone https://github.com/xiaotwu/Muses-Euterpe.git
cd Muses-Euterpe
dotnet build Muses.slnx
dotnet test Muses.slnx
dotnet run --project src/Muses.App
```

A development checkout can also use `resources/mpv.exe` and `resources/yt-dlp.exe`.

For source runs, `MUSES_YTDLP_PATH` and `MUSES_DENO_PATH` can point to absolute executable paths outside the desktop session's PATH.

## Data and cache

**Settings → General → App Data Folder** shows and opens the active data directory. The default is `%APPDATA%\Muses`. It contains the SQLite library and a `cache` subdirectory.

For isolated development runs, set `MUSES_DATA_HOME` and, if needed, `MUSES_CACHE_HOME` to absolute paths dedicated to that run. An explicit data root uses session-only account credentials.

## Import a Mac library

Quit Muses on the Mac and use SQLite's backup command to create a standalone copy of its library. The backup includes changes recorded in the database journal.

```sh
backup_dir=$(mktemp -d "${TMPDIR:-/tmp}/muses-library.XXXXXX")
sqlite3 -readonly "$HOME/.muses/data/muses-youtube-native.sqlite" \
  ".backup \"$backup_dir/muses-library.sqlite\""
```

Transfer `muses-library.sqlite` to the Windows machine. In Muses, open **Settings**, select **Import Mac Library**, and choose the backup. The import merges YouTube tracks and playlists into the existing library. Keep the backup until you verify the imported library.

## Packages

Install `Muses-<version>-win-x64.msix` on Windows 11 x64. The installed app appears as **Muses** in Start.

The MSIX includes the self-contained .NET app, a separate Web Home helper, mpv, yt-dlp with its EJS scripts, and Deno. Muses uses these installed tools automatically. The build pins upstream media assets by SHA-256 and includes notices and provenance in `MediaTools`. For a stable release, use the release tag's numeric version without the leading `v` so the running app and the asset agree.

### Windows 11 x64

Build on Windows with PowerShell 7, Python 3, the .NET 10 SDK and the x64 Windows SDK packaging tools. Pass `-PythonExecutable` if Python is outside PATH. You can supply the `bin/<sdk-version>/x64` directory from `Microsoft.Windows.SDK.BuildTools` with `-SdkToolsDir`. MakeAppx builds and validates the package. SignTool signs it when you provide a code-signing certificate. Use the existing package identity `xiaotwu.Muses` and publisher `CN=xiaotwu` for in-place upgrades. [Windows SDK downloads](https://developer.microsoft.com/windows/downloads/windows-sdk/) · [Windows SDK BuildTools on NuGet](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/)

```powershell
$packageDir = Join-Path ([IO.Path]::GetTempPath()) ('muses-packages-' + [guid]::NewGuid())
pwsh -NoProfile -File scripts/package-msix.ps1 -Version 0.1.1 -OutputDir $packageDir
```

The default build produces an unsigned package for structure checks. To produce an installable test candidate, rerun with `-CertificateThumbprint <thumbprint>` for a matching certificate with a private key in `Cert:\CurrentUser\My`. For self-signed testing, export the public `.cer` and import it into `Cert:\LocalMachine\TrustedPeople` from an elevated PowerShell session on the test machine. A trusted signing chain is required for install. [Microsoft MSIX signing guide](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide)

For production, supply the exact signing certificate subject as `-Publisher`, an HTTPS RFC 3161 timestamp URL, and the expected SHA-256 fingerprint of the trusted issuing root. The packaging script verifies the timestamped signature, publisher, and chain before copying the package to the output folder.

```powershell
pwsh -NoProfile -File scripts/package-msix.ps1 -Version 0.1.3 -OutputDir $packageDir `
  -ProductionSigning -Publisher $publisher -TimestampUrl $timestampUrl `
  -TrustedRootSha256 $rootFingerprint -CertificateThumbprint $leafThumbprint
```

For a SignTool signing provider such as Azure Artifact Signing, supply `-SigningDlib` and `-SigningMetadata` in place of `-CertificateThumbprint`. Use a verified public-trust signing profile and keep provider credentials in its authentication environment. Choose the production publisher before distributing the first stable package. A different publisher creates a different MSIX package family. Upgrade acceptance uses two signed versions with the same production identity. [Artifact Signing integrations](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-signing-integrations)

From Windows PowerShell on the test machine, run the following with the signed package. Use the same command with a higher version signed for the same publisher to upgrade. `Remove-AppxPackage` removes the package for the current user. Windows may remove package-private data on uninstall.

```powershell
Add-AppxPackage -Path (Join-Path $packageDir 'Muses-0.1.1-win-x64.msix')
# Launch Muses from Start.
Get-AppxPackage -Name xiaotwu.Muses | Select-Object PackageFamilyName, Version, Architecture
Get-AppxPackage -Name xiaotwu.Muses | Remove-AppxPackage
```

In-app video uses Microsoft's WebView2 Evergreen Runtime. [WebView2 download](https://developer.microsoft.com/microsoft-edge/webview2/)

### Verify audio prerequisites

From a checkout, run `py -3 scripts/check-media-tools.py`. Pass `--media-dir <absolute-path>` to verify the tools from a package, or `--mpv <absolute-path>` to inspect an mpv executable outside PATH. The check starts mpv for its version and verifies local EJS and Deno without resolving a media URL. The package smoke tests can also exercise real playback and resume.

See [release candidate verification](docs/release-verification.md) for automated package checks and the clean desktop acceptance matrix.
