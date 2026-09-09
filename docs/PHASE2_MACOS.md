# Euterpe Phase 2 — macOS-dev notes

This checkout runs on macOS for development. Production target remains Windows 11.

## WebView / YouTube embed

- The on-demand YouTube video overlay is the **only** place an Avalonia WebView / WebView2 may appear.
- No compatible Avalonia WebView2 package is referenced for `net10.0` + Avalonia 11.3 yet (`WebView.Avalonia` 11.0.0.1 does not restore on this TFM; `Avalonia.Controls.WebView` targets Avalonia 12).
- On this Mac build, `YouTubeEmbedHostFactory` returns a **degraded** `IYouTubeIFrameClient` (`IsAvailable = false`). The overlay shows an honest disabled embed surface plus secondary **Open in YouTube**. It does **not** silently `Process.Start` as the only Windows path.
- Overlay open/close policy still Pause + `SuspendNative` / teardown + `ResumeNative` + optional `Play()` via `PrefKey.ResumeAfterVideo` (default true), testable with `FakeYouTubeIFrameClient` (no live WebView in CI).

## Audio / mpv

- Phase 1 `ProcessStreamEngine` uses mpv JSON IPC. This Mac may not have `mpv` on PATH; UI can still build/test with `FakePlayerEngine`. Install mpv for live native playback smoke tests.

## Preferences

- Phase 3: SQLite `preferences` table via `SqlitePreferences`. Session `MemoryPreferences` remains for unit tests.

## Win11 follow-ups

- Wire WebView2 (or Avalonia WebView) **only** inside `YouTubeVideoOverlay` via `WindowsWebView2YouTubeIFrameClient.TryCreate`.
- Prefs persistence: landed in Phase 3 (`SqlitePreferences`).
- Confirm WebView2 Evergreen runtime on target machines.
