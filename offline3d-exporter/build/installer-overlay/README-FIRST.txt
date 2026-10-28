Offline 3D PDF v0.5.3 — EXPERIMENTAL PREBUILT WINDOWS PACKAGE

EXTRACT ZIP -> CLOSE REVIT AND THE 3D VIEWER -> DOUBLE-CLICK INSTALL.cmd

No SDK, compilation, commands or runtime paths are needed. Setup detects full
Revit 2024–2027 and installs the matching exporter and the viewer. You can
install over 0.5.2 directly. If Microsoft WebView2 is missing, setup asks
before obtaining it online. Your regular FastPDF reader and ClickTo3D remain
separate. No PDF association is changed. UNINSTALL.cmd removes only this
prototype.

WHAT IS NEW IN 0.5.3 (details in CHANGES-0.5.3.md)
Viewer:
- The 3D view loads. In 0.5.2 it could stay on "Loading 3D model…" with
  "Failed to fetch"; the viewer now passes the model to the 3D page directly.
- Revit-like display:
  - Shaded uses Revit's lighting, with tops in the material colour and sides
    progressively darker.
  - Colours are true to the Revit material; 0.5.2 showed them too dark.
  - Black edges, plus outlines on curved objects and section cuts.
  - Styles: Wireframe, Hidden line, Shaded, Consistent colors and Realistic.
- Faster:
  - Edges are drawn in a few batches instead of one object per element. On a
    15,000-part test model, opening went from 31 s to 2 s.
  - Orbiting needs about a quarter of the CPU work.
- Navigation:
  - Double-click the mouse wheel to zoom to fit, as in Revit.
  - Home view and Focus fit exactly.
  - The section box grips have Revit's arrow shape.
Exporter (same as the 0.5.3 exporter update ZIP):
- Host elements after a linked model are no longer lost or shifted.
- Repeated families are tessellated once and reused.
- Writing the model is about 33 times faster on large geometry.
- No more hard stops at 64 MiB per element or 100,000 parts.
- Compact options dialog with the preview inside it.
- A live export log is kept in
  %LOCALAPPDATA%\Offline3DPdfPrototype\Logs\Exports

WHERE TO FIND IT
Reopen Revit: Add-Ins > Offline 3D PDF v0.5.3 > Export 3D PDF.
Windows Start: Offline 3D PDF Viewer.
The viewer title and the 3D header both show v0.5.3.
Extract this package into a new folder; do not mix old and new package files.

EXPORT
Open a model view directly (no sheet required), or open a sheet. A single
supported viewport is automatic; sheets with several supported model views
include all of them. Plans/RCPs, sections, elevations and orthographic 3D views
are supported. Plan crops must be rectangular and unsplit.

The default is VIEW CROP ONLY, at MEDIUM detail. Plans and RCPs use the
nearest levels below and above. ALL UP and ALL DOWN extend height only.
INCLUDE GEOMETRY OUTSIDE THE CROP is off and asks you to confirm, with No as
the default. QUICK EXPORT uses Revit's Coarse geometry for urgent reviews.
Loaded visible links, phases, worksets and element/link hides are respected.
Unloaded links cannot be exported.

VIEW
Open the PDF with Offline 3D PDF Viewer and click 3D box, then click the
drawing to place the section. Drag the blue paired triangles to stretch a box
face.
- Left-drag orbits around the point under the pointer, or around the selected
  element.
- Middle-drag or right-drag pans. Shift+middle-drag orbits, as in Revit.
- The wheel zooms toward the pointer.
- Double-click the wheel fits the box; double-click an element focuses it.
- Home view returns to the default orientation and fits the box.
Settings > Display chooses the style. Edges, Shadows, Contact shading and
Solid cuts are under Lighting and sections. Contact shading applies on High
and Extra High quality. Tools has Hide, Isolate, X-ray, Show all and Measure.

RE-EXPORT OLD GRAY PDFS to obtain material colours, normals and metadata.
Cut patterns are stored as metadata; caps are filled with the material colour,
not hatched. Textures and exact Autodesk rendering are not implemented. Very
large sections have drawing budgets (8/16/32/64 million active triangles for
Low/Medium/High/Extra High). Exceeding one pauses 3D with a message; nothing
is removed from the PDF.

TEST STATUS
The exporter and viewer were cross-built and tested automatically on Linux:
- simulated Revit callbacks;
- 150 assembly checks;
- the unchanged PDF packager and validator;
- a real Chromium browser running the shipped 3D page with synthetic models of
  1 and 100 rooms.
You have exported successfully with the 0.5.3 exporter. This viewer build has
NOT yet run in WebView2 on Windows or with a real Revit model. Please report
what you see: the setup log, the export log folder above and a screenshot of
the 3D view.
BUILD_AND_INSTALL.cmd is a separate automatic Windows fallback; it is not
needed for the included prebuilt INSTALL.cmd installation.
