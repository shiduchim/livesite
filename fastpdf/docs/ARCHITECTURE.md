# EK FastPDF 1.2 – rendering architecture

## 1. What was slow in v1.1 (source analysis + measurement)

| # | Finding in v1.1 | Effect | Evidence |
|---|---|---|---|
| 1 | `PdfEngine.Render` calls `FPDF_LoadPage` / `FPDF_ClosePage` **for every tile**. | The page's content stream is parsed again for each 768 px tile. On a dense Revit sheet, parsing costs hundreds of milliseconds per tile. | Engine benchmark "v1.1" rows: 15 tiles at 400 % take ~3.1 s on the dense sheet; the same tiles take 0.23 s with the page kept parsed (1 worker). |
| 2 | One static `Gate` lock around every PDFium call, for all documents. | Rendering, search, page-size queries and other tabs all queue behind each other. At most one CPU core rasterises. | Code review; `PdfEngine.cs`. |
| 3 | Tiles render strictly one after another (`await Task.Run(...)` inside a loop). | 12-core CPU used as a 1-core CPU. Tiles become visible one by one → the "square-by-square" effect. | Code review; `PaintVisibleAsync`. |
| 4 | Tile cache key contains the exact page pixel size. Zoom factor is `1.15^delta`, so wheel zoom produces a new resolution at almost every notch. | Little cache reuse when zooming back and forth. | Code review. |
| 5 | Every tile is a WPF `Image` element in a `Canvas` inside a `ScrollViewer`; zooming changes element sizes and triggers layout. | Layout passes on the UI thread during zoom, scrolling via `ScrollViewer` offsets. | Code review. |
| 6 | No way to stop a running PDFium render. | A slow tile for an obsolete zoom level blocks the next one. | `FPDF_RenderPageBitmap` is not interruptible. |
| 7 | Search runs on the same lock. | Searching a large document stalls rendering. | Code review. |
| 8 | PDFium is in the UI process. | A crash inside PDFium (malformed PDF) kills the viewer with all tabs. | Design. |

## 2. Decision

**Keep PDFium, rebuild everything around it.** Tabs, search and the visual style were rewritten on the new
architecture; no v1.1 rendering code survives.

*Why not another engine?* MuPDF is the realistic alternative (fast, thread-safe display lists). It is
licensed under the **AGPL v3** unless a commercial licence is bought from Artifex. Distributing EK FastPDF
with MuPDF would require publishing the full application source under AGPL, or paying for a licence.
A like-for-like measurement (whole sheet at 96 dpi = 4032×2880 px, including open and parse, one thread, same
Linux VM) gave **PDFium 1.1 s vs MuPDF 1.23 `mutool draw` 2.9 s** on the dense synthetic sheet and **4.4 s vs 7.7 s**
on the extreme sheet (docs/BENCHMARKS.md) – no evidence that MuPDF would be faster for this kind of content. PDFium (BSD/Apache, used by Chrome) is mature, has progressive (interruptible)
rendering and is already what v1.1 used. The bottlenecks were in how it was used, not in PDFium.

## 3. Pipeline

```
 UI process (EKFastPDF.exe)                                  Render workers (EKFastPDF.exe --render-worker) × N
 ┌──────────────────────────────────────────────┐            ┌───────────────────────────────────────────┐
 │ PdfViewport (FrameworkElement)               │  requests  │ own PDFium instance, single thread        │
 │  – view transform (zoom/offset, device px)   │ ─────────► │ parsed-page cache (6 pages)               │
 │  – per frame: decide what is visible,        │  stdin     │ FPDF_RenderPageBitmap_Start (progressive, │
 │    request tiles, draw what is cached        │            │   pause callback = cancel)                │
 │ TileStore: LRU cache of frozen bitmaps       │ ◄───────── │ writes BGRx pixels straight into          │
 │ RenderPool: priority queue + cancel          │  replies   │   shared memory (no pipe copy)            │
 │ WPF composition → Direct3D (GPU)             │  stdout    │ search / text extraction / GDI printing   │
 └──────────────────────────────────────────────┘            └───────────────────────────────────────────┘
```

