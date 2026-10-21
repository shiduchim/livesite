EK FASTPDF 1.1 - DISPLAY AND ZOOM PERFORMANCE UPDATE
====================================================

THIS IS AN UPDATED WINDOWS PROJECT (NOT A COMPILED EXE)

1. Extract this ZIP to a folder on your Windows desktop.
2. Close the old EK FastPDF before you build.
3. Double-click BUILD_WINDOWS.cmd.
4. Wait for "Build succeeded." At the prompt, type Y to start.
5. In future, run: EK FastPDF\EKFastPDF.exe

If you already had a working v1.0 viewer, it is not automatically modified.
The new exe will be in the NEW project's EK FastPDF folder after the build.
To avoid confusion, create a desktop shortcut to that new exe.

The first build requires .NET 8 SDK (64-bit), internet for NuGet/PDFium,
and Windows 10/11 x64. The published application bundles its .NET runtime.

RENDERING CHANGES vs 1.0:
- Before, zoom erased all tile images immediately, revealing white while new
  tiles rendered. Now zoom leaves a scaled preview until visible tiles finish.
- Rapid successive zoom steps retain the full previous preview instead of
  replacing it with a handful of partially rendered tiles.
- Real display-DPI rasterization: 125/150/200% Windows scaling is accounted
  for so images are not simply stretched from 96 DPI.
- Ctrl + Wheel zoom centers on cursor position; toolbar zoom uses page center.
- Tile-boundary pixel overlap helps prevent white hairline cracks/seams.
- PDFium LCD text antialiasing flag enabled for improved text sharpness.
- Visible image objects are reused on scrolling, instead of clearing the
  entire canvas for every scroll event.
- Tile cache raised from 48 fixed tiles to 512 MB LRU with reuse across
  zoom levels (adapted for a PC with 32 GB RAM).
- Less waiting before requesting visible tile renders (25ms vs 60ms).
- Native tile results already in progress are cached even if scrolling
  cancels the prior request, provided the document/zoom still matches.
- Tabs, search, page navigation, drag-drop, middle-button pan preserved.

LIMITS AND HONEST BENCHMARK STATUS:
- Performance has NOT been measured against Foxit on your hardware or PDFs.
- We cannot run/build Windows WPF binaries in this environment. This release
  has undergone static source and XAML validation, NOT a Windows runtime test.
- Very complex CAD/vector drawings may still be slower than Foxit because
  PDFium rasterizes the page geometry anew at every zoom level.
- A temporary enlarged preview during zoom may look soft until sharp tiles
  arrive. That is intentional and avoids flashing white areas.
- PDFs above 4 GiB, encrypted PDFs, continuous multi-page scrolling, and
  markups are not supported by this version.

KEYS:
Ctrl+O - Open    Ctrl+F - Search    Ctrl+W - Close tab
Ctrl+Wheel - Zoom at mouse pointer   Middle-button drag - Pan
Page Up / Down - Page navigation    Ctrl+Tab - Change tab

SOURCE:
src\EKFastPDF\PdfViewerControl.xaml.cs - Zoom, cache, tiles, search
src\EKFastPDF\PdfEngine.cs - PDFium native rendering & text extraction
src\EKFastPDF\MainWindow.xaml - User interface, tabs

PDFium: https://pdfium.googlesource.com/pdfium/
Windows native binaries: https://github.com/bblanchon/pdfium-binaries
Check the relevant licenses before redistributing the application.
