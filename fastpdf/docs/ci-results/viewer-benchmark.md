# EK FastPDF viewer benchmark

- Machine: runnervmgnbhw, Microsoft Windows 10.0.20348
- CPU: 4 logical / 2 cores, 16 GB RAM; render workers: 2
- GPU: Microsoft Basic Render Driver (vendor 0x1414, 0 MB dedicated, 8189 MB shared); WPF render tier 2; mode Default
- Viewport: 1044x672 DIP at 1x DPI

| Document | Scenario | Result (ms) | Frame interval p50/p95 | UI frame build p95 | Input→frame p95 |
|---|---|---:|---:|---:|---:|
| cad-dense-e1.pdf | open: document ready | 28 |  |  |  |
| cad-dense-e1.pdf | open: first pixels | 700 |  |  |  |
| cad-dense-e1.pdf | open: viewport fully sharp | 1210 |  |  |  |
| cad-dense-e1.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 61%) | 1936 | 16.0 / 16.9 | 5.76 | 12.0 |
| cad-dense-e1.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 77%) | 71 | 16.2 / 29.0 | 0.89 | 13.3 |
| cad-dense-e1.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 86%) | 58 | 16.0 / 16.9 | 0.67 | 15.2 |
| cad-dense-e1.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 16 | 16.6 / 31.7 | 0.28 | 10.5 |
| cad-extreme-e1.pdf | open: document ready | 10 |  |  |  |
| cad-extreme-e1.pdf | open: first pixels | 1747 |  |  |  |
| cad-extreme-e1.pdf | open: viewport fully sharp | 3413 |  |  |  |
| cad-extreme-e1.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 58%) | 3440 | 16.1 / 32.3 | 0.37 | 13.7 |
| cad-extreme-e1.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 70%) | 78 | 14.8 / 35.4 | 0.48 | 9.5 |
| cad-extreme-e1.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 73%) | 67 | 16.0 / 20.7 | 0.37 | 5.5 |
| cad-extreme-e1.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 99%) | 16 | 31.2 / 31.4 | 0.14 | 15.8 |
| image-heavy.pdf | open: document ready | 7 |  |  |  |
| image-heavy.pdf | open: first pixels | 121 |  |  |  |
| image-heavy.pdf | open: viewport fully sharp | 430 |  |  |  |
| image-heavy.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 58%) | 290 | 16.0 / 17.5 | 0.21 | 8.0 |
| image-heavy.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 76%) | 62 | 15.8 / 31.2 | 0.34 | 29.3 |
| image-heavy.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 84%) | 63 | 15.6 / 16.1 | 0.38 | 15.3 |
| image-heavy.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 15 | 31.2 / 31.5 | 0.16 | 11.9 |
| many-pages-600.pdf | open: document ready | 25 |  |  |  |
| many-pages-600.pdf | open: first pixels | 43 |  |  |  |
| many-pages-600.pdf | open: viewport fully sharp | 259 |  |  |  |
| many-pages-600.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 51%) | 211 | 15.9 / 16.7 | 0.29 | 13.3 |
| many-pages-600.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 71%) | 59 | 16.1 / 28.4 | 0.34 | 16.4 |
| many-pages-600.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 80%) | 85 | 16.0 / 16.2 | 0.34 | 13.1 |
| many-pages-600.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 16 | 31.5 / 31.9 | 0.16 | 5.3 |
| many-pages-600.pdf | jump to middle page: sharp | 207 |  |  |  |
| many-pages-600.pdf | jump back to page 2: sharp | 144 |  |  |  |
| cad-set-12-sheets.pdf | open: document ready | 11 |  |  |  |
| cad-set-12-sheets.pdf | open: first pixels | 255 |  |  |  |
| cad-set-12-sheets.pdf | open: viewport fully sharp | 1011 |  |  |  |
| cad-set-12-sheets.pdf | zoom in 10 notches (cold): sharp after last input (cache hits 54%) | 580 | 16.0 / 32.3 | 0.35 | 7.6 |
| cad-set-12-sheets.pdf | zoom out 10 notches (cached levels): sharp after last input (cache hits 78%) | 72 | 31.8 / 32.5 | 0.40 | 15.9 |
| cad-set-12-sheets.pdf | rapid zoom reversals 5x(3 in, 3 out): sharp after last input (cache hits 84%) | 67 | 16.1 / 31.8 | 0.33 | 15.2 |
| cad-set-12-sheets.pdf | drag-pan 90 frames at 1600%-class zoom: sharp after last input (cache hits 100%) | 1 | 16.2 / 24.8 | 0.15 | 10.6 |
| cad-dense-e1.pdf | tab switch: sharp | 3 |  |  |  |
| cad-extreme-e1.pdf | tab switch: sharp | 3 |  |  |  |
| image-heavy.pdf | tab switch: sharp | 3 |  |  |  |
| many-pages-600.pdf | tab switch: sharp | 7 |  |  |  |
| cad-set-12-sheets.pdf | tab switch: sharp | 3 |  |  |  |
| many-pages-600.pdf | search 600 pages while panning: first hit | 11 | 15.9 / 16.3 | 0.15 | 12.6 |
| many-pages-600.pdf | search 600 pages while panning: complete (600 hits) | 1871 |  |  |  |

## Resources at end of run

- viewerWorkingSetMB: 1158.53
- viewerPrivateMB: 1110.08
- workerProcesses: 2
- workersWorkingSetMB: 602.02
- tileCacheMB: 389.48
- tileCacheHitRate: 0.8
- vramUsageMB: 0.01
- vramBudgetMB: 7523.9
- tileRenderP50: 18.77
- tileRenderP95: 410.83
- tiles: 284
- queueWaitP50: 99.75
- queueWaitP95: 2063.87
- workerCrashes: 0
