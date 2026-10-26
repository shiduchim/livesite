Offline 3D PDF - Revit exporter 0.5.3 update (for an existing 0.5.2 installation)

What it does
  Replaces only the Revit exporter DLL that Revit already loads for Offline 3D PDF
  (found through %APPDATA%\Autodesk\Revit\Addins\<year>\Offline3DPdfPrototype.addin).
  The viewer, the PDF helper and the PDF/3D file format are unchanged.
  The previous DLL is kept next to it as *.v0.5.2.bak.

How to use
  1. Close Revit.
  2. Extract this whole ZIP to a folder.
  3. Double-click UPDATE.cmd.
  4. Start Revit. The Add-Ins ribbon panel reads "Offline 3D PDF v0.5.3".
  To undo: close Revit and double-click ROLLBACK.cmd.

Status
  Cross-built on Linux for Revit 2024 (.NET Framework 4.8), 2025/2026 (.NET 8 and
  .NET 10 hosts) and 2027 (.NET 10) against the official API reference packages,
  with automated geometry, packaging and assembly checks. It has NOT yet been run
  inside Revit. Please follow ACCEPTANCE-TEST.md and send the export log folder
  (%LOCALAPPDATA%\Offline3DPdfPrototype\Logs\Exports) after the first exports.

The scripts are plain text; read them before running if your company requires it.
If Windows or company policy blocks a script or DLL, stop and ask IT to review.
