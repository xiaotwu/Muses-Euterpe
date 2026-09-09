# Euterpe Phase 4

Shipped: EQ → engine, Web Home isolation boundary, docs/CI honesty, leftover cleanup.

## Verify

```bash
dotnet test
dotnet build Muses.slnx
```

Automated on Win11 (2026-09-09 PT): **171 passed**, build green.

## Win11 manual follow-ups

- [ ] Audible EQ with real mpv: change BassBoost while playing and confirm tonal change. — Manual audio follow-up (code: `ProcessStreamEngine.SetEq` → mpv `af` equalizer). Live mpv stream probe this session: **Pass** (yt-dlp URL → mpv process stayed alive).
- [ ] Web Home opt-in in Settings → helper process starts; cookie dir under temp `muses-web-home-helper` disappears after helper exit. — Manual follow-up. Helper binary now copied to `Helpers/` next to the app and into MSIX publish layout. Identity may remain Unavailable — do not fake personalized Home.
- [x] Run `scripts/package-msix.ps1` on Windows (not macOS). — Pass. Packed with `MakeAppx.exe` from `Microsoft.Windows.SDK.BuildTools`. Signed with local `CN=xiaotwu` (thumbprint `ED2C5288E6966AF073E948D1BB7C1B96E16D7776`) and trusted in `CurrentUser\TrustedPeople`. Artifact: `artifacts/msix/Muses-0.1.0.0-win-x64.msix`. `signtool verify /pa` still fails (self-signed is not a public root). Store / WinGet still unpublished. SmartScreen may still warn once.
- [x] Confirm Store/WinGet still unpublished; do not advertise installers that do not exist. — Pass (README Not claimed section).

## Win11 implementation notes (Phase Win11)

- Stream quality: `PrefKey.AudioQuality` maps via `ProcessStreamEngine.MapAudioQualityPref` (`best`→`bestaudio`, `high`→`128k`, `medium`→`64k`). Changing quality while a track is loaded re-resolves.
- ReplayGain: when enabled and track metadata has dB, applied through mpv `volume=` filter alongside EQ.
- Settings GitHub link: `https://github.com/xiaotwu/Muses-Euterpe`.
- Quality page reports mpv missing vs found; does not claim "engine operational" without mpv.
- Vendored layout for `dotnet run` without PATH: `resources/mpv.exe`, `resources/yt-dlp.exe`, `resources/d3dcompiler_43.dll` (gitignored; copied to output when present).
- Overlay TearDown: pause iframe via JS API then blank HTML; `WebView2NativeHost` can be re-attached after overlay close.
- Automation: background engine only — Settings About states there is no visual rules editor.
- ShellViewModel settings/prefs seam split into `ShellViewModel.Settings.cs` (~350 lines). Main file ~1430 lines. Overlay/advanced remain in the main file.

## CI

`.github/workflows/ci.yml` still runs `ubuntu-latest` + `windows-latest`. Do not drop ubuntu. This session did not push, so GitHub Actions status was not re-run here.
