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
- [x] Run `scripts/package-msix.ps1` on Windows (not macOS). — Pass. Packed + signed `CN=xiaotwu`. After elevation, cert is in `LocalMachine\TrustedPeople`. Sideload **installed**: `xiaotwu.Muses` 0.1.0.0 (`WindowsApps\xiaotwu.Muses_0.1.0.0_x64__r58mek0329vyw`). Launch verified (window title Muses, responding) then stopped. Payload includes mpv.exe, yt-dlp.exe, Helpers\MusesWebHomeHelper.exe. Store / WinGet still unpublished.
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

`.github/workflows/ci.yml` still runs `ubuntu-latest` + `windows-latest`. Push `675c425`: [CI run 34414066986](https://github.com/xiaotwu/Muses-Euterpe/actions/runs/34414066986) **success** (ubuntu-latest + windows-latest).

## Stage 3 (this machine)

- Web Home identity: helper uses opted-in browser cookies (yt-dlp `--cookies-from-browser`) then yt-dlp Liked Videos (`LL`) to read `UC…`. No scrape in Avalonia. No fake personalized Home — verified session chip only.
- Global hotkeys: `RegisterHotKey` Ctrl+P / Left / Right when `FfGlobalHotkeys` is on (default off). In-window chords stay ungated.
- Light theme: Appearance combo Dark / Light / System. Dark remains default. Accent stays `#FA586A`.
- Clear Cache / Reset Data: Settings → General. Reset signs out, deletes sqlite + caches, quits.
- Audio Nerd: WASAPI default render device name when COM succeeds; otherwise Unknown. Never invents “Realtek…”. Device period left Unknown (no unsafe IAudioClient vtable).
- Crossfade: Settings slider 0–8 s overlaps two mpv sessions. 0 = off. mpv `--gapless-audio=weak`. YouTube streams are not gapless.
- Store / WinGet: skipped; remind later.
