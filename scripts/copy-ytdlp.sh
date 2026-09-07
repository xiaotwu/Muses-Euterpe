#!/usr/bin/env bash
# Fetch the bundled yt-dlp binary. Wave 1 playback needs this; Wave 0 does not.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST_DIR="$ROOT/resources"
mkdir -p "$DEST_DIR"
OS="$(uname -s)"
if [[ "$OS" == "Darwin" ]]; then
  URL="https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos"
  OUT="$DEST_DIR/yt-dlp"
elif [[ "$OS" == MINGW* || "$OS" == MSYS* || "$OS" == CYGWIN* ]]; then
  URL="https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"
  OUT="$DEST_DIR/yt-dlp.exe"
else
  URL="https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp"
  OUT="$DEST_DIR/yt-dlp"
fi
curl -L --fail --output "$OUT" "$URL"
chmod +x "$OUT"
echo "Wrote $OUT"
