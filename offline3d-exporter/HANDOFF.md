# Revit exporter source — v0.5.2

Give this archive to Claude for focused exporter work. Start with CLAUDE_PROMPT.txt, HISTORY_AND_RESULTS.md and CURRENT_CONTRACT.md. Latest feedback: preview now works; its separate build popup and oversized scrolling options need improvement.

The original application source is copied without edits. The reader UI is excluded. Shared geometry/contract and PDF finalizer libraries are included so Claude can review the compatibility boundary and run portable tests. These duplicate the matching files in the reader archive intentionally.

The exporter still needs the installed viewer helper to finalize PDFs. This source archive is not an installer or a standalone replacement application. Keep the two extracted archives in separate sibling folders; do not flatten their paths.

Entry project: src/RevitExporter/RevitExporter.csproj. Main flow: ExportCommand.cs, MeshContext.cs, ExportRegions.cs, ExportPreviewDialog.cs, ExportProgressDialog.cs, PreviewFraming.cs, ExportTarget.cs and ExportScope.cs. Shared writer: src/Core/SpoolSceneWriter.cs.

## Developer verification for Claude

.NET 10 SDK with NuGet access is sufficient for cross-compilation; licensed Revit is needed to execute the plugin. Build examples (for Claude, not end-user installation):

    dotnet build src/RevitExporter/RevitExporter.csproj -c Release -p:RevitYear=2024 -p:RestoreLockedMode=false
    dotnet build src/RevitExporter/RevitExporter.csproj -c Release -p:RevitYear=2026 -p:RestoreLockedMode=false
    dotnet build src/RevitExporter/RevitExporter.csproj -c Release -p:RevitYear=2026 -p:RevitHostFramework=net10.0-windows -p:RestoreLockedMode=false
    dotnet run --project tests/CropExportTests -c Release
    dotnet run --project tests/LargeSceneTests -c Release
    dotnet run --project tests/ExportWireInterop -c Release

Portable tests target .NET 8 and need its runtime as well. Other target years are 2025 and 2027. RevitApiDir may select a locally installed API instead of pinned build references. No Autodesk runtime DLLs, credentials, user RVT files or generated application binaries are included.

SOURCE_MANIFEST.json records original paths and hashes. Original test results are in evidence/. Installer scripts in reference-only/ are combined-package examples, not usable split installers. EXPORTER_CODE_FOR_CHAT.txt concatenates the exporter and shared boundary source for a chat interface that cannot unpack archives; actual project files are authoritative.
