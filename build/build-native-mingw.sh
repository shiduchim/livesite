#!/usr/bin/env bash
# Cross-compiles the native DLL and its test harness with MinGW-w64 (Linux).
# Used for fast local checks; release builds use MSVC (build/build.ps1 / CI).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/out/mingw}"
mkdir -p "$OUT"
MH="$ROOT/src/native/third_party/minhook"
CC=x86_64-w64-mingw32-gcc
CXX=x86_64-w64-mingw32-g++
OBJ="$OUT/obj"; mkdir -p "$OBJ"
for c in hook buffer trampoline; do $CC -O2 -c "$MH/src/$c.c" -I"$MH/include" -o "$OBJ/$c.o"; done
$CC -O2 -c "$MH/src/hde/hde64.c" -o "$OBJ/hde64.o"
x86_64-w64-mingw32-windres -I"$ROOT/src/native" "$ROOT/src/native/EKTriangleHook.rc" -O coff -o "$OBJ/res.o"
$CXX -O2 -std=c++17 -Wall -Wno-unknown-pragmas -Wno-cast-function-type -DUNICODE -D_UNICODE -DEKT_VERSION=1.0.0 \
    -I"$MH/include" -shared -static -static-libgcc -static-libstdc++ \
    "$ROOT/src/native/EKTriangleHook.cpp" "$OBJ"/*.o -o "$OUT/EKTriangleHook.dll" \
    -luxtheme -lgdiplus -lgdi32 -luser32 -Wl,--exclude-all-symbols
$CXX -O2 -std=c++17 -DUNICODE -D_UNICODE -static -static-libgcc -static-libstdc++ \
    -municode "$ROOT/tests/native/NativeTest.cpp" -o "$OUT/Revit.exe" -luxtheme -lgdi32 -luser32
cp "$OUT/Revit.exe" "$OUT/NotRevit.exe"
echo "native build OK: $OUT"
