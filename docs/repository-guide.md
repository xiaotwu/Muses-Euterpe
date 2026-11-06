# Euterpe engineering

Euterpe hosts the Muses desktop application for Windows. [Polyhymnia](https://github.com/xiaotwu/Muses-Polyhymnia) is the reference for the artwork-led interface, navigation, playback interactions, and visual language. See the [desktop port plan](desktop-port.md) for the current implementation and acceptance stages.

| Project | Responsibility |
| --- | --- |
| Muses.App | Avalonia 12 views, shared theme, view models, and desktop composition |
| Muses.Core | Playback facade, queue and library policies, domain values, and localization |
| Muses.Persistence | SQLite schema, upgrade path, and repository implementations |
| Muses.Infrastructure | Media resolution, mpv IPC, account, history, lyrics, discovery, and update metadata |
| Muses.Platform.Windows | Windows media controls, hotkeys, audio output probe, and tray commands |
| Muses.WebHome and Muses.WebHome.Helper | Opt-in browser session access in an isolated helper |
| Muses.Tests | Domain, persistence, playback, and release selection checks |

Use [the source build instructions](../INSTALL.md) and verify desktop behavior on Windows 11 x64. Euterpe owns the Windows packages. Project-Muses owns the website and release aggregation.
