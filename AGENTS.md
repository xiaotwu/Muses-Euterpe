# Muses Euterpe — Project Guidance

Muses Euterpe is the Windows 11 port of [Muses](https://github.com/xiaotwu/Muses). Product law is the same: a native desktop **YouTube-native music application**. Not a local-file player, not Electron, not a WebView sound source.

Display name is **Muses**. The repository and solution are named Euterpe.

## Source of truth

1. Explicit current user decisions and this file.
2. Current C# source.
3. Tests under `tests/Muses.Tests`.
4. The macOS Muses source at a sibling `../Muses` checkout, when behavior is unspecified here.

## Architecture

- `src/Muses.Core` — tokens, domain, policies, `PlaybackService` facade, L10n. No UI.
- `src/Muses.App` — Avalonia 11 + FluentAvalonia chrome and ViewModels.
- `src/Muses.Infrastructure` — yt-dlp, mpv `ProcessStreamEngine`, discovery, lyrics, history, advanced services, update checker (GitHub releases API only).
- `src/Muses.Persistence` — SQLite store (`SqliteStore.DefaultPath`).
- `src/Muses.Platform.Windows` — SMTC + tray bridges (no-op / limited on non-Windows). Credentials live in Infrastructure via `ICredentialStore`.
- `src/Muses.WebHome` + `src/Muses.WebHome.Helper` — isolated Web Home helper. Cookie jars are ephemeral, permission-restricted, and deleted on helper exit. No scraping in the Avalonia process.

`PlaybackService` is the only UI-facing playback facade. Views never own a second engine or store. EQ band changes reach the engine through `PlaybackService.SetEq` → `IPlayerEngine.SetEq` (mpv `af` equalizer in production; recorded by `FakePlayerEngine` in tests).

## Visual contract

Keep Muses chrome: accent `#FA586A`, page `#1F1F1F`, 244/88 sidebar, 668×56 floating capsule, MonteCarlo wordmark, YouTube red mark, no glass on browsing cards.

Adapt windowing: native Win11 caption on the right, Mica on the window, Acrylic on chrome. Do not draw fake macOS traffic lights.

Play/pause in the main capsule is a white circle, not pink.

## Privacy

Local-first. Tokens in the platform credential store. Web Home cookies live only in a permission-restricted helper jar deleted on exit. No telemetry. Update checks only hit the GitHub releases API.

## Verification

`dotnet test` and `dotnet build`. Do not claim done without running them. Visual work is judged in the running app against Muses screenshots.

Identifiers and comments are English. User-visible strings go through `L10n.Tr`.

## Packaging

- `scripts/package-msix.ps1` prepares / packs MSIX on **Windows** with the Windows SDK.
- macOS and Linux checkouts cannot produce a MSIX artifact — document that honestly in INSTALL.
- Microsoft Store and WinGet are **not published**.
