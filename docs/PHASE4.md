# Euterpe Phase 4

Shipped on the Mac checkout: EQ → engine, Web Home isolation boundary, docs/CI honesty, leftover cleanup.

## Verify

```bash
dotnet test
dotnet build
```

## Win11 manual follow-ups

- [ ] Audible EQ with real mpv: change BassBoost while playing and confirm tonal change.
- [ ] Web Home opt-in in Settings → helper process starts; cookie dir under temp `muses-web-home-helper` disappears after helper exit.
- [ ] Run `scripts/package-msix.ps1` on Windows (not macOS).
- [ ] Confirm Store/WinGet still unpublished; do not advertise installers that do not exist.
