# Euterpe Phase 4

Shipped: EQ → engine, Web Home isolation boundary, docs/CI honesty, leftover cleanup.

## Verify

```bash
dotnet test
dotnet build Muses.slnx
```

Automated on Win11 (2026-09-09 PT): **167 passed**, build green.

## Win11 manual follow-ups

- [ ] Audible EQ with real mpv: change BassBoost while playing and confirm tonal change. — Manual audio follow-up (code: `ProcessStreamEngine.SetEq` → mpv `af` equalizer).
- [ ] Web Home opt-in in Settings → helper process starts; cookie dir under temp `muses-web-home-helper` disappears after helper exit. — Manual follow-up.
- [ ] Run `scripts/package-msix.ps1` on Windows (not macOS). — Manual follow-up.
- [x] Confirm Store/WinGet still unpublished; do not advertise installers that do not exist. — Pass (README Not claimed section).

## Win11 implementation notes (Phase Win11)

- Stream quality: `PrefKey.AudioQuality` maps via `ProcessStreamEngine.MapAudioQualityPref` (`best`→`bestaudio`, `high`→`128k`, `medium`→`64k`).
- ReplayGain: when enabled and track metadata has dB, applied through mpv `volume=` filter alongside EQ.
- Settings GitHub link: `https://github.com/xiaotwu/Muses-Euterpe`.