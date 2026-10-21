# EK FastPDF 1.2

Fast, sharp PDF viewer for large architectural and electrical drawings (Revit sheet exports) and long
document sets. Windows 10/11 x64.

## Install
1. Run **`EK_FastPDF_Setup_v1.2.exe`**. The installer is not code-signed: if Windows SmartScreen says
   *"Windows protected your PC"*, click *More info → Run anyway*.
2. Keep *Offer EK FastPDF in "Open with" for PDF files* ticked if you want it in the right-click *Open with*
   menu. Your default PDF program is **not** changed. To make EK FastPDF the default, right-click a PDF →
   *Open with → Choose another app → EK FastPDF → Always*.
3. Start it from the Start menu (or the desktop shortcut if you chose one), or open a PDF with it.

Everything it needs is included (its own .NET runtime and PDFium); nothing else is installed or downloaded.
Upgrade by running a newer setup; uninstall from *Settings → Apps*. A **portable** ZIP is also built: extract it to a
local folder and run `EKFastPDF.exe`.

## Use
| Action | How |
|---|---|
| Open | Ctrl+O, the folder button, drag files onto the window, or Explorer *Open with* (opens as a new tab in the running window) |
| Zoom at the mouse | Ctrl + mouse wheel (or touchpad pinch). ⋯ → *Mouse wheel zooms* swaps wheel/Ctrl+wheel |
| Pan | Drag with the left or middle mouse button; arrow keys; Space / Shift+Space |
| Zoom steps / presets | Ctrl+= / Ctrl+−, zoom box (type a percentage), ▾ presets |
| Fit page / 100 % / fit width | Ctrl+0 / Ctrl+1 / Ctrl+2 |
| Pages | Page Up / Page Down, Ctrl+Home / Ctrl+End, page box |
| Search | Ctrl+F, Enter / F3 next, Shift+Enter / Shift+F3 previous, Esc clears |
| Tabs | Ctrl+Tab / Ctrl+Shift+Tab, Ctrl+W or middle-click a tab to close |
| Print | Ctrl+P (vector printing at full printer resolution) |
| Render statistics overlay | F12 |
| Diagnostics report | Ctrl+Shift+D, or ⋯ → *Diagnostics report* |

Pages are rendered by several background processes at once; the bottom bar shows *Sharp* when everything visible is
rendered at full resolution. Text search finds real PDF text (no OCR); scanned drawings report *No searchable text*.

## Settings and logs
`%APPDATA%\EK FastPDF\settings.json` (0 = automatic):
`RenderWorkers`, `CacheMegabytes`, `TileSize` (512), `LcdText` (true), `WheelZooms` (false),
`RenderMode` (`Auto` or `Software`), `DebugOverlay`.
Log: `%LOCALAPPDATA%\EK FastPDF\logs\fastpdf.log`. Command line: `EKFastPDF.exe file.pdf …`,
`--software-rendering`, `--diagnose`, `--benchmark <folder> [--quick]`.

## Documentation
* [Architecture and design decisions](docs/ARCHITECTURE.md)
* [Benchmarks (measured)](docs/BENCHMARKS.md)
* [Start-up error investigation](docs/LAUNCH-INVESTIGATION.md)
* [Tests to run on your PC](docs/WINDOWS-TEST-CHECKLIST.md)

## Build from source
Windows with the .NET 8 SDK: double-click `BUILD_WINDOWS.cmd` (or `build\build.ps1`). With Inno Setup 6 installed it
also produces the installer. Output: `out\installer\`. The GitHub workflow `.github/workflows/fastpdf.yml` builds,
tests, benchmarks, installs and launch-tests on Windows for every change.

Engine tests and benchmarks also run on Linux: `dotnet test tests/EKFastPDF.Tests`,
`dotnet run -c Release --project tools/EKFastPDF.Bench -- engine <sample folder>`.

## Licences
PDFium (BSD-3-Clause / Apache-2.0) and the .NET runtime (MIT) are redistributed; see `THIRD-PARTY-NOTICES.txt`.
