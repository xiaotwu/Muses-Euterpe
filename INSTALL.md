# Installing Muses

Muses (Windows 11 port: Euterpe) is intended for **Windows 10/11**. The supported end-user path today is **build from source**. Prebuilt MSIX / WinGet packages are **not published** yet.

## System Requirements

- **Operating System**: Windows 11 (recommended for Mica and native caption integration) or Windows 10 (version 2004 / build 19041 or newer) for the full desktop experience.
- **Architecture**: 64-bit (`x64` or `arm64`).
- **Runtime / SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for source builds. Framework-dependent publishes need the .NET 10 Desktop Runtime.
- **Dev hosts**: macOS and Linux can `dotnet build` / `dotnet test` / run the Avalonia shell for UI work. SMTC, tray, and MSIX packaging are Windows-only.

---

## Supported path: build from source

1. **Clone**:
   ```bash
   git clone https://github.com/xiaotwu/Muses-Euterpe.git
   cd Muses-Euterpe
   ```

2. **Test**:
   ```bash
   dotnet test
   ```

3. **Run**:
   ```bash
   dotnet run --project src/Muses.App
   ```

4. **Optional helper** (Web Home isolation boundary):
   ```bash
   dotnet build src/Muses.WebHome.Helper
   ```
   The UI process launches `MusesWebHomeHelper` when Web Home is opted in; cookies never live in the Avalonia process.

---

## MSIX packaging (Windows only)

`scripts/package-msix.ps1` publishes the app, packs an `.msix` with `MakeAppx.exe` (Windows SDK **or** the `Microsoft.Windows.SDK.BuildTools` NuGet package), and **signs** it with a local `CN=xiaotwu` code-signing certificate (matches `Package.appxmanifest` Publisher). The private key stays in `Cert:\CurrentUser\My` and is never committed. The public cert is imported to `CurrentUser\TrustedPeople` so this account can sideload.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-msix.ps1 -Configuration Release
Add-AppxPackage -Path artifacts\msix\Muses-0.1.0.0-win-x64.msix
```

This is a **self-signed sideload** certificate, not a public CA / EV Authenticode cert. AppX install (`0x800B0109`) needs the cert in **LocalMachine\TrustedPeople**, which requires an elevated PowerShell **once**:

```powershell
Import-Certificate -FilePath artifacts\msix\Muses-sideload.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage -Path artifacts\msix\Muses-0.1.0.0-win-x64.msix
```

Microsoft SmartScreen may still warn once for an unknown publisher — *More info* → *Run anyway*. A silent SmartScreen reputation requires a purchased Authenticode certificate (Stage 3; not done here).

Source `dotnet run --project src/Muses.App` remains the supported development path. Store / WinGet are **not published**.

### This Windows 11 machine (2026-09-09)

Packed and signed: `artifacts/msix/Muses-0.1.0.0-win-x64.msix`. Full Windows SDK is still not installed; tools come from `Microsoft.Windows.SDK.BuildTools`.

### macOS / Linux limitation

**You cannot produce a MSIX artifact on macOS or Linux.** MSIX requires the Windows App SDK / `MakeAppx` toolchain. On a Mac checkout, use `dotnet build` / `dotnet test` only; run `package-msix.ps1` on a Windows machine or CI `windows-latest` runner.

### Store / WinGet status

- Microsoft Store: **not published**
- WinGet (`winget install xiaotwu.Muses`): **not published**
- GitHub Releases MSIX: publish when you intentionally cut a release — do not assume a package exists

---

## Media engine

Playback is **yt-dlp → mpv IPC** (`ProcessStreamEngine`). Official YouTube IFrame is not the sound source.

Vendored layout for `dotnet run` without a global PATH (binaries are gitignored — drop them in locally):

| File | Role |
| --- | --- |
| `resources/yt-dlp.exe` | Resolves YouTube stream URLs (copied to the app output directory when present) |
| `resources/mpv.exe` | Emits audio over named-pipe IPC (copied to output when present) |
| `resources/d3dcompiler_43.dll` | mpv companion DLL on Windows (copied next to `mpv.exe` when present) |

`MpvPlayerFactory` looks on `PATH`, then next to the app, then walks up to repo `resources/`. If mpv is missing, `LoadAsync` fails with `PlayerErrorKind.EngineStartFailed` (not a hung Buffering state). Settings → Audio Quality reports that honestly.

---

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `Space` | Play / Pause (when not focusing input) |
| `Ctrl + P` | Play / Pause |
| `Ctrl + Right` | Next Track |
| `Ctrl + Left` | Previous Track |
| `Ctrl + F` | Open Search Window |
| `Ctrl + V` | Open Paste URL Dialog |
| `Ctrl + L` | Toggle Lyrics Drawer |
| `Ctrl + U` | Toggle Queue Drawer |
| `Ctrl + E` | Toggle 32-band Equalizer |
| `Ctrl + N` | Open Track Notes & Bookmarks |
| `Ctrl + I` | Open Music Inbox |
| `Ctrl + ,` | Open Settings |
| `F11` | Toggle Fullscreen Now Playing |
| `Escape` | Dismiss modal dialogs / drawers |

---

## Troubleshooting

- **No audio**: confirm `mpv` is installed and the Windows output device works; check Muses volume.
- **Stream fails**: check network / `yt-dlp` availability.
- **SmartScreen** (if you sideload an unsigned MSIX built on Windows): *More info* → *Run anyway*, or trust the publisher certificate.
