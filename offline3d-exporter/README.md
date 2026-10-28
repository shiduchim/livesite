# Offline 3D PDF 0.5.3: Revit exporter and viewer

This is the Revit side of the offline clickable 3D PDF prototype. **Export 3D PDF** in Revit writes a cropped, coloured GLB model and a registered drawing into one PDF, which the Offline 3D PDF Viewer opens. The viewer source is in [`../offline3d-reader`](../offline3d-reader); its changes are in [CHANGES-0.5.3.md](../offline3d-reader/CHANGES-0.5.3.md).

## Install (recommended): full 0.5.3 package

1. Download [`release/0.5.3/Offline3DPdfPrototype-0.5.3.zip`](release/0.5.3/Offline3DPdfPrototype-0.5.3.zip) (80 MB). Its SHA-256 is in the `.sha256` file next to it.
2. Close Revit and the 3D viewer, and extract the whole ZIP into a new folder.
3. Double-click `INSTALL.cmd`. It installs the 0.5.3 exporter for every Revit 2024–2027 it finds, and the 0.5.3 viewer. It installs over 0.5.2.
4. In Revit, the panel reads **Offline 3D PDF v0.5.3**. The viewer title and the 3D header read **v0.5.3**.

`build/package-full-installer.py` rebuilds this ZIP from the 0.5.2 package, the published viewer and the exporter builds.

## Exporter-only update

[`release/0.5.3/Offline3D-RevitExporter-0.5.3-update.zip`](release/0.5.3/Offline3D-RevitExporter-0.5.3-update.zip) replaces only the exporter DLL of an existing 0.5.2 installation (`UPDATE.cmd`, with `ROLLBACK.cmd`). The full package already contains the same DLLs.

Follow [ACCEPTANCE-TEST.md](ACCEPTANCE-TEST.md) and send back the log folder (`%LOCALAPPDATA%\Offline3DPdfPrototype\Logs\Exports`).

**Status:** cross-built and tested automatically:
- Linux, with simulated Revit callbacks;
- the reader's validator and packager;
- headless Chromium, running the shipped 3D page.

The exporter has exported successfully in Revit. The 0.5.3 viewer has not yet run in WebView2 on Windows.

## What changed and why

[CHANGES-0.5.3.md](CHANGES-0.5.3.md) has the full diagnosis, file list and measurements. In short:

- **Linked models:** host elements that come after a link are no longer lost or shifted.
- **Repeated families** (sockets, fixtures): verified reuse, so Revit no longer tessellates each copy.
- **Speed:** the GLB writer is about 33 times faster on large unique geometry.
- **Large crops:** they no longer stop at the 64 MiB-per-element or 100,000-part limits.
- **Options dialog:** compact and DPI-aware, with the preview rendered inside it and the buttons always visible.
- **Logs:** a live export trace for every run.

Developer notes are in [HANDOFF.md](HANDOFF.md).
