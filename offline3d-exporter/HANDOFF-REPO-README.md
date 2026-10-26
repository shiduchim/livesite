# Offline Revit to clickable 3D PDF — source handoff

Baseline v0.5.2. Start with [CLAUDE_PROMPT.txt](CLAUDE_PROMPT.txt), [HISTORY_AND_RESULTS.md](HISTORY_AND_RESULTS.md), and [CURRENT_CONTRACT.md](CURRENT_CONTRACT.md).

- `RevitExporter_Source_v0.5.2/`: edit this Revit exporter.
- `PDFReader_Source_v0.5.2/`: preserve this working FastPDF reader as a reference.

Keep the folder structure intact. The exporter uses the installed reader helper for final PDF registration and embedding. These are source snapshots. The packaged installer belongs in GitHub Releases. Native Revit correctness and performance still require acceptance testing; the user reports preview generation now works but its popup and options layout need improvement.
