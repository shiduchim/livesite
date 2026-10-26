# Offline 3D PDF: Revit exporter 0.5.3

This is the Revit side of the offline clickable 3D PDF prototype. **Export 3D PDF** in Revit writes a cropped, coloured GLB model and a registered drawing into one PDF, which the Offline 3D PDF Viewer opens. The viewer and reader are unchanged; their source is in `shiduchim/files`, `PDFReader_Source_v0.5.2`.

## Update an existing 0.5.2 installation

1. Download [`release/0.5.3/Offline3D-RevitExporter-0.5.3-update.zip`](release/0.5.3/Offline3D-RevitExporter-0.5.3-update.zip). Its SHA-256 is in the `.sha256` file next to it.
2. Close Revit and extract the whole ZIP.
3. Double-click `UPDATE.cmd`. It replaces only the exporter DLL that Revit loads, and keeps a backup. `ROLLBACK.cmd` undoes it.
4. Start Revit. The panel should read **Offline 3D PDF v0.5.3**.
5. Follow [ACCEPTANCE-TEST.md](ACCEPTANCE-TEST.md) and send back the log folder (`%LOCALAPPDATA%\Offline3DPdfPrototype\Logs\Exports`).

**Status:** cross-built and tested automatically (Linux, simulated Revit callbacks, the unchanged reader's validator and packager). **It has not yet been run inside Revit.**

## What changed and why

[CHANGES-0.5.3.md](CHANGES-0.5.3.md) has the full diagnosis, file list and measurements. In short:

- **Linked models:** host elements that come after a link are no longer lost or shifted.
- **Repeated families** (sockets, fixtures): verified reuse, so Revit no longer tessellates each copy.
- **Speed:** the GLB writer is about 33 times faster on large unique geometry.
- **Large crops:** they no longer stop at the 64 MiB-per-element or 100,000-part limits.
- **Options dialog:** compact and DPI-aware, with the preview rendered inside it and the buttons always visible.
- **Logs:** a live export trace for every run.

Developer notes are in [HANDOFF.md](HANDOFF.md).
