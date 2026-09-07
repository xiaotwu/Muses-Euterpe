# Installing Muses

Muses (Windows 11 port: Euterpe) can be installed via MSIX package, portable release, or built directly from source code.

## System Requirements

- **Operating System**: Windows 11 (recommended for Mica and native caption integration) or Windows 10 (version 2004 / build 19041 or newer).
- **Architecture**: 64-bit (`x64` or `arm64`).
- **Runtime**:
  - For framework-dependent builds: [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
  - For self-contained packages: No runtime installation required.

---

## Installation Options

### Method 1: MSIX Package (Recommended)

1. Download the latest `Muses-x.y.z-win-x64.msix` package from [GitHub Releases](https://github.com/xiaotwu/Muses-Euterpe/releases).
2. Double-click the `.msix` file and select **Install** via the Windows App Installer.
3. Alternatively, install via PowerShell:
   ```powershell
   Add-AppxPackage -Path .\Muses-0.1.0-win-x64.msix
   ```

### Method 2: WinGet (Windows Package Manager)

Once published to the community repository:
```powershell
winget install xiaotwu.Muses
```

### Method 3: Portable ZIP

1. Download `Muses-win-x64.zip` from GitHub Releases.
2. Extract the archive to any desired location (e.g., `C:\Tools\Muses` or `%LOCALAPPDATA%\Programs\Muses`).
3. Launch `Muses.exe`.
4. (Optional) Create a shortcut on your Desktop or pin to Taskbar / Start Menu.

---

## Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.100 or newer)
- Git
- `python3` (for asset generation scripts if customizing)

### Steps

1. **Clone the repository**:
   ```bash
   git clone https://github.com/xiaotwu/Muses-Euterpe.git
   cd Muses-Euterpe
   ```

2. **Run tests**:
   ```bash
   dotnet test
   ```

3. **Build and run the application**:
   ```bash
   dotnet run --project src/Muses.App
   ```

4. **Package MSIX (Windows only)**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\package-msix.ps1 -Configuration Release
   ```

---

## Media Engine & Streaming Requirements

Muses resolves streaming URLs using `yt-dlp`.
- A compatible binary is automatically copied into the app directory during build.
- If you wish to use a system-installed `yt-dlp`, ensure `yt-dlp.exe` is available in your system `PATH`.
- To update the bundled `yt-dlp` manually, run `yt-dlp -U` or replace `yt-dlp.exe` in the application directory.

---

## Keyboard Shortcuts Reference

| Shortcut | Action |
| --- | --- |
| `Space` | Play / Pause (when not focusing input) |
| `Ctrl + P` | Play / Pause |
| `Ctrl + Right` | Next Track |
| `Ctrl + Left` | Previous Track |
| `Ctrl + F` | Open Search Window |
| `Ctrl + V` | Open Paste URL Dialog |
| `Ctrl + L` | Toggle Lyrics Drawer |
| `Ctrl + U` | Toggle Queue Drawer |
| `Ctrl + E` | Toggle 32-band Equalizer |
| `Ctrl + N` | Open Track Notes & Bookmarks |
| `Ctrl + I` | Open Music Inbox |
| `Ctrl + ,` | Open Settings |
| `F11` | Toggle Fullscreen Now Playing |
| `Escape` | Dismiss modal dialogs / drawers |

---

## Troubleshooting

- **No audio output**: Verify default Windows audio output device in Windows Settings > Sound. Check the volume slider in Muses.
- **Stream fails to resolve**: Ensure internet connectivity and check that `yt-dlp` has not been blocked by firewall software.
- **SmartScreen warning**: If downloading pre-release unsigned MSIX packages, click *More info* -> *Run anyway*, or install the publisher certificate into the Trusted People store.
