# Euterpe Phase 3 — Windows 11 validation checklist

Phase 3 lands preference persistence, honest YouTube OAuth, SMTC/tray → `PlaybackService`, and OS credential storage.
This Mac checkout builds and tests with **macOS no-ops** for SMTC WinRT. Validate the following on a real **Windows 11** machine before shipping.

## Preferences

- [ ] Change volume, theme, language, ResumeAfterVideo, sidebar collapsed, audio quality, ReplayGain, tray, close-to-tray, Web Home flags in Settings.
- [ ] Quit and relaunch: values restore from SQLite prefs table.
- [ ] Language switch updates chrome strings via `L10n.Tr`.

## YouTube account

- [ ] Without `MUSES_GOOGLE_OAUTH_CLIENT_ID` (and without untracked config): Sign-In shows **Error** with honest L10n copy — never a fake "Muses User" / `UC_default`.
- [ ] With a desktop OAuth client id configured: auth-code loopback → token exchange → channel userinfo; profile appears in Settings and Home guest banner hides.
- [ ] Tokens live in **Windows Credential Manager** (`Muses/YouTubeOAuth/...`), not plaintext JSON under `%AppData%\Muses`.
- [ ] Sign Out clears Credential Manager entries.
- [ ] Playback starts and continues while signed out / signing in (account never blocks the engine).

## SMTC

- [ ] While playing, Windows media flyout / hardware keys show title, artist, artwork (when URL fetchable), play state, and timeline.
- [ ] Play / Pause / Next / Previous from SMTC call the same `PlaybackService` as the in-app capsule (not the engine directly).

## Tray

- [ ] With `PrefKey.FfTray` on (default): notification-area icon appears with Now Playing tooltip.
- [ ] Menu: Play/Pause, Next, Prev, Show, Exit — all route through `PlaybackService` / app lifecycle.
- [ ] Close-to-tray (pref) hides the main window instead of exiting; Exit from tray shuts down.
- [ ] Turning tray off in Settings removes the icon.

## Mini Player / Desktop Lyrics / hotkeys

- [ ] Mini Player and Desktop Lyrics stay disabled until their feature flags are enabled (defaults off).
- [ ] Global transport hotkeys (`Ctrl+P` / Left / Right) stay off unless `FfGlobalHotkeys` is enabled.

## Phase 2 carry-over (WebView2)

- [ ] YouTube video overlay uses WebView2 Evergreen only inside the overlay host.
- [ ] Overlay open still Pause + `SuspendNative`; close tears down iframe + `ResumeNative` (+ optional Play per ResumeAfterVideo).

## Privacy

- [ ] No telemetry endpoints contacted during sign-in or playback.
- [ ] No OAuth tokens written into `muses-youtube-native.sqlite` or other app-data JSON.
