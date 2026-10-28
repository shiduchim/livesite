# FastPDF integration in an isolated developer copy

Source of truth: uploaded `livesite-fastpdf-v1.2.0.zip`, **`fastpdf/src/`**. The archive's outer `src/` is another product and was not used. `fastpdf/legacy/v1.1-source/` remains unchanged reference material. This delivery's `fastpdf/` is a separate integration copy; no installed or released files were changed.

Run the published **`payload/Viewer/Offline3D.FastPDF.Dev.exe`**. Open `samples/SYNTHETIC-Offline3D.pdf`, choose **3D box**, then click in the plan. The docked or undocked 3D panel uses the shared package/coordinate services and locally bundled renderers. The optional PDF overlay shows synchronized bounds with draggable edge arrows; the native PDFium tile renderer remains in place. Hide releases the WebView2 control and its scene. Switching/closing tabs cancels pending activation and disposes resources to prevent stale document data.

The developer copy has a different executable name, single-instance mutex/pipe, settings folder, log folder and WebView2 cache. It does not register file associations or run the supplied production installer. It does not read released FastPDF settings. Registration/removal applies only to the separate Revit developer command.

## Rendering and coordinate path

PDFium version **157.0.8100** stays pinned as supplied. WPF tile compositing, tile cache, resolution ladder, progressive render code, print code and shader paths are retained. WebView2 never draws the PDF in this integration. While 3D is inactive there is no package scan, GLB decode, WebView2 control or per-frame 3D callback. The right dock has zero width.

After explicit activation, package integrity checks run off the UI thread. One metadata job obtains the PDFium page's normalized display→default-user-space affine mapping by evaluating `FPDF_DeviceToPage` at three canonical viewport corners. This is evaluated on the worker's existing serialized PDFium thread, never in the UI process. Intrinsic rotation and effective page boxes follow the same engine as the rendered page. Normalized WPF click positions include current layout, pan, zoom and DPI. No per-click native job is needed once the map is cached.

Mouse-up emits a plan click only for a left click with no meaningful drag. Existing left/middle panning and wheel zoom stay available. Click handlers are attached only while the 3D mode is active. Outside the registered crop is rejected and leaves the last box unchanged. This first package accepts one page, so other/multipage PDFs continue to render in 2D but do not activate 3D.

Worker protocol is bumped **3 → 4** for the new metadata operation; ship matching UI/worker binaries from the same build. Do not copy this Core DLL into an installed release.

## Verification and remaining performance gate

The unchanged input suite passes 31 tests. The integrated suite passes 36, including 4 native mapping cases with cropped, rotated, UserUnit pages and an end-to-end native mapping to the packaged model plane. Compare original versus integrated engine pixels and timings in `tests/engine-comparison.json`; the probe uses unchanged 1024×1325 renders on the small synthetic scene. This is evidence for the engine path only, not a Windows WPF/WebView2/GPU regression conclusion.

Before production integration, run the existing `BenchmarkRunner` viewer scenarios on the **same Windows machine** for original and integrated builds with 3D inactive, then measure active 3D contention, cold startup, click latency, orbit frame distribution, UI/worker/WebView2 working sets and GPU allocations. Keep the original source archive/build as baseline. Runtime/driver measurements and ordinary-reader printing remain pending.
