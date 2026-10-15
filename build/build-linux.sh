#!/usr/bin/env bash
# Full build on Linux: tests, MinGW native DLL, .NET loaders/tool, Inno Setup
# (under Wine). Needs: dotnet SDK 8 + 10, mingw-w64, wine (with 32-bit
# support) and Inno Setup 6 (ISCC.exe; set ISCC=/path/to/ISCC.exe).
# The release build is produced by CI on Windows (build/build.ps1, MSVC).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/out"
PAYLOAD="$OUT/payload"
ISCC="${ISCC:-/opt/inno/tools/ISCC.exe}"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props")"
rm -rf "$PAYLOAD" "$OUT/installer"
mkdir -p "$PAYLOAD"

echo "== unit tests"
dotnet test "$ROOT/tests/EKTriangle.Tests" -c Release --nologo -v q

echo "== native (MinGW)"
"$ROOT/build/build-native-mingw.sh" "$OUT/mingw"

echo "== helper and loaders"
dotnet build "$ROOT/src/tool/EKTriangle.Tool.csproj" -c Release --nologo -v q -o "$OUT/tool"
cp "$OUT/tool/EKTriangle.exe" "$OUT/tool/EKTriangle.exe.config" "$PAYLOAD/"
for y in 2024 2025 2026 2027; do
  dotnet build "$ROOT/src/loader/Revit$y/EKTriangle.Loader.$y.csproj" -c Release --nologo -v q -o "$OUT/loader$y"
  mkdir -p "$PAYLOAD/Revit$y"
  cp "$OUT/loader$y/EKTriangle.dll" "$PAYLOAD/Revit$y/"
  cp "$OUT/mingw/EKTriangleHook.dll" "$PAYLOAD/Revit$y/"
done

echo "== installer"
export WINEDEBUG=-all
( cd "$ROOT/installer" && wine "$ISCC" /Q "/DAppVersion=$VERSION" "/DPayload=$(winepath -w "$PAYLOAD")" "/DOutDir=$(winepath -w "$OUT/installer")" EKTriangle.iss )
( cd "$OUT/installer" && sha256sum EK_Triangle_Setup.exe | tee EK_Triangle_Setup.exe.sha256 )
