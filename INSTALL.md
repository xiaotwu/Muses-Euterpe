# Building Muses

Euterpe is in development for Windows and Linux. The initial target matrix is Windows 11 x64 and Ubuntu 24.04 LTS x64. The Linux desktop build uses Avalonia's default X11 backend and can run under XWayland.

## From source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). On Linux, use a graphical session with Avalonia's [desktop libraries](https://docs.avaloniaui.net/docs/platform-specific-guides/linux). Put mpv and yt-dlp on PATH for playback, and follow the [yt-dlp EJS guide](https://github.com/yt-dlp/yt-dlp/wiki/EJS) for the supported JavaScript runtime.

```sh
git clone https://github.com/xiaotwu/Muses-Euterpe.git
cd Muses-Euterpe
dotnet build Muses.slnx
dotnet test Muses.slnx
dotnet run --project src/Muses.App
```

On Windows, a development checkout can also use `resources/mpv.exe` and `resources/yt-dlp.exe`. In-app video uses WebView2 on Windows and WebKitGTK 4.1 on Linux. Linux system media controls use MPRIS over the desktop session D-Bus.

When yt-dlp and Deno are installed outside the desktop session's PATH, set `MUSES_YTDLP_PATH` and `MUSES_DENO_PATH` to their absolute executable paths before launching Muses. The optional Web Home helper finds yt-dlp on PATH.

For isolated development runs, set `MUSES_DATA_HOME` and `MUSES_CACHE_HOME` to absolute paths dedicated to that run. These override the app's database and cache roots. An explicit data root uses session-only account credentials. Windows retains the existing default SQLite path under `%APPDATA%\Muses`. Linux uses the XDG data and cache directories and recognizes the previous development database.

## Import a Mac library

Quit Muses on the Mac and use SQLite's backup command to create a standalone copy of its library. The backup includes changes recorded in the database journal.

```sh
backup_dir=$(mktemp -d "${TMPDIR:-/tmp}/muses-library.XXXXXX")
sqlite3 -readonly "$HOME/.muses/data/muses-youtube-native.sqlite" \
  ".backup \"$backup_dir/muses-library.sqlite\""
```

Transfer `muses-library.sqlite` to the Windows or Linux machine. In Muses, open **Settings**, select **Import Mac Library**, and choose the backup. The import merges YouTube tracks and playlists into the existing library. Keep the backup until you verify the imported library.

## Packages

Use the package that matches the operating system and CPU architecture.

| Target | Asset | Installed entry |
| --- | --- | --- |
| Windows 11 x64 | `Muses-<version>-win-x64.msix` | Muses in Start |
| Ubuntu 24.04 LTS x64 | `Muses-<version>-linux-x64.deb` (`amd64` in Debian metadata) | Muses in the application menu or `muses` |

Both packages include the self-contained .NET application and a separately published Web Home helper. The build restores the .NET runtime and managed dependencies from the configured NuGet sources. For a stable release, use the release tag's numeric version without the leading `v` so the running app and the asset agree.

### Windows 11 x64

