# Muses desktop privacy

Muses keeps your library, listening history, queue, bookmarks, notes, equalizer presets and preferences in a local SQLite database. The Windows data folder is `%APPDATA%\Muses`. Linux uses `$XDG_DATA_HOME/Muses` or `~/.local/share/Muses`. The data folder contains `muses-youtube-native.sqlite` and a `cache` subdirectory. On Linux, Muses copies an existing library from the previous XDG configuration directory when the new database is absent.

Optional Google sign-in uses Windows Credential Manager or the Linux desktop's Secret Service. Older local credential files can be read for one-way migration. A development run with an explicit data directory uses credentials only for the current session.

The optional YouTube Music Home requires consent to use the named browser's session. A separate helper checks that the browser and Muses are signed in to the same YouTube channel before fetching recommendations. The helper keeps session cookies in its isolated workspace. Muses keeps the displayed account recommendations in memory and clears them on sign-out or consent removal.

## Network requests

Muses has no analytics or telemetry service. Search, artwork, imports, and playback contact YouTube or its media servers. The locally installed yt-dlp resolves audio and the embedded YouTube player handles video. Lyrics lookup sends the track and artist names to LRCLIB. Checking for updates requests public Muses release metadata from GitHub.

## Local data removal

**Clear Cache** in Settings removes Muses-managed caches, including artwork and temporary media resolver data. **Reset Data** requests sign-out from the system credential store, clears the SQLite library and caches, then quits. Reset is irreversible.
