# Offline 3D PDF Viewer 0.5.3

0.5.3 fixes the 3D view that stayed on "Loading 3D model…". It also makes the view look and behave like Revit, and makes large models much faster. Everything was tested in headless Chromium on Linux, using the shipped page files and synthetic exports. **It has not yet run in WebView2 on Windows.**

Unchanged:
- the PDF format and the GLB contract;
- the helper command (`--auto-export`);
- the settings files;
- the Three.js renderer option.

## Loading fix

| | |
|---|---|
| **Symptom** | The PDF opened, but the 3D panel stayed on "Loading 3D model…" and reported "Failed to fetch". |
| **Cause** | The page fetched `https://offline3d-v052.local/scene.glb`. `BoxPanel` answered that request from `WebResourceRequested` on a host that was also mapped to a folder with `SetVirtualHostNameToFolderMapping`. WebView2 does not reliably raise the event for a mapped host, and `.local` names add a DNS delay, so the request failed. |
| **Fix** | `BoxPanel.SendScene` now hands the GLB over without a network request:<br>• It writes the GLB into a WebView2 shared buffer and posts it read-only (`PostSharedBufferToScript`).<br>• If shared buffers are unavailable, it sends the GLB in 4 MiB base64 chunks.<br>• The page (`three.mjs`) accepts whichever complete copy arrives first, in any order. It checks the byte count and releases the shared buffer.<br>• After 60 s it reports a clear error instead of waiting forever.<br>The page host is now `offline3d.example`. |

## Revit-like display (`renderer.mjs`, new `outlines.mjs`, `sections.mjs`)

- **Colours.** glTF stores Revit's sRGB colours as linear values. 0.5.2 showed those linear values as if they were sRGB, so every colour came out too dark. Shaded and Consistent Colors now convert back, so Consistent Colors shows the exact material colour.
- **Shaded lighting.** Revit's model: an even ambient light plus a key light fixed to the view, from the upper left behind the viewer. Top faces show the material colour. In the home view the two visible side faces show about 82% and 62% of it.
- **Edges.** Model edges are black, 1 px, and the selection is blue. Faces sit just behind the lines (polygon offset), so edges no longer flicker.
- **Silhouettes.** A screen-space pass reads view distance and normals from a geometry buffer. It outlines curved surfaces (pipes, cylinders) and cut faces against what lies behind them. It runs on Medium and above, and is off on Low.
- **Section cuts.** Each cap gets a black outline.
- **Display styles:**
  - New Hidden Line: white faces with black edges.
  - Wireframe draws only model edges, including hidden ones, plus silhouettes of curved faces.
  - The list follows Revit's order.
- **Image.**
  - Opaque white background.
  - Multisampling (4× on Medium) followed by FXAA.
  - Contact shading shares the geometry buffer and also works in Shaded (High and Extra High).
- **Section box grips** match Revit's arrows: two 16 × 17 px triangles with a 6 px gap, #6085D6 with a dark outline.

## Speed

Measured on a synthetic 100-room export (14,736 nodes, 1.5 million placed triangles) in Chromium:

| | 0.5.2 | 0.5.3 |
|---|---|---|
| Open the 3D model (whole model in the box) | 30.7 s | 1.5–2.1 s |
| Draw calls per frame | 14,758 | 418 |
| Materials | 14,784 | 17 |
| CPU time per frame while orbiting | 62 ms | 14 ms |

How:
- **Edges.** Feature edges are one thin-instanced line mesh per unique geometry, plus one for the selection. 0.5.2 made one line mesh, and one shader material, per element. Each line mesh has its own geometry, because Babylon stores thin-instance matrices on the geometry. Black and blue lines use separate materials, because a shared material keeps its colour between draws.
- **Materials.** Display materials are shared per source material and style.
- **Frozen draw list.** Nothing moves, so after each visibility change the list of drawn meshes is frozen, with frustum culling off. Frames no longer re-cull thousands of instances.

The GPU cost of the extra geometry pass is not measured here: the tests use a software renderer.

## Navigation

- Home view, double-click on the wheel (Revit's zoom to fit), the host's "fit box" and Focus all fit the box or element exactly in the current orientation. 0.5.2 estimated the zoom from the largest dimension.
- The initial display settings follow the settings panel; in 0.5.2, Contact shading showed as on but was off.

## Packaging fixes

- `EKFastPDF.csproj` lists the web files one by one. It now includes the new `outlines.mjs`, and also `comparison-worker.mjs`. The Three.js renderer needs the worker for section cuts, but 0.5.2 never shipped it.
- The version is now 0.5.3 in the window title, the 3D header and the assembly.

## Not done

- Cut caps are filled with the material colour; Revit's cut hatch patterns are stored but not drawn.
- No textures or Autodesk render appearances.
- WebGPU keeps the 0.5.2 look without silhouettes (it is not offered in the UI).
