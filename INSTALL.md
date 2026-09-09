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

`scripts/package-msix.ps1` publishes the app and, when the Windows SDK `MakeAppx.exe` is present, packs an `.msix`.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-msix.ps1 -Configuration Release
```

### macOS / Linux limitation

**You cannot produce a MSIX artifact on macOS or Linux.** MSIX requires the Windows App SDK / `MakeAppx` toolchain. On a Mac checkout, use `dotnet build` / `dotnet test` only; run `package-msix.ps1` on a Windows machine or CI `windows-latest` runner.

### Store / WinGet status

- Microsoft Store: **not published**
- WinGet (`winget install xiaotwu.Muses`): **not published**
- GitHub Releases MSIX: publish when you intentionally cut a release — do not assume a package exists

---

## Media engine

- `yt-dlp` resolves stream URLs; a binary may be copied from `resources/` at build time.
- Playback uses **mpv** over JSON IPC (`ProcessStreamEngine`). Install `mpv` on PATH or place it under `resources/` for audible playback and EQ filters.

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
