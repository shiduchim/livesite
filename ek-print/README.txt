EK Print C# v6.2 — Revit 2024–2027
=====================================

INSTALL
1. Close every Revit window.
2. Run INSTALL_EK_PRINT_CSHARP.bat.
3. Reopen Revit and go to EK > Print.

VISIBLE BUTTONS
- Quick PDF
- Select PDF
- Quick DWG
- Select DWG
- Sheet Print

HIDDEN SLIDE-OUT
- Settings

PDF SETTINGS
- Use Sheet Size
- View links in blue: OFF
- Hide reference/work planes: ON
- Hide unreferenced view tags: ON
- Region edges mask coincident lines: OFF
- Hide scope boxes: ON
- Hide crop boundaries: ON
- Replace halftone with thin lines: OFF
- Export in background: OFF where supported

PDF EXPORT / CANCELLATION
- One PDF item keeps the fast direct Revit export path and shows no progress window.
- Separate mode with two or more sheets/views shows compact progress plus Cancel Print.
- Cancel is cooperative: it stops BEFORE the next Revit export. It never tries to interrupt a Document.Export already in progress.
- Applies to Quick PDF, Select PDF, and Sheet Print.
- Separate mode keeps PDFs already completed and skips the remaining items after Cancel.
- Combine mode: Revit builds the combined PDF itself in one export (PDFExportOptions.Combine). No third-party code.
- The combined PDF is exported to a temporary folder first; the old combined PDF is replaced only after Revit finishes.
  If the old PDF is open in a viewer, the new one is saved next to it as "Name (2).pdf".
- Combine mode cannot be stopped halfway, because Revit does the whole combined export in one step.
- Separate mode exports one item at a time with the progress window, so Cancel Print stops between sheets/views.
- If the combined export fails, the previous combined PDF is left untouched.

SHEET PRINT (v30 behavior ported to C#)
- Select views/sheets in the Project Browser and click Sheet Print.
- If nothing is selected, the active view is used as a fallback.
- If non-view/model objects are selected, Sheet Print does NOT silently fall back to the active view.
- Finds all printable host sheets, removes duplicates, supports legends on multiple sheets, and maps schedules through ScheduleSheetInstance.
- Selected views that are not placed on printable sheets are reported after the export.
- One sheet: Project - Sheet Number - Sheet Name.pdf
- Combine: Project - Selected Sheets.pdf
- Separate: Project - Sheet Number - Sheet Name.pdf

DWG SETTINGS
- AutoCAD 2013
- Centimeters
- Shared coordinates
- Colors specified in view
- Views on sheets merged instead of external references
- Only .dwg files are kept; sidecar files are discarded

BUILD / PERFORMANCE
- The add-in is C# and compiled against each installed Revit API (2024–2027).
- Revit itself still performs PDF rendering; C# removes pyRevit/IronPython command overhead but cannot make Revit's renderer faster.
- Cancellable multi-page jobs deliberately export one page at a time so Cancel can be honored between pages.

RIBBON ICONS (v6.1)
- Same icon artwork as v30, rebuilt cleanly from the original high-resolution design.
- Quick DWG / Select DWG now fill their canvas like the other icons (in v30 they were drawn at about half size with faint text).
- Every icon ships at each Windows display scale, and the add-in loads the exact match for the monitor:
    Large button (32): Name.png, Name_40.png, Name_48.png, Name_64.png      (100% / 125% / 150% / 200%)
    Small button (16): Name_16.png, Name_16_20.png, Name_16_24.png, Name_16_32.png
- Icons are pinned to exactly 32 / 16 logical pixels. v30 PNGs were read as 96.012 DPI, which made WPF resample them slightly soft.
- Settings keeps its original 32/16 icon (no high-resolution source).
- IconSource\ holds the high-res artwork, the rebuild script (build_icons.py) and the untouched v30 icons.

NO THIRD-PARTY CODE (v6.2)
- PDFsharp and the EKPdfMerge helper from v30/v6.1 were removed. Nothing is downloaded during install.
- The installer (and uninstaller) delete the old helper from %APPDATA%\EKPrintCSharp\Tools and the PDFsharp
  package files NuGet downloaded (%USERPROFILE%\.nuget\packages\pdfsharp*, NuGet download cache).

DIALOGS (v6.1)
- Select PDF, Select DWG, Settings and the export progress window were redesigned. Behavior is unchanged.
- Sheet/view list with Number / Name (or View name / Type) columns, larger check boxes, click a row to toggle it.
- Search box with clear button; Ctrl+F jumps to it. "Select all" / "Clear" apply to the items currently shown.
- Live preview of the file name that will be created, plus the current save folder in the footer.
- Print / Export is disabled until at least one item is checked.
- Everything is laid out through the display scale, so text and buttons are never clipped at 125–200%.
- Window opens sized to the screen it is on.
- Small fix: switching Separate -> Combine now restores the combined PDF name instead of keeping a sheet name;
  switching Combine -> Separate shows the real per-file name instead of the stale combined name.

IMPORTANT
Actual PDF/DWG export behavior must be tested inside Revit because Document.Export cannot be executed in this build environment.
