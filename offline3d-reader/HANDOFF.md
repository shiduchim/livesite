# Working PDF reader source — v0.5.3

**0.5.3 changes this reader:**
- It fixes the 3D view stuck on "Loading 3D model…".
- It adds the Revit-like display, faster large models and exact zoom to fit.

See [CHANGES-0.5.3.md](CHANGES-0.5.3.md). The paragraphs below describe the v0.5.2 baseline this was imported from (commit ba54c9f).

This is the user's working FastPDF-based 2D/3D reader baseline, provided separately from the Revit exporter. Keep this folder read-only during exporter fixes. All copied application files retain their original bytes; no new reader behavior is introduced by the handoff.

The active application project is fastpdf/src/EKFastPDF/EKFastPDF.csproj. It uses fastpdf/src/EKFastPDF.Core, src/Core, src/Packaging, src/AutomaticRegistration and src/Viewer/web. The older standalone Viewer project and legacy FastPDF v1.1 are intentionally excluded. Third-party offline browser assets and licenses are included.

This is source, not a prebuilt installer. It is separate from the user's ordinary installed FastPDF app. No Revit exporter code is in this archive. Matching shared-library files appear in both source ZIPs to keep their project graphs complete.

## Developer verification for Claude

    dotnet build fastpdf/src/EKFastPDF/EKFastPDF.csproj -c Release -p:RestoreLockedMode=false
    dotnet publish fastpdf/src/EKFastPDF/EKFastPDF.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=false -o build/Viewer
    dotnet test fastpdf/tests/EKFastPDF.Tests/EKFastPDF.Tests.csproj -c Release -p:RestoreLockedMode=false

The SDK pin is .NET 10.0.100. The viewer targets net8.0-windows; portable tests target net8.0. Cross-compilation is possible on Linux, but native viewer/Revit acceptance requires Windows. Windows WebView2 is a runtime dependency. The original source uses PDFium and self-contained .NET publishing.

Read CURRENT_CONTRACT.md before changing export format. The reader helper handles --auto-export and completes PDF registration/embedding. Source entry: fastpdf/src/EKFastPDF/Program.cs. Shared finalizer: src/AutomaticRegistration/ExportFinalizer.cs. Strict package/GLB validation: src/Core/Contract.cs and src/Packaging/PdfPackage.cs.

SOURCE_MANIFEST.json proves the copied file identities. evidence/ contains retained baseline results. Do not treat synthetic data or cross-builds as real-model Revit tests.
