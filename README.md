<p align="center">
  <img src="resources/images/icon.png" width="96" alt="Muses icon" />
</p>

<h1 align="center">Muses</h1>

<p align="center">
  A native Windows 11 music application — the Euterpe port of <a href="https://github.com/xiaotwu/Muses">Muses</a> for macOS.
</p>

<p align="center">
  <a href="#features">Features</a> •
  <a href="INSTALL.md">Installation</a> •
  <a href="PRIVACY.md">Privacy</a> •
  <a href="#architecture">Architecture</a> •
  <a href="#development">Development</a> •
  <a href="#license">License</a>
</p>

---

## Overview

**Muses** is a native desktop music application tailored for YouTube listening. It is neither an Electron wrapper, a WebView sound source, nor a local-file-only player. Built with **C# / .NET 10 + Avalonia 11 + FluentAvalonia**, it brings the Muses visual language to Windows 11 with native system integration where implemented.

## Features

### Discovery & library
- Home & Discover feeds (yt-dlp discovery + local library).
- Albums, Artists, Songs, and playlists.
- Auxiliary search window and YouTube URL / playlist import (`Ctrl+V` / drop).

### Playback
- Floating glass capsule transport (668×56) with white circular play/pause.
- Now Playing (cover / vinyl), LRC lyrics drawer, queue drawer with persistence.
- YouTube video overlay (WebView2 on Windows) — iframe is **not** the sound source; native audio suspends while the overlay owns video.
- Listening history & heatmap.
- Real playback engine: **yt-dlp → mpv IPC** (`ProcessStreamEngine`). Pause/seek/volume do not kill the process.

### Windows 11 integration (Windows hosts)
- Mica / Acrylic chrome adaptation.
- SMTC (lock screen / volume flyout) routed through `PlaybackService`.
- System tray + optional Mini Player / Desktop Lyrics (feature-flagged; defaults noted in prefs).

### Advanced
- **32-band EQ** — band changes call `IPlayerEngine.SetEq`; production applies mpv `af` equalizer filters. If the engine cannot EQ, the UI disables with honest copy.
- **Audio Nerd** inspector — shows known track/stream fields; **does not invent** output device names (Unknown until real device enumeration exists).
- Focus Mode, Notes & Bookmarks, Music Inbox.
- Automation rules engine (playback triggers + time-band / headphone conditions when a real context is supplied — no fabricated device names). **No visual automation editor / rule builder UI is shipped** — rules are engine-side only.
- **Web Home helper** (opt-in, default off) — isolated `MusesWebHomeHelper` process; ephemeral cookie jar deleted on exit; no scraping in the Avalonia process.

### Not claimed / not published
- Microsoft Store and WinGet packages are **not published**.
- Visual parity with macOS Muses is judged in the running app, not from docs alone.

---

## Visual design contract

| Token | Value | Purpose |
| --- | --- | --- |
| **Accent** | `#FA586A` | Primary brand accent |
| **Page Background** | `#1F1F1F` | Main content backdrop |
| **Surface Background** | `#26262B` | Elevated panels / capsule |
| **Sidebar Width** | `244px` / `88px` | Expanded / collapsed |
| **Capsule Dock** | `668×56px` | Floating transport |
| **Play/Pause** | `#FFFFFF` circle | Dock play control |
| **Typography** | Inter + MonteCarlo | UI + wordmark |

---

## Architecture

```
src/
├── Muses.Core/              # Domain, PlaybackService facade, L10n
├── Muses.Persistence/       # SQLite (SqliteStore.DefaultPath)
├── Muses.Infrastructure/    # yt-dlp, mpv engine, services, WebHome client
├── Muses.Platform.Windows/  # SMTC, tray
├── Muses.WebHome/           # Cookie jar + probe command (helper-side)
├── Muses.WebHome.Helper/    # MusesWebHomeHelper executable
└── Muses.App/               # Avalonia UI
tests/
└── Muses.Tests/
```

---

## Development

```bash
dotnet test
dotnet build
dotnet run --project src/Muses.App
```

CI: `.github/workflows/ci.yml` runs `dotnet test` + `dotnet build` on `ubuntu-latest` and `windows-latest`.

MSIX: see [INSTALL.md](INSTALL.md) — **Windows only**; macOS cannot produce MSIX. Source build remains the supported path. A sideload `.msix` is produced only when `MakeAppx.exe` (Windows SDK) is on the packing machine.

Vendored player binaries (`resources/mpv.exe`, `resources/yt-dlp.exe`) are gitignored. Place them under `resources/` for `dotnet run` without a global PATH.

---

## Privacy

Zero telemetry. Local SQLite + platform credential locker. Web Home cookies only in the helper jar. Details: [PRIVACY.md](PRIVACY.md).

---

## License

MIT License. © 2026 xiaotwu. Third-party tools (`Avalonia`, `FluentAvalonia`, `yt-dlp`, `mpv`) remain under their respective licenses.
