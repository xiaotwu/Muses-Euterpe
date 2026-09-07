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

**Muses** is a native desktop music application tailored for YouTube listening. It is neither an Electron wrapper, a WebView sound source, nor a local-file-only player. Built with **C# / .NET 10 + Avalonia 11 + FluentAvalonia**, it brings the refined visual language of Apple Music Web and macOS Muses directly to Windows 11 with native system integration.

## Key Features

### 🎵 Discovery & Library Surfaces
- **Home & Discover**: Curated music discovery feeds, featured songs, and trending albums.
- **Full Catalogs**: Comprehensive views for Albums, Artists, Songs, and Custom Playlists.
- **Auxiliary Search Window**: Multi-provider search with instant suggestion queries and YouTube results.
- **Drag-and-Drop & URL Import**: Drop any YouTube link or playlist directly onto the window or press `Ctrl+V` to play instantly.

### 🎛️ Playback & Immersive Audio
- **Floating Glass Capsule**: Iconic 668×56 transport dock floating above browsing content with smooth hover states and a white circular play/pause button.
- **Full-Screen Now Playing**: Immersive full-screen view featuring high-res album artwork, rotating vinyl record animation, and synchronized lyrics.
- **Synchronized LRC Lyrics**: Dynamic line-by-line synced lyrics drawer with auto-scroll and jump-to-timestamp.
- **Playback Queue**: Intuitive queue drawer supporting reordering, history tracking, and queue persistence across app restarts.
- **YouTube Video Overlay**: Picture-in-picture floating video overlay synchronized with current audio stream.
- **Listening History & Heatmap**: Interactive GitHub-style listening activity heatmap and chronological listening history log.

### 🪟 Windows 11 Desktop Integration
- **Mica & Acrylic**: Win11 native Mica backdrop and Acrylic chrome surfaces with native caption buttons positioned on the right.
- **System Media Transport Controls (SMTC)**: Native Windows 11 lock screen and volume flyout playback integration with live track metadata and album art.
- **System Tray & Mini Player**: Background minimization to system tray with quick transport menu, plus a compact floating always-on-top Mini Player.
- **Desktop Lyrics**: Transparent, click-through, draggable desktop lyrics banner hovering over your workflow.
- **Global Shortcuts**: System-wide media key interception and customizable hotkey triggers.

### 🔬 Advanced Audiophile & Productivity Suites
- **32-Band Equalizer**: 32-band ISO frequency sliders (16 Hz – 20 kHz) with real-time spline frequency curve rendering and built-in/custom presets.
- **Audio Nerd Inspector**: Live audio stream statistics, codec details, sample rate, bit depth, container format, and buffer latency monitor.
- **Focus Mode**: Customizable Pomodoro timer (25/45/60/90m) with auto-pause on completion, break phases, and playback queue locking.
- **Notes & Bookmarks**: Song-specific timestamped bookmarks and rich notes for study, critique, or track dissection.
- **Music Inbox**: Zero-sidebar triage workflow (`Ctrl+I`) to evaluate music recommendations (Unheard → Listening → Accept/Reject/Snooze).
- **Automation Rules Engine**: Event-driven rules reacting to playback start/finish, time bands, and track genres.
- **Isolated Web Home Helper**: Separate process protocol maintaining authenticated personalized feeds in an ephemeral, isolated cookie jar.

---

## Visual Design Contract

Muses strictly follows these design tokens:

| Token | Value | Purpose |
| --- | --- | --- |
| **Accent** | `#FA586A` | Primary brand accent and interactive focus |
| **Page Background** | `#1F1F1F` | Main content backdrop |
| **Surface Background** | `#26262B` | Elevated panels, cards, and capsule chrome |
| **Sidebar Width** | `244px` / `88px` | Expanded vs. compact collapsed modes |
| **Capsule Dock** | `668×56px` | Floating player transport capsule |
| **Play/Pause Button** | `#FFFFFF` circle | Dock play button (white circle, dark icon) |
| **Typography** | Inter + MonteCarlo | UI body font and script wordmark |

---

## Architecture

The solution is divided into clean, decoupled layers:

```
src/
├── Muses.Core/           # Pure domain models, tokens, commands, interfaces, L10n. (No UI)
├── Muses.Persistence/    # SQLite store: schema migrations, library, queue, history, notes, rules
├── Muses.Infrastructure/ # yt-dlp resolver, lyrics engine, audio pipeline, update checker
└── Muses.App/            # Avalonia 11 + FluentAvalonia UI, ViewModels, Windows 11 chrome
tests/
└── Muses.Tests/          # Comprehensive unit and contract tests (129+ automated tests)
```

---

## Development & Testing

### Requirements
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10/11 (or macOS/Linux for cross-platform UI development)

### Build & Run
```bash
# Run test suite
dotnet test

# Run application
dotnet run --project src/Muses.App
```

### Packaging MSIX
To produce a Windows 11 MSIX package:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-msix.ps1 -Configuration Release
```

For more detailed setup options, see [INSTALL.md](INSTALL.md).

---

## Privacy & Security

Muses adheres to strict local-first principles:
- **Zero telemetry**, zero analytics, and zero advertising.
- All database state is stored locally in `%LOCALAPPDATA%\Muses\muses.db`.
- Tokens are held securely via platform credential lockers.
- Read our full [Privacy Policy](PRIVACY.md).

---

## License

MIT License. © 2026 xiaotwu. Third-party libraries and tools (`Avalonia`, `FluentAvalonia`, `yt-dlp`) remain under their respective licenses.
