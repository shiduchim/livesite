#!/usr/bin/env bash
# Cross-builds the six Revit exporter variants (needs the .NET 10 SDK and NuGet
# access; no Revit). Output: <out>/<year>-<framework>/Offline3DPrototype.Revit<year>.dll (+ .pdb)
# Usage: build/build-exporters.sh [out-dir]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$(realpath -m "${1:-$ROOT/out/exporters}")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
rm -rf "$OUT"; mkdir -p "$OUT"
for variant in 2024:net48 2025:net8.0-windows 2025:net10.0-windows 2026:net8.0-windows 2026:net10.0-windows 2027:net10.0-windows; do
  year=${variant%%:*}; fw=${variant#*:}
  host=""; [ "$fw" != "net48" ] && host="-p:RevitHostFramework=$fw"
  stage="$(mktemp -d)"
  dotnet build "$ROOT/src/RevitExporter/RevitExporter.csproj" -c Release -p:RevitYear=$year $host \
    -p:RestoreLockedMode=false -p:RestorePackagesWithLockFile=false -o "$stage" -v q -nologo > "$OUT/build-$year-$fw.txt" 2>&1 \
    || { cat "$OUT/build-$year-$fw.txt"; exit 1; }
  mkdir -p "$OUT/$year-$fw"
  cp "$stage/Offline3DPrototype.Revit$year.dll" "$stage/Offline3DPrototype.Revit$year.pdb" "$OUT/$year-$fw/"
  for f in RevitAPI.dll RevitAPIUI.dll; do [ ! -e "$OUT/$year-$fw/$f" ] || { echo "Autodesk references must not be redistributed"; exit 1; }; done
  rm -rf "$stage"
  echo "$year $fw: $(grep -E 'Warn|Error' "$OUT/build-$year-$fw.txt" | tr '\n' ' ')"
done
( cd "$OUT" && sha256sum */*.dll > SHA256SUMS.txt )
echo "Exporters in $OUT"
