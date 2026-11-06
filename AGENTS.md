# Muses Euterpe project guidance

Euterpe is the Windows desktop edition of Muses. The installed application is named Muses. Polyhymnia defines the product and the application content, motion, and interactions. Windows provides native window decorations, system media controls, credential storage, and packaging.

- Use Avalonia 12 AXAML with the shared Muses palette, typography, and control materials. Follow current Polyhymnia source for the navigation islands, floating player, queue, and Now Playing.
- Keep browsing surfaces focused on artwork. Apply translucent materials within bounded control groups and preserve keyboard access, focus, legibility, and reduced motion.
- Keep playback, queue, and persistence behind the existing C# service boundaries. Use yt-dlp and mpv for audio, and keep the IFrame video session separate. The product library is YouTube-backed.
- Preserve existing library and queue data on upgrade. Keep account credentials in platform storage and browser consent and cookies within the isolated helper boundary.
- User-facing strings use L10n. Identifiers and code comments use English.
- Run dotnet build and dotnet test. Compare the actual desktop app against the Mac reference before claiming visual or interaction parity.
- Keep implementation and validation resource use bounded. Reuse existing dependencies, keep builds and smoke profiles in task-owned locations, and stop test processes when finished. Remove task-owned temporary files, superseded build artifacts, and caches once they are no longer needed. Preserve user data, shared dependency caches, and files or tools still in use by another task.
- Project-Muses owns the shared website and release aggregation. Euterpe owns Windows builds and MSIX release assets. Match the package to the running Windows architecture.

See [desktop port plan](docs/desktop-port.md) and [family baseline](https://github.com/xiaotwu/Project-Muses/blob/main/docs/platform-baseline.md).
