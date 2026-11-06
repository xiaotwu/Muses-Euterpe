# Muses desktop port

Euterpe uses Avalonia 12 on Windows. Polyhymnia at `7ce577588f3a8fdd4b8f31fc9c01b541291ffdd3` is the initial visual and interaction reference. Euterpe uses native Windows decorations and desktop services. Euterpe's repository history and existing SQLite user data provide the upgrade baseline.

## Delivery stages

| Stage | Work | Acceptance |
| --- | --- | --- |
| 1. App shell | Upgrade to Avalonia 12. Replace the previous Windows visual foundation with Muses typography, champagne-gold and graphite colors, navigation islands, and shared in-window control materials. Keep native window decorations. | Verify startup, title bar, keyboard focus, compact sizing, and navigation on Windows. |
| 2. Listening experience | Align the floating player, queue, cover and vinyl Now Playing, lyrics, motion, and collection navigation with the current Mac source. Use render-thread animation where useful and keep translucent effects bounded to their controls. | Compare the same states on Windows and Mac. Exercise idle, playback, pause, seek, track change, queue reorder and restoration, reduced motion, and high contrast. |
| 3. Library and playback | Reuse and validate the existing C# domain and SQLite upgrade path. Align Home, main-pane Search, collection views, Settings, and podcast behavior. Verify yt-dlp resolution, request context, mpv playback, failure and retry behavior. | Existing data fixtures retain identity, ordering, favorites, history, notes, and queue state. Verify real public media with mpv on Windows. |
| 4. Host integration | Keep Windows SMTC and Credential Manager. Validate the WebView2 IFrame host, tray availability, and window restoration. | Exercise controls from Windows, verify correct capability reporting, and check credential isolation. |
| 5. Release candidates | Validate Windows 11 x64. Verify the MSIX, dependency provenance, and Windows update asset selection. | Install, run, upgrade, and uninstall on a clean system. Record screenshots and interaction results before claiming visual or feature parity. |

Run the build and test suite throughout. Use isolated runtime data for smoke tests. Mac library transfer and account-personalized Home require separate acceptance because they have their own data and consent boundaries.

## Integrated development candidate

The Windows application uses Avalonia 12.1.5 and native window decorations. Navigation islands, Search, Settings, the dock, the queue, and Now Playing use the G1 theme. The cover transition uses the arranged source and destination positions. Vinyl motion follows playback state. Materials use in-window fills, highlights, and an artwork backdrop, with solid fills for high contrast or reduced transparency.

- Windows 11 x64 exercised dark and light appearances, Search, navigation, Settings, the queue, and cover and vinyl views in the desktop app. An isolated relaunch restored the selected track paused at its saved offset. The mpv integration resolved and played public YouTube audio, recovered from a fixture HTTP 403, sought, restored from SQLite, and advanced at end of file.
- The test suite covers SQLite migration, track identity, queue occurrences, playback recovery, and Windows release asset selection. The [release verification checklist](release-verification.md) records the MSIX package procedures.

Visual acceptance still requires a side-by-side desktop run against the pinned macOS app, including live refraction, motion timing, and keyboard interaction.

## Library and listening additions

- Home offers local Muses recommendations and an opt-in YouTube Music account source. The isolated helper checks the active YouTube channel before returning normalized recommendations. A Windows desktop fixture verified matching and mismatched channels and the switch back to local recommendations. Live account consent and browser login remain separate acceptance checks.
- Settings imports tracks and playlists from a standalone Polyhymnia SQLite backup. A Windows desktop test selected a synthetic backup through the native file picker and imported one track and two entries into one playlist. Additional fixtures cover repeat import, duplicate entries, and rollback. Account credentials, podcast follows, and episode progress remain local to the original installation. [Backup and import steps](../INSTALL.md#import-a-mac-library)
- Podcasts supports catalog search, followed shows, episode pagination, completion and resume. The Windows desktop test played a public episode and restored it paused at 0:31 after restart, with the followed show and a completed episode still present.
- The queue drawer supports directional keys, Home, End, and Return. A headless Avalonia run verified that a reordered occurrence retains focus by its ID and activates the intended track.

## Desktop comparison

Use an interactive macOS 26 session. In a disposable Polyhymnia checkout at the pinned revision, run its existing `./Scripts/build_and_run.sh --isolated` entry point. Launch through the script so the isolation environment is applied. Use a test account or a clean desktop profile for acceptance, and test with public tracks and a synthetic queue. [Polyhymnia source at the reference revision](https://github.com/xiaotwu/Muses-Polyhymnia/tree/7ce577588f3a8fdd4b8f31fc9c01b541291ffdd3)

Start with the same window content size, scale, appearance, track, and queue order on each host. Record the OS, app revision, size and scaling with each capture. Repeat with a compact window and with the desktop's reduced-motion setting. Keep screenshots cropped to the app window and capture short clips for the transitions.

| Surface | Capture and interaction |
| --- | --- |
| Navigation | Capture Home, Search and a populated collection in light and dark appearance. Traverse islands by keyboard and check focus visibility, scrolling, and selection at the compact width. |
| Floating player | Capture idle, paused and playing states. Seek, change tracks and open the volume control. Compare the button hit areas, positions and backdrop over artwork. |
| Queue | Use manual and collection occurrences of the same track. Open the drawer, use Up/Down and Home/End, activate with Return, reorder a manual occurrence, and dismiss with Escape. Check which occurrence plays and where focus returns. |
| Now Playing | Record entry and exit in cover and vinyl modes. Toggle lyrics, pause and resume, change tracks, and repeat under reduced motion. Compare artwork alignment, dock visibility, focus, and vinyl motion. |

The reference code sets the drawer transition to 250 ms and the Now Playing artwork morph to 320 ms, disables those animations under Reduce Motion, and rotates active vinyl once per 16 seconds. Record measured behavior separately from these source values. Report differences in window decorations separately from app content.
