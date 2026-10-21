# EK FastPDF viewer benchmark

- Machine: runnervmgnbhw, Microsoft Windows 10.0.20348
- CPU: 4 logical / 2 cores, 16 GB RAM; render workers: 2
- GPU: Microsoft Basic Render Driver (vendor 0x1414, 0 MB dedicated, 8189 MB shared); WPF render tier 2; mode Default
- Viewport: 1044x672 DIP at 1x DPI

| Document | Scenario | Result (ms) | Frame interval p50/p95 | UI frame build p95 | Input→frame p95 |
|---|---|---:|---:|---:|---:|
| cad-dense-e1.pdf | open: document ready | 26 |  |  |  |
| cad-dense-e1.pdf | open: first pixels | 683 |  |  |  |
| cad-dense-e1.pdf | open: viewport fully sharp | 1172 |  |  |  |
| cad-dense-e1.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 58%) | 1294 | 16.1 / 22.4 | 6.57 | 13.0 |
| cad-dense-e1.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 76%) | 62 | 26.2 / 31.7 | 0.93 | 16.7 |
| cad-dense-e1.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 86%) | 78 | 31.2 / 31.9 | 0.95 | 16.2 |
| cad-dense-e1.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 3 | 16.2 / 31.8 | 0.27 | 10.9 |
| cad-extreme-e1.pdf | open: document ready | 9 |  |  |  |
| cad-extreme-e1.pdf | open: first pixels | 1454 |  |  |  |
| cad-extreme-e1.pdf | open: viewport fully sharp | 2971 |  |  |  |
| cad-extreme-e1.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 59%) | 3315 | 15.9 / 16.9 | 0.35 | 13.1 |
| cad-extreme-e1.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 76%) | 70 | 24.0 / 31.7 | 0.31 | 15.8 |
| cad-extreme-e1.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 86%) | 59 | 16.0 / 16.2 | 0.26 | 10.3 |
| cad-extreme-e1.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 16 | 16.3 / 31.6 | 0.14 | 8.7 |
| image-heavy.pdf | open: document ready | 6 |  |  |  |
| image-heavy.pdf | open: first pixels | 108 |  |  |  |
| image-heavy.pdf | open: viewport fully sharp | 377 |  |  |  |
| image-heavy.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 55%) | 243 | 15.9 / 16.7 | 0.24 | 16.2 |
| image-heavy.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 79%) | 67 | 16.8 / 28.2 | 0.30 | 16.2 |
| image-heavy.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 85%) | 84 | 16.0 / 26.3 | 0.34 | 14.7 |
| image-heavy.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 16 | 16.1 / 20.5 | 0.13 | 13.4 |
| many-pages-600.pdf | open: document ready | 10 |  |  |  |
| many-pages-600.pdf | open: first pixels | 29 |  |  |  |
| many-pages-600.pdf | open: viewport fully sharp | 239 |  |  |  |
| many-pages-600.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 56%) | 184 | 15.9 / 18.7 | 0.28 | 11.9 |
| many-pages-600.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 74%) | 72 | 16.2 / 32.4 | 0.29 | 12.3 |
| many-pages-600.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 80%) | 70 | 16.0 / 16.3 | 0.24 | 10.8 |
| many-pages-600.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 16 | 30.6 / 31.8 | 0.12 | 13.1 |
| many-pages-600.pdf | jump to middle page: sharp | 197 |  |  |  |
| many-pages-600.pdf | jump back to page 2: sharp | 157 |  |  |  |
| cad-set-12-sheets.pdf | open: document ready | 10 |  |  |  |
| cad-set-12-sheets.pdf | open: first pixels | 243 |  |  |  |
| cad-set-12-sheets.pdf | open: viewport fully sharp | 961 |  |  |  |
| cad-set-12-sheets.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 55%) | 493 | 16.0 / 16.4 | 0.29 | 10.9 |
| cad-set-12-sheets.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 77%) | 63 | 15.7 / 29.5 | 0.32 | 15.7 |
| cad-set-12-sheets.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 85%) | 62 | 15.6 / 15.9 | 0.31 | 15.6 |
| cad-set-12-sheets.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 16 | 16.4 / 31.6 | 0.11 | 11.0 |
| cad-dense-e1.pdf | tab switch: sharp | 2 |  |  |  |
| cad-extreme-e1.pdf | tab switch: sharp | 2 |  |  |  |
| image-heavy.pdf | tab switch: sharp | 2 |  |  |  |
| many-pages-600.pdf | tab switch: sharp | 2 |  |  |  |
| cad-set-12-sheets.pdf | tab switch: sharp | 2 |  |  |  |
| many-pages-600.pdf | search 600 pages while panning: first hit | 12 | 15.9 / 16.2 | 0.27 | 13.0 |
| many-pages-600.pdf | search 600 pages while panning: complete (600 hits) | 1140 |  |  |  |

## Resources at end of run

- viewerWorkingSetMB: 1070.52
- viewerPrivateMB: 1007.15
- workerProcesses: 2
- workersWorkingSetMB: 598.24
- tileCacheMB: 397.28
- tileCacheHitRate: 0.81
- vramUsageMB: 0.01
- vramBudgetMB: 7523.9
- tileRenderP50: 15.99
- tileRenderP95: 329.22
- tiles: 291
- queueWaitP50: 78.17
- queueWaitP95: 2017.55
- workerCrashes: 0