### 3.1 CPU: out-of-process parallel rasterisation
* PDFium is not thread-safe, so real parallelism needs separate PDFium instances. EK FastPDF starts **N worker
  processes** (the same EXE with `--render-worker`). Each keeps its parsed pages, so a page is parsed once per
  worker, not once per tile.
* **Worker count** (`HardwareProfile.RecommendedWorkers`): number of *performance* cores − 1 on Intel hybrid
  CPUs (detected with `GetLogicalProcessorInformationEx` → `EfficiencyClass`); physical cores otherwise; at most
  8 and at most one per 3 GB RAM. An **i7-12700 (8P+4E) gets 7 workers**, leaving a P-core for the UI and
  composition threads. Override: `RenderWorkers` in `%APPDATA%\EK FastPDF\settings.json`.
  Rationale: tiles of one view finish together only if they run on equally fast cores; a tile scheduled on an
  E-core becomes the last one and delays "fully sharp". Benchmarks sweep 1…N workers (docs/BENCHMARKS.md).
* Workers opt out of Windows 11 **EcoQoS power throttling** (`SetProcessInformation(ProcessPowerThrottling)`),
  because windowless background processes are otherwise candidates for low-clock E-core scheduling.
* **Shared memory**: the worker creates its PDFium bitmap directly on a shared section; the viewer converts it into a
  frozen WPF bitmap on a pool thread (one copy, never on the UI thread).
* **Cancellation**: obsolete renders are interrupted inside PDFium through the progressive-render pause callback,
  which polls a cancel word in shared memory. Queued jobs are withdrawn. Measured: a 3000×3000 render of the
  extreme sheet is abandoned within a fraction of its full time (test `Cancel_InterruptsRunningRender`).
* **Crash isolation**: a worker crash is detected (pipe EOF), the worker restarts and the job is retried once.
  The viewer and the other tabs keep running (test `WorkerCrash_IsRecoveredTransparently`).
* Search and text extraction are worker jobs at lower priority, limited to `workers/4` (min 1) at a time, so
  rendering always has free workers (test `Search_DoesNotBlockRendering`).

### 3.2 Scheduling (what renders first)
Priorities, lowest first: page overviews of visible pages → visible exact-resolution tiles, ordered by distance
from the mouse cursor (or the view centre) → overscan ring, extended in the direction of panning → overviews of
the neighbouring pages → search. Every frame the viewer recomputes the wanted set; queued jobs that are no longer
wanted are withdrawn, running jobs of an obsolete zoom level are interrupted. Jobs of an inactive tab are withdrawn
when the tab loses focus (its cached tiles remain). During continuous wheel zooming, exact tiles are requested only
after the zoom has been still for 70 ms, so workers are not spent on intermediate zoom levels.

### 3.3 Resolution levels and caching
* **Zoom ladder**: wheel and toolbar zoom move along 2^(k/6) (≈12 % per notch). Zooming in and back out lands on
  exactly the same scales, so tiles are reused instead of re-rendered.
* **Level key** = device pixels per point (× 10⁶). The same zoom on the same monitor always produces the same pixel
  grid; a different DPI monitor is a different level (always rendered at the real device resolution, like v1.1).
* **Overviews**: each page has a whole-page bitmap (long edge 256 / 1024 / 1600 px, chosen by on-screen size). It is
  the immediate fallback while zooming, scrolling into new pages and while sharp tiles are pending – no blank pages.
* **Tiles**: 512 px (configurable; benchmarked 256/512/1024). Each tile is rendered with a hidden 24 px margin and
  cropped: PDFium anti-aliases paths and positions glyphs slightly differently at a bitmap edge; with the margin,
  adjacent tiles are **pixel-identical to one large render** (test `AdjacentTiles_AreIdenticalToOneLargeRender`),
  so there are no seams. One extra overlap pixel is drawn under neighbours for scaled previews.
* **Cache**: byte-budgeted LRU shared by all tabs, default RAM/32 (1 GB on a 32 GB PC; 256 MB…1.5 GB), override
  `CacheMegabytes`. Bitmaps used in the current frame are never evicted by that frame.

### 3.4 GPU presentation
* The viewport is a single `FrameworkElement` with three retained `DrawingVisual`s (preview layer, sharp layer,
  overlay layer). A frame is a list of `DrawImage` commands that reference immutable bitmaps; WPF's composition
  engine (Direct3D 9Ex on the GPU, independent render thread) uploads each bitmap **once** as a texture and
  re-composites every frame. Zoom and pan only change coordinates: no layout pass, no new textures, no re-render.