Build on Windows with PowerShell 7, the .NET 10 SDK and the x64 Windows SDK packaging tools. You can also supply the `bin/<sdk-version>/x64` directory from `Microsoft.Windows.SDK.BuildTools` with `-SdkToolsDir`. MakeAppx builds and validates the package. SignTool signs it when you provide a code-signing certificate. Use the existing package identity `xiaotwu.Muses` and publisher `CN=xiaotwu` for in-place upgrades. [Windows SDK downloads](https://developer.microsoft.com/windows/downloads/windows-sdk/) · [Windows SDK BuildTools on NuGet](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/)

```powershell
$packageDir = Join-Path ([IO.Path]::GetTempPath()) ('muses-packages-' + [guid]::NewGuid())
pwsh -NoProfile -File scripts/package-msix.ps1 -Version 0.1.1 -OutputDir $packageDir
```

The default build produces an unsigned package for structure checks. To produce an installable test candidate, rerun with `-CertificateThumbprint <thumbprint>` for a matching certificate with a private key in `Cert:\CurrentUser\My`. For self-signed testing, export the public `.cer` and import it into `Cert:\LocalMachine\TrustedPeople` from an elevated PowerShell session on the test machine. A trusted signing chain is required for install. [Microsoft MSIX signing guide](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide)

From Windows PowerShell on the test machine, run the following with the signed package. Use the same command with a higher version signed for the same publisher to upgrade. `Remove-AppxPackage` removes the package for the current user. Windows may remove package-private data on uninstall.

```powershell
Add-AppxPackage -Path (Join-Path $packageDir 'Muses-0.1.1-win-x64.msix')
# Launch Muses from Start.
Get-AppxPackage -Name xiaotwu.Muses | Select-Object PackageFamilyName, Version, Architecture
Get-AppxPackage -Name xiaotwu.Muses | Remove-AppxPackage
```

Install an x64 mpv build, upstream `yt-dlp.exe`, and an EJS-compatible Deno runtime on the desktop user's PATH for audio. Use Microsoft's WebView2 Evergreen Runtime for in-app video. [mpv Windows builds](https://mpv.io/installation/) · [yt-dlp releases](https://github.com/yt-dlp/yt-dlp/releases) · [yt-dlp EJS setup](https://github.com/yt-dlp/yt-dlp/wiki/EJS) · [WebView2 download](https://developer.microsoft.com/microsoft-edge/webview2/)

### Ubuntu 24.04 LTS x64

Build on Ubuntu 24.04 x64 with the .NET 10 SDK and `dpkg-deb`. APT resolves the native dependencies, including mpv from Ubuntu's Universe archive. The desktop session needs X11 or XWayland. Linux system media controls use the session D-Bus, and account storage requires a desktop Secret Service provider such as GNOME Keyring. The package recommends Ubuntu's `libwebkit2gtk-4.1-0` for the IFrame video host. [Avalonia Linux desktop guide](https://docs.avaloniaui.net/docs/platform-specific-guides/linux)

```sh
package_dir=$(mktemp -d)
bash scripts/package-deb.sh 0.1.1 "$package_dir"
sudo apt install "$package_dir/Muses-0.1.1-linux-x64.deb"
muses
dpkg-query -W muses
sudo apt remove muses
```

Use `sudo apt install /absolute/path/to/Muses-<higher-version>-linux-x64.deb` for an in-place upgrade. Package operations install files under `/usr` and leave the user's XDG directories intact. The default database lives under `${XDG_DATA_HOME:-$HOME/.local/share}/Muses`. When there is no database at the preferred location, Muses can open the legacy database in `${XDG_CONFIG_HOME:-$HOME/.config}/Muses` or `$HOME/.config/Muses` in place.

For audio, install the x64 `yt-dlp_linux` release as `yt-dlp` and install Deno on the graphical session's PATH, for example in `/usr/local/bin`. Ubuntu's mpv package can pull in the archive copy of yt-dlp. Run `command -v yt-dlp` from the desktop session to check that PATH resolves the upstream standalone binary first. Verify standalone binaries against upstream release checksums. The standalone yt-dlp release includes the EJS scripts used for YouTube challenges. [yt-dlp releases and checksums](https://github.com/yt-dlp/yt-dlp/releases) · [yt-dlp EJS setup](https://github.com/yt-dlp/yt-dlp/wiki/EJS) · [Deno installation](https://docs.deno.com/runtime/getting_started/installation/)

### Verify audio prerequisites

From a checkout, run `python3 scripts/check-media-tools.py` on Ubuntu or `py -3 scripts/check-media-tools.py` on Windows in the desktop user's environment. The optional check starts mpv for its version and verifies that yt-dlp recognizes the installed Deno and local EJS. It uses no media URL, cookies, user yt-dlp configuration or remote challenge components. Pass `--mpv <absolute-path>` to inspect an mpv executable outside PATH. Real media resolution and playback are separate checks.

See [release candidate verification](docs/release-verification.md) for automated package checks and the clean desktop acceptance matrix.
