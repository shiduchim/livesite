#!/usr/bin/env bash
# Builds the six exporters and assembles the drop-in update ZIP for an existing
# 0.5.2 installation. Usage: build/package-update.sh [out-dir]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$(realpath -m "${1:-$ROOT/out}")"
NAME="Offline3D-RevitExporter-0.5.3-update"
STAGE="$OUT/$NAME"
rm -rf "$STAGE" "$OUT/$NAME.zip"; mkdir -p "$STAGE"
"$ROOT/build/build-exporters.sh" "$OUT/exporters"
mkdir -p "$STAGE/Exporters"
for d in "$OUT/exporters"/*/; do cp -r "$d" "$STAGE/Exporters/"; done
cp "$ROOT/build/update-package/Update-Offline3DExporter.ps1" "$ROOT/build/update-package/README.txt" "$ROOT/ACCEPTANCE-TEST.md" "$STAGE/"
cp "$ROOT/build/update-package/UPDATE.cmd" "$ROOT/build/update-package/ROLLBACK.cmd" "$STAGE/"
# Windows line endings for the launchers and text files.
for f in "$STAGE"/*.cmd "$STAGE"/*.txt "$STAGE"/*.ps1 "$STAGE"/*.md; do sed -i 's/\r$//; s/$/\r/' "$f"; done
( cd "$STAGE" && find Exporters -name '*.dll' | sort | xargs sha256sum > SHA256SUMS.txt )
( cd "$OUT" && rm -f "$NAME.zip" && zip -qr "$NAME.zip" "$NAME" )
( cd "$OUT" && sha256sum "$NAME.zip" > "$NAME.zip.sha256" )
echo "Package: $OUT/$NAME.zip"
