# Status — v0.5.3 experimental

New in v0.5.3. The full list is in CHANGES-0.5.3.md.

**Viewer**
- The 3D model is handed to the page in memory, which fixes "Loading 3D model… / Failed to fetch".
- Revit-like Shaded lighting and true material colours.
- Black edges, plus silhouettes of curved and cut faces.
- Hidden Line and edge-only Wireframe styles.
- Edges are drawn in batches: a 14,736-node test model opens in about 2 s instead of 31 s.
- The draw list is frozen between changes: CPU per frame while orbiting drops from 62 ms to 14 ms.
- Exact zoom to fit (double-click on the wheel) and Revit-shaped grips.

**Exporter**
- Linked-model stack fix.
- Verified reuse of repeated families.
- A writer about 33 times faster.
- No 64 MiB-per-element or 100,000-part hard stops.
- Compact options dialog with the preview inside it.
- A live export trace.

**Tests**
- Exporter: 23 core checks, 150 assembly checks and 119 original v0.5.2 checks pass.
- Viewer: browser checks on the shipped page files pass:
  - loading (both transfer paths and both message orders);
  - selection, Hide, Isolate, X-ray, Show all;
  - all five styles;
  - edges on and off;
  - box edits with solid cuts, and zoom to fit;
  - switching to Three.js and back.

**Not yet confirmed on Windows**
- WebView2 on Windows, real Revit models and physical GPU speed are untested here.
- You have exported successfully with the 0.5.3 exporter; viewing that export with this viewer is the next acceptance step.
