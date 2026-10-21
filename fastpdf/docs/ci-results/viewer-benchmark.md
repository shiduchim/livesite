# EK FastPDF viewer benchmark

- Machine: runnervmgnbhw, Microsoft Windows 10.0.20348
- CPU: 4 logical / 2 cores, 16 GB RAM; render workers: 2
- GPU: Microsoft Basic Render Driver (vendor 0x1414, 0 MB dedicated, 8189 MB shared); WPF render tier 2; mode Default
- Viewport: 1044x672 DIP at 1x DPI

| Document | Scenario | Result (ms) | Frame interval p50/p95 | UI frame build p95 | Input→frame p95 |
|---|---|---:|---:|---:|---:|
| cad-dense-e1.pdf | open: document ready | 22 |  |  |  |
| cad-dense-e1.pdf | open: first pixels | 682 |  |  |  |
| cad-dense-e1.pdf | open: viewport fully sharp | 881 |  |  |  |
| cad-dense-e1.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 60%) | 1069 | 16.0 / 31.7 | 5.92 | 13.4 |
| cad-dense-e1.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 75%) | 71 | 15.8 / 32.3 | 0.77 | 15.7 |
| cad-dense-e1.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 86%) | 72 | 16.0 / 16.5 | 0.62 | 8.9 |
| cad-dense-e1.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 2 | 16.3 / 31.9 | 0.27 | 10.7 |
| cad-extreme-e1.pdf | open: document ready | 8 |  |  |  |
| cad-extreme-e1.pdf | open: first pixels | 1267 |  |  |  |
| cad-extreme-e1.pdf | open: viewport fully sharp | 2595 |  |  |  |
| cad-extreme-e1.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 57%) | 2881 | 16.0 / 16.8 | 0.35 | 5.6 |
| cad-extreme-e1.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 68%) | 58 | 16.1 / 32.5 | 0.57 | 7.2 |
| cad-extreme-e1.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 73%) | 59 | 16.0 / 16.5 | 0.32 | 11.0 |
| cad-extreme-e1.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 91%) | 1 | 16.0 / 31.5 | 0.14 | 11.1 |
| image-heavy.pdf | open: document ready | 6 |  |  |  |
| image-heavy.pdf | open: first pixels | 87 |  |  |  |
| image-heavy.pdf | open: viewport fully sharp | 343 |  |  |  |
| image-heavy.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 57%) | 233 | 15.9 / 16.3 | 0.27 | 15.7 |
| image-heavy.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 78%) | 63 | 16.2 / 31.9 | 0.30 | 14.4 |
| image-heavy.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 85%) | 69 | 16.0 / 31.4 | 0.31 | 10.3 |
| image-heavy.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 1 | 16.2 / 31.6 | 0.14 | 9.2 |
| many-pages-600.pdf | open: document ready | 17 |  |  |  |
| many-pages-600.pdf | open: first pixels | 32 |  |  |  |
| many-pages-600.pdf | open: viewport fully sharp | 226 |  |  |  |
| many-pages-600.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 51%) | 192 | 16.0 / 16.5 | 0.25 | 5.3 |
| many-pages-600.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 73%) | 71 | 16.1 / 31.9 | 0.32 | 9.9 |
| many-pages-600.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 80%) | 56 | 16.0 / 16.1 | 0.29 | 10.7 |
| many-pages-600.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 16 | 31.6 / 31.8 | 0.14 | 6.9 |
| many-pages-600.pdf | jump to middle page: sharp | 191 |  |  |  |
| many-pages-600.pdf | jump back to page 2: sharp | 162 |  |  |  |
| cad-set-12-sheets.pdf | open: document ready | 11 |  |  |  |
| cad-set-12-sheets.pdf | open: first pixels | 225 |  |  |  |
| cad-set-12-sheets.pdf | open: viewport fully sharp | 658 |  |  |  |
| cad-set-12-sheets.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 53%) | 433 | 15.9 / 16.2 | 0.29 | 12.0 |
| cad-set-12-sheets.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 77%) | 59 | 16.2 / 31.9 | 0.32 | 9.2 |
| cad-set-12-sheets.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 85%) | 70 | 16.0 / 31.5 | 0.31 | 14.1 |
| cad-set-12-sheets.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 16 | 16.1 / 31.8 | 0.15 | 7.2 |
| cad-dense-e1.pdf | tab switch: sharp | 4 |  |  |  |
| cad-extreme-e1.pdf | tab switch: sharp | 2 |  |  |  |
| image-heavy.pdf | tab switch: sharp | 3 |  |  |  |
| many-pages-600.pdf | tab switch: sharp | 3 |  |  |  |
| cad-set-12-sheets.pdf | tab switch: sharp | 9 |  |  |  |
| many-pages-600.pdf | search 600 pages while panning: first hit | 129 | 16.0 / 16.9 | 0.13 | 12.7 |
| many-pages-600.pdf | search 600 pages while panning: complete (600 hits) | 1309 |  |  |  |

## Resources at end of run

- viewerWorkingSetMB: 1184.43
- viewerPrivateMB: 1137.79
- workerProcesses: 2
- workersWorkingSetMB: 602.94
- tileCacheMB: 389.48
- tileCacheHitRate: 0.79
- vramUsageMB: 0.01
- vramBudgetMB: 7523.9
- tileRenderP50: 13.35
- tileRenderP95: 353.79
- tiles: 284
- queueWaitP50: 68.49
- queueWaitP95: 1815.35
- workerCrashes: 0
