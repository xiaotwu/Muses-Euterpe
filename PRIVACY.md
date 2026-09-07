# Muses Privacy Policy & Security Principles

Muses is built from the ground up to respect your privacy and provide an uncompromised, local-first listening experience.

## Core Commitments

1. **Zero Telemetry & Zero Analytics**
   - Muses collects no analytics, telemetry, diagnostic logs, or usage metrics.
   - We do not operate remote telemetry servers, tracking pixels, or heartbeat pingers.
   - No crash reports or personal data are sent to any remote servers without explicit user initiation.

2. **Local-First Architecture**
   - Your listening history, playlists, queue, tracks, bookmarks, notes, equalizer presets, and automation rules reside strictly on your device in a local SQLite database (`muses.db`).
   - All cache data (e.g. lyrics, album art thumbnails, stream cache) is kept within your local app data directory and can be wiped from Settings at any time.

3. **Secure Credential Storage**
   - Account tokens (e.g., optional Google OAuth tokens) are stored in the platform's native secure storage (Windows Credential Locker / DPAPI encrypted storage).
   - Plaintext passwords or raw session tokens are never logged or stored in plain JSON/text files.

4. **Isolated Web Home Helper**
   - Web Home helper runs in an isolated, sandboxed child process with its own volatile ephemeral session directory.
   - Session cookies and authentication states required for Web Home synchronization live only in a permission-restricted jar that is deleted when the session ends or when the helper exits.
   - The helper only handles designated playlist and personalized recommendation synchronization and does not act as a general-purpose web browser.

5. **Direct Media Resolving**
   - Streaming URLs are resolved locally on your machine via bundled, open-source `yt-dlp` tool directly to YouTube's media servers.
   - Stream traffic flows directly between your machine and YouTube media CDNs without any intermediary proxy or proxy service operated by Muses.

6. **Automatic Updates**
   - When enabled, update checking contacts GitHub's public API (`api.github.com/repos/xiaotwu/Muses-Euterpe/releases`) to query release tags and version metadata.
   - No personal identifiers, machine IDs, or unique device fingerprints are sent during update checks.

## Data Removal

You can delete all application data at any time by clearing:
- Windows: `%LOCALAPPDATA%\Muses`
- Or by choosing **Settings > General > Clear Cache & Reset Data** within the application.
