# Muses Euterpe — Project Guidance

Muses Euterpe is the Windows 11 port of [Muses](https://github.com/xiaotwu/Muses). Product law is the same: a native desktop **YouTube-native music application**. Not a local-file player, not Electron, not a WebView sound source.

Display name is **Muses**. The repository and solution are named Euterpe.

## Source of truth

1. Explicit current user decisions and this file.
2. Current C# source.
3. Tests under `tests/Muses.Tests`.
4. The macOS Muses source at a sibling `../Muses` checkout, when behavior is unspecified here.

## Architecture

- `src/Muses.Core` — tokens, domain, policies. No UI.
- `src/Muses.App` — Avalonia 11 + FluentAvalonia chrome.
- `src/Muses.Infrastructure` — yt-dlp, caches (later waves).
- `src/Muses.Persistence` — SQLite store (later waves).
- `src/Muses.Platform` / `Muses.Platform.Windows` — SMTC, tray, credentials (later waves).
- `src/Muses.WebHome.*` — isolated Web Home helper (later waves).

`PlaybackService` is the only UI-facing playback facade once Wave 1 lands. Views never own a second engine or store.

## Visual contract

Keep Muses chrome: accent `#FA586A`, page `#1F1F1F`, 244/88 sidebar, 668×56 floating capsule, MonteCarlo wordmark, YouTube red mark, no glass on browsing cards.

Adapt windowing: native Win11 caption on the right, Mica on the window, Acrylic on chrome. Do not draw fake macOS traffic lights.

Play/pause in the main capsule is a white circle, not pink.

## Privacy

Local-first. Tokens in the platform credential store. Web Home cookies live only in a permission-restricted helper jar deleted on exit. No telemetry.

## Verification

`dotnet test` and `dotnet build`. Do not claim done without running them. Visual work is judged in the running app against Muses screenshots.

Identifiers and comments are English. User-visible strings go through `L10n.Tr`.