* Sharp tiles are placed on whole device pixels with nearest-neighbour sampling (1:1, no filter blur). Previews use
  bilinear filtering on the GPU.
* **Atomic reveal**: newly arrived sharp tiles are held until *every* visible tile is ready, then all appear together
  with a 110 ms cross-fade over the scaled preview. If a view takes longer than 650 ms (extreme pages), tiles are
  released in batches every 180 ms instead of one by one.
* Why not a custom Direct3D 11 swap chain? Measured UI-thread frame build time is 0.1–0.8 ms at p95 in most
  scenarios (6 ms worst case, during the first zoom of a document) and frames arrive at the 16.7 ms display interval
  (p50) even with CPU-only composition on the CI VM (docs/BENCHMARKS.md). The expensive part is CPU rasterisation, which no
  presentation API changes. A D3D11 surface inside WPF needs `D3DImage` (D3D9Ex shared surface, an extra copy per
  frame) or an `HwndHost` (airspace problems with tooltips, menus and future markup overlays). The presentation is
  isolated in `PdfViewport`, so a D3D11/DirectComposition presenter can be added later if profiling on the target
  GPU shows composition cost.
* GPU and video memory are queried at run time through DXGI (`GpuInfo`), shown in the diagnostics report and logged
  in benchmarks (process VRAM use via `IDXGIAdapter3::QueryVideoMemoryInfo`). Nothing assumes a particular GPU.
* **Software fallback**: WPF falls back to software composition automatically when no suitable GPU/driver exists
  (render tier 0) and recovers from device loss on its own (re-uploads textures). `--software-rendering` or
  `"RenderMode": "Software"` forces software composition (tested in CI).

### 3.5 Large documents
* Opening reads only the cross-reference table and page dictionaries (`FPDF_LoadDocument` + `FPDF_GetPageSizeByIndexF`);
  no page content is parsed until a page becomes visible. PDFium reads the file on demand (64-bit offsets; the v1.1
  4 GB limit came from its custom file-access callback and is gone).
* Continuous vertical scrolling over all pages (v1.1 showed one page at a time).
* Password-protected PDFs prompt for the password; damaged or non-PDF files show an error in their tab.

### 3.6 Annotation overlay (for EK HDsnip features later)
`IViewportOverlay.Render(DrawingContext, OverlayContext)` draws in **page display points** (top-left origin, page
rotation applied – the same space as search results). `OverlayContext.PageToScreen` / `ScreenToPage` convert between
page points and device pixels for the current frame, so a markup layer stays aligned at every zoom without touching
the renderer. Search highlights are the first overlay (`SearchHighlightOverlay`).

## 4. Source map

| Area | Files |
|---|---|
| PDFium interop, worker process | `src/EKFastPDF.Core/Native/Pdfium.cs`, `WorkerHost.cs`, `WindowsPrinter.cs`, `WindowsPerformance.cs` |
| IPC | `src/EKFastPDF.Core/Ipc/Protocol.cs`, `SharedBuffer.cs` |
| Scheduler / pool | `src/EKFastPDF.Core/Rendering/RenderPool.cs`, `Jobs.cs`, `TileGrid.cs` |
| Geometry | `src/EKFastPDF.Core/Layout/DocumentLayout.cs`, `Viewport.cs` |
| Cache | `src/EKFastPDF.Core/Caching/ByteBudgetCache.cs`, `src/EKFastPDF/Viewer/TileStore.cs` |
| Viewport / presentation | `src/EKFastPDF/Viewer/PdfViewport.cs` |
| Tabs, search | `src/EKFastPDF/Viewer/DocumentSession.cs`, `SearchController.cs`, `MainWindow.xaml(.cs)` |
| Start-up, diagnostics | `src/EKFastPDF/Program.cs`, `Services/DiagnosticsReport.cs`, `Services/GpuInfo.cs` |
| Benchmarks | `tools/EKFastPDF.Bench` (engine), `src/EKFastPDF/BenchmarkRunner.cs` (real viewer) |
