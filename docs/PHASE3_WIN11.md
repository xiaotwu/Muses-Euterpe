# Euterpe Phase 3 — Windows 11 validation checklist

Phase 3 lands preference persistence, honest YouTube OAuth, SMTC/tray → `PlaybackService`, and OS credential storage.
Validate the following on a real **Windows 11** machine before shipping.

Re-verified on this Win11 x64 host (2026-09-09): .NET 10.0.401, WebView2 **152.0.4191.66**, vendored `resources\mpv.exe` + `resources\yt-dlp.exe` (PATH has yt-dlp; mpv PATH absent), Windows SDK / MakeAppx **absent**.

## Preferences

- [x] Change volume, theme, language, ResumeAfterVideo, sidebar collapsed, audio quality, ReplayGain, tray, close-to-tray, Web Home flags in Settings. — Pass (prefs wired to SQLite; `SettingsPolicyTests` + Settings bindings). Quality change re-resolves the current track via `ProcessStreamEngine.SetStreamQuality`.
- [ ] Quit and relaunch: values restore from SQLite prefs table. — Manual follow-up (needs GUI relaunch). Store path `%APPDATA%\Muses\muses-youtube-native.sqlite`.
- [ ] Language switch updates chrome strings via `L10n.Tr`. — Manual follow-up (Settings labels localized this pass; full chrome refresh needs GUI).

## YouTube account

- [x] Without `MUSES_GOOGLE_OAUTH_CLIENT_ID` (and without untracked config): Sign-In shows **Error** with honest L10n copy — never a fake "Muses User" / `UC_default`. — Pass (automated: `SettingsPolicyTests.YouTubeAccount_missing_client_id_sets_Error_not_fake_user`). GUI click still a manual follow-up.
- [ ] With a desktop OAuth client id configured: auth-code loopback → token exchange → channel userinfo; profile appears in Settings and Home guest banner hides. — Manual follow-up. Do not invent a production client id.
- [x] Tokens live in **Windows Credential Manager** (`Muses/YouTubeOAuth/...`), not plaintext JSON under `%AppData%\Muses`. — Pass (code path: `PlatformCredentialStore` / Windows CredMan).
- [x] Sign Out clears Credential Manager entries. — Pass (automated locker delete; GUI Sign Out still a manual follow-up).
- [x] Playback starts and continues while signed out / signing in (account never blocks the engine). — Pass (composition isolates account from engine; live yt-dlp→mpv probe succeeded signed-out).

## SMTC

- [ ] While playing, Windows media flyout / hardware keys show title, artist, artwork (when URL fetchable), play state, and timeline. — Manual audio/GUI follow-up (`WindowsSmtcBackend` hooks ButtonPressed + MediaPlayer SMTC + artwork best-effort). App process is running (`Muses`) on this host.
- [x] Play / Pause / Next / Previous from SMTC call the same `PlaybackService` as the in-app capsule (not the engine directly). — Pass (events → `PlaybackService`; `TryHookButtons` is Expression-based WinRT hook, not empty).

## Tray

- [ ] With `PrefKey.FfTray` on (default): notification-area icon appears with Now Playing tooltip. — Manual follow-up.
- [ ] Menu: Play/Pause, Next, Prev, Show, Exit — all route through `PlaybackService` / app lifecycle. — Manual follow-up (code routes via `WindowsTrayController` → `PlaybackService`).
- [ ] Close-to-tray (pref) hides the main window instead of exiting; Exit from tray shuts down. — Manual follow-up (`MainWindow.Closing` honors `CloseToTray` + `TrayEnabled`).
- [x] Turning tray off in Settings removes the icon. — Pass (`TrayEnabled` → `SetEnabled(false)` → `TrayIconHost.DisposeIcon`).

## Mini Player / Desktop Lyrics / hotkeys

- [x] Mini Player and Desktop Lyrics stay disabled until their feature flags are enabled (defaults off). — Pass (checkboxes bound to `PrefKey.FfMiniPlayer` / `FfDesktopLyrics`; Open buttons remain gated).
- [x] In-window transport chords (`Ctrl+P` / `Ctrl+Left` / `Ctrl+Right`) always work when MainWindow is focused. — Pass (ungated in `MainWindow.OnKeyDown`; `FfGlobalHotkeys` does not gate them).
- [x] Optional system-wide hotkeys (`PrefKey.FfGlobalHotkeys`, default off) left unwired — documented in Settings; not required for Phase 3. — Pass (checkbox + honest copy; no global hook). Stage 3 if a robust global hook is added later.

## Phase 2 carry-over (WebView2)

- [x] YouTube video overlay uses WebView2 Evergreen only inside the overlay host. — Pass (`WindowsWebView2YouTubeIFrameClient.TryCreate` + `WebView2NativeHost` in `EmbedHost`; runtime present). TearDown pauses iframe JS then navigates blank; host reuse after close resets `_destroying`.
- [x] Overlay open still Pause + `SuspendNative`; close tears down iframe + `ResumeNative` (+ optional Play per ResumeAfterVideo). — Pass (`YouTubeVideoOverlayPolicyTests` + TearDown navigates blank).

## Privacy

- [x] No telemetry endpoints contacted during sign-in or playback. — Pass (no telemetry code paths).
- [x] No OAuth tokens written into `muses-youtube-native.sqlite` or other app-data JSON. — Pass (CredMan-backed store).

## Automated checks (this machine)

- `dotnet test`: **171 passed**
- `dotnet build Muses.slnx`: green (MSB3277 WebView2 WPF WindowsBase conflict warning remains; not treated as error)
- WebView2 Evergreen: **152.0.4191.66**
- mpv: `resources\mpv.exe` (copied to app output); live googlevideo URL stayed alive in mpv 4s
- yt-dlp: `resources\yt-dlp.exe` + PATH WinGet copy
- MakeAppx: **absent** (see Phase 4)
