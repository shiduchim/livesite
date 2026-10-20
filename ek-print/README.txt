EK Print C# v6.3 — Revit 2024–2027
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

WHAT'S NEW IN v6.3
- Sheet/view list: clicking a row now always checks/unchecks it. (Before, a click was lost if the mouse
  moved a few pixels while pressed, because Windows started a drag instead.) Shift+click sets a range.
- Select PDF / Select DWG: small "Settings" link in the footer, next to the save folder. Changes apply
  immediately to the open dialog.
- Select PDF: "Paper size" drop-down. Default is "Use sheet size". Any fixed size (A4-A0, B4-B1,
  ANSI A-E, ARCH A-E3) prints with Zoom = Fit to page, centered, orientation automatic.
  The choice is for that print only; the next print starts again at "Use sheet size".
- Cancel Print now works for combined PDFs too, and also in the middle of a sheet (see below).
- Settings: DWG Colors (Index color / Object styles / View), DWG unit (Foot, Inch, Meter, Centimeter,
  Millimeter) and Coordinates (Shared / Internal origin). They are saved and stay until you change them.
- Old files removed (see INSTALL CLEANUP).

PDF ENGINE AND QUALITY
- Uses Revit's own PDF export (Document.Export + PDFExportOptions) - the same engine as
  File > Export > PDF. No printer driver, no third-party code.
- Vector output (Always use raster: off), Color, Raster quality: High.
- Paper: Use sheet size (default) or a fixed size with Fit to page.
- View links in blue: OFF; Hide reference/work planes, unreferenced view tags, scope boxes and crop
  boundaries: ON; Region edges mask coincident lines: OFF; Replace halftone with thin lines: OFF.
- Export in background: OFF (Revit 2025+), so the PDF is finished when the export returns.

PDF EXPORT / CANCEL
- Every PDF is first written to a local work folder (%TEMP%\EKPrint) and copied to the save folder only
  when it is complete. A cancelled or failed export never leaves a half-written PDF and never touches an
  existing one. Network / OneDrive folders only ever receive finished files.
- Combine: Revit builds the combined PDF itself in ONE export. This is the fastest way and gives one clean
  vector PDF. (The alternative - printing every sheet to a temp folder and merging them afterwards - needs a
  third-party PDF merge library, is slower, and was removed in v6.2.)
- Cancel Print: the progress window runs on its own thread, so it stays responsive while Revit exports.
  Cancel is passed to Revit through its progress events, so it stops the running export, not just the next
  sheet. If Revit cannot stop at that moment, the export is discarded when it ends.
    Combined: nothing is saved; the existing PDF is not changed.
    Separate: PDFs already finished are kept; the rest are skipped.
- Single sheet/view: no progress window unless the export takes longer than 1.5 seconds.
- If the PDF is open in a viewer, the new one is saved next to it as "Name (2).pdf".

SHEET PRINT (v30 behavior ported to C#)
- Select views/sheets in the Project Browser and click Sheet Print.
- If nothing is selected, the active view is used as a fallback.
- If non-view/model objects are selected, Sheet Print does NOT silently fall back to the active view.
- Finds all printable host sheets, removes duplicates, supports legends on multiple sheets, and maps schedules through ScheduleSheetInstance.
- Selected views that are not placed on printable sheets are reported after the export.
- One sheet: Project - Sheet Number - Sheet Name.pdf
- Combine: Project - Selected Sheets.pdf
- Separate: Project - Sheet Number - Sheet Name.pdf

DWG SETTINGS (Settings > DWG export; saved between sessions)
- Colors: Index color (255) / Object styles (True Color) / View (True Color). Default: View.
- One DWG unit is: Foot / Inch / Meter / Centimeter / Millimeter. Default: Centimeter.
- Coordinates: Shared coordinates / Internal origin. Default: Shared.
- Always: AutoCAD 2013, views on sheets merged instead of external references,
  only .dwg files kept (sidecar files are discarded).

BUILD / PERFORMANCE
- The add-in is C# and compiled against each installed Revit API (2024–2027).
- Revit itself still performs PDF rendering; C# removes pyRevit/IronPython command overhead but cannot make Revit's renderer faster.
- Separate mode exports one file per Revit export; Combine uses one Revit export for the whole set.

RIBBON ICONS
- Every icon ships at each Windows display scale; the add-in loads the exact match for the monitor:
    Large button (32): Name.png, Name_40.png, Name_48.png, Name_64.png      (100% / 125% / 150% / 200%)
    Small button (16): Name_16.png, Name_16_20.png, Name_16_24.png, Name_16_32.png
- IconSource\ holds the high-resolution artwork and the rebuild script (build_icons.py).

INSTALL CLEANUP (v6.3)
The installer removes files left by older EK Print versions:
- The old pyRevit EK Print extension, including the "EKPrint.extension.DISABLED_BY_CSHARP" copies v6.2 left.
- The old pyRevit settings folder %APPDATA%\EKTools\QuickPrint (its save folder / mode is migrated first).
- Builds and .addin manifests for Revit versions that are no longer installed.
- Old temporary export folders (%TEMP%\EKDWG_*, ".EKPrint_work_*" folders in the save folder).
- The v30/v6.1 PDF merge helper and its PDFsharp downloads (as in v6.2).
Your settings.ini and your PDFs/DWGs are kept.

NO THIRD-PARTY CODE (v6.2)
- PDFsharp and the EKPdfMerge helper from v30/v6.1 were removed. Nothing is downloaded during install.
- The installer (and uninstaller) delete the old helper from %APPDATA%\EKPrintCSharp\Tools and the PDFsharp
  package files NuGet downloaded (%USERPROFILE%\.nuget\packages\pdfsharp*, NuGet download cache).

DIALOGS
- Sheet/view list with Number / Name (or View name / Type) columns. Click anywhere on a row to toggle it,
  Shift+click for a range, Space toggles the focused row.
- Search box with clear button; Ctrl+F jumps to it. "Select all" / "Clear" apply to the items currently shown.
- Live preview of the file name that will be created, plus the save folder and a Settings link in the footer.
- Print / Export is disabled until at least one item is checked.
- Everything is laid out through the display scale, so text and buttons are never clipped at 125-200%.

IMPORTANT
Actual PDF/DWG export behavior must be tested inside Revit because Document.Export cannot be executed in this build environment.
