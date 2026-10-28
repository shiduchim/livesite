# Revit exporter source: 0.5.3

Start with [CHANGES-0.5.3.md](CHANGES-0.5.3.md), which has the diagnosis, the changes and the measurements. Then read [ACCEPTANCE-TEST.md](ACCEPTANCE-TEST.md), the steps that only Revit can confirm. Background is in HISTORY_AND_RESULTS.md, CURRENT_CONTRACT.md and the original CLAUDE_PROMPT.txt.

**Status.**
- 0.5.3 is cross-built for all six variants and covered by automated tests. The user has exported with it in Revit.
- The reader is now 0.5.3 too (`../offline3d-reader`, see its CHANGES-0.5.3.md).
- `build/package-full-installer.py` produces the full installer, `release/0.5.3/Offline3DPdfPrototype-0.5.3.zip`.
- The exporter still needs the installed viewer helper (`Viewer/Offline3D.FastPDF.Dev.exe`) to finalise PDFs.

## Where things are

- **Entry project:** `src/RevitExporter/RevitExporter.csproj`.
- **Main flow:** `ExportCommand.cs`.
- **Revit callbacks:** `RevitGeometryContext.cs`, which feeds `Collection/GeometryStream.cs`.
- **Revit-independent core in `Collection/`:**
  - geometry stream (links, instances, crop selection, instance reuse);
  - mesh builder;
  - scene writer (GLB, sharing, merging, reader limits);
  - feature edges;
  - statistics.
- **UI:**
  - `ExportOptionsDialog.cs` (options plus the in-dialog preview);
  - `ExportProgressDialog.cs`.
- **Unchanged from 0.5.2:**
  - region and registration math in `ExportRegions.cs`, `ExportScope.cs` and `CropClipper.cs`;
  - `ExportTarget.cs` and `PreviewFraming.cs`;
  - `LinkOverrideTransfer.cs` and the wire JSON writer.
- **Shared libraries:** `src/Core`, `src/Packaging` and `src/AutomaticRegistration` are unchanged copies of the reader's files. The Revit DLL links only pure files from `src/Core`, with no Core.dll or JSON package.

## Build and test (developer machine; .NET 10 SDK plus the .NET 8 runtime; no Revit needed)

    build/build-exporters.sh out/exporters                  # six variants
    build/package-update.sh out                             # drop-in update ZIP for a 0.5.2 install
    dotnet run --project tests/ExporterCoreTests -c Release  # 23 core checks (simulated Revit callbacks)
    dotnet run --project tests/ExporterAssemblyAudit -c Release -- out/exporters   # 150 metadata checks
    dotnet run --project tests/CropExportTests -c Release    # plus the other original suites

- **Dialog screenshots without Revit:** publish `tests/DialogHarness` for win-x64 and run it on Windows, or under Wine.
- **Local Revit API:** `-p:RevitApiDir=…` builds against a locally installed Revit API instead of the pinned reference packages.
- **Not included:** Autodesk runtime DLLs, user RVT files.

SOURCE_MANIFEST.json lists the 0.5.3 files and their hashes. `evidence/` holds the original v0.5.2 results together with the new `v053-*` results and dialog screenshots. Installer scripts in `reference-only/` are the original combined-package examples.
