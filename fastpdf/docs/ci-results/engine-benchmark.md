# EK FastPDF engine benchmark
Machine: Microsoft Windows 10.0.20348; 4 logical / 2 cores, 16 GB RAM; recommended workers 2
Viewport 2560x1440 device px. Times in ms (median of runs where noted).

## cad-dense-e1.pdf — 42x30 in sheet, ~90k path objects + 8k labels (Revit-like) (page 1)
  v1.1      fit       first     311 ms  sharp    1412 ms  (6 tiles)
  v1.1      zoom400   first     177 ms  sharp    2681 ms  (15 tiles)
  v1.1      pan       first     177 ms  sharp    2124 ms  (12 tiles)
  v1.1      zoom1600  first     165 ms  sharp    1305 ms  (8 tiles)
  pool w=1 t=512  fit       first      80 ms  sharp     490 ms  (12 tiles; overview 509 ms)
  pool w=1 t=512  zoom400   first       8 ms  sharp     165 ms  (24 tiles; overview 509 ms)
  pool w=1 t=512  pan       first       8 ms  sharp     152 ms  (24 tiles; overview 509 ms)
  pool w=1 t=512  zoom1600  first       3 ms  sharp      69 ms  (24 tiles; overview 509 ms)
  pool w=2 t=512  fit       first      88 ms  sharp     404 ms  (12 tiles; overview 497 ms)
  pool w=2 t=512  zoom400   first      10 ms  sharp     111 ms  (24 tiles; overview 497 ms)
  pool w=2 t=512  pan       first      10 ms  sharp     103 ms  (24 tiles; overview 497 ms)
  pool w=2 t=512  zoom1600  first       4 ms  sharp      39 ms  (24 tiles; overview 497 ms)
  pool w=3 t=512  fit       first     103 ms  sharp     370 ms  (12 tiles; overview 500 ms)
  pool w=3 t=512  zoom400   first      11 ms  sharp      82 ms  (24 tiles; overview 500 ms)
  pool w=3 t=512  pan       first      10 ms  sharp     104 ms  (24 tiles; overview 500 ms)
  pool w=3 t=512  zoom1600  first       5 ms  sharp      79 ms  (24 tiles; overview 500 ms)
  pool w=4 t=512  fit       first     125 ms  sharp     383 ms  (12 tiles; overview 505 ms)
  pool w=4 t=512  zoom400   first      10 ms  sharp      65 ms  (24 tiles; overview 505 ms)
  pool w=4 t=512  pan       first       9 ms  sharp    1003 ms  (24 tiles; overview 505 ms)
  pool w=4 t=512  zoom1600  first       4 ms  sharp      38 ms  (24 tiles; overview 505 ms)
  pool w=4 t=256  fit       first      48 ms  sharp     538 ms  (48 tiles; overview 511 ms)
  pool w=4 t=256  zoom400   first       5 ms  sharp     100 ms  (77 tiles; overview 511 ms)
  pool w=4 t=256  pan       first       5 ms  sharp      91 ms  (77 tiles; overview 511 ms)
  pool w=4 t=256  zoom1600  first       3 ms  sharp      55 ms  (66 tiles; overview 511 ms)
  pool w=4 t=1024 fit       first     134 ms  sharp     601 ms  (4 tiles; overview 511 ms)
  pool w=4 t=1024 zoom400   first      31 ms  sharp     105 ms  (12 tiles; overview 511 ms)
  pool w=4 t=1024 pan       first      27 ms  sharp      94 ms  (12 tiles; overview 511 ms)
  pool w=4 t=1024 zoom1600  first       9 ms  sharp      31 ms  (8 tiles; overview 511 ms)

## cad-extreme-e1.pdf — 42x30 in sheet, ~350k path objects + 33k labels (page 1)
  v1.1      fit       first    1172 ms  sharp    5646 ms  (6 tiles)
  v1.1      zoom400   first     682 ms  sharp   10256 ms  (15 tiles)
  v1.1      pan       first     664 ms  sharp    8095 ms  (12 tiles)
  v1.1      zoom1600  first     654 ms  sharp    5148 ms  (8 tiles)
  pool w=1 t=512  fit       first     288 ms  sharp    1759 ms  (12 tiles; overview 1840 ms)
  pool w=1 t=512  zoom400   first      24 ms  sharp     568 ms  (24 tiles; overview 1840 ms)
  pool w=1 t=512  pan       first      24 ms  sharp     549 ms  (24 tiles; overview 1840 ms)
  pool w=1 t=512  zoom1600  first      11 ms  sharp     257 ms  (24 tiles; overview 1840 ms)
  pool w=2 t=512  fit       first     304 ms  sharp    1336 ms  (12 tiles; overview 1844 ms)
  pool w=2 t=512  zoom400   first      26 ms  sharp     373 ms  (24 tiles; overview 1844 ms)
  pool w=2 t=512  pan       first      33 ms  sharp     375 ms  (24 tiles; overview 1844 ms)
  pool w=2 t=512  zoom1600  first      12 ms  sharp     182 ms  (24 tiles; overview 1844 ms)
  pool w=3 t=512  fit       first     397 ms  sharp    1320 ms  (12 tiles; overview 1869 ms)
  pool w=3 t=512  zoom400   first      30 ms  sharp     270 ms  (24 tiles; overview 1869 ms)
  pool w=3 t=512  pan       first      24 ms  sharp     261 ms  (24 tiles; overview 1869 ms)
  pool w=3 t=512  zoom1600  first      12 ms  sharp     122 ms  (24 tiles; overview 1869 ms)
  pool w=4 t=512  fit       first     445 ms  sharp    1466 ms  (12 tiles; overview 1848 ms)
  pool w=4 t=512  zoom400   first      25 ms  sharp     408 ms  (24 tiles; overview 1848 ms)
  pool w=4 t=512  pan       first      35 ms  sharp     220 ms  (24 tiles; overview 1848 ms)
  pool w=4 t=512  zoom1600  first      18 ms  sharp     116 ms  (24 tiles; overview 1848 ms)
  pool w=4 t=256  fit       first      94 ms  sharp    1871 ms  (48 tiles; overview 1872 ms)
  pool w=4 t=256  zoom400   first      17 ms  sharp     371 ms  (77 tiles; overview 1872 ms)
  pool w=4 t=256  pan       first      13 ms  sharp     369 ms  (77 tiles; overview 1872 ms)
  pool w=4 t=256  zoom1600  first      13 ms  sharp     229 ms  (66 tiles; overview 1872 ms)
  pool w=4 t=1024 fit       first     832 ms  sharp    1996 ms  (4 tiles; overview 2134 ms)
  pool w=4 t=1024 zoom400   first     111 ms  sharp     342 ms  (12 tiles; overview 2134 ms)
  pool w=4 t=1024 pan       first      99 ms  sharp     315 ms  (12 tiles; overview 2134 ms)
  pool w=4 t=1024 zoom1600  first      38 ms  sharp      80 ms  (8 tiles; overview 2134 ms)

## image-heavy.pdf — 8 pages, each a 4000x3000 RGB image (page 1)
  v1.1      fit       first      48 ms  sharp     242 ms  (6 tiles)
  v1.1      zoom400   first      35 ms  sharp     538 ms  (15 tiles)
  v1.1      pan       first      37 ms  sharp     526 ms  (15 tiles)
  v1.1      zoom1600  first      32 ms  sharp     493 ms  (15 tiles)
  pool w=1 t=512  fit       first       7 ms  sharp      68 ms  (12 tiles; overview 84 ms)
  pool w=1 t=512  zoom400   first       4 ms  sharp      77 ms  (24 tiles; overview 84 ms)
  pool w=1 t=512  pan       first       3 ms  sharp      77 ms  (24 tiles; overview 84 ms)
  pool w=1 t=512  zoom1600  first       1 ms  sharp      32 ms  (24 tiles; overview 84 ms)
  pool w=2 t=512  fit       first       9 ms  sharp      65 ms  (12 tiles; overview 83 ms)
  pool w=2 t=512  zoom400   first       3 ms  sharp      43 ms  (24 tiles; overview 83 ms)
  pool w=2 t=512  pan       first       3 ms  sharp      52 ms  (24 tiles; overview 83 ms)
  pool w=2 t=512  zoom1600  first       2 ms  sharp      17 ms  (24 tiles; overview 83 ms)
  pool w=3 t=512  fit       first       9 ms  sharp      61 ms  (12 tiles; overview 83 ms)
  pool w=3 t=512  zoom400   first       4 ms  sharp      35 ms  (24 tiles; overview 83 ms)
  pool w=3 t=512  pan       first       4 ms  sharp      35 ms  (24 tiles; overview 83 ms)
  pool w=3 t=512  zoom1600  first       2 ms  sharp      17 ms  (24 tiles; overview 83 ms)
  pool w=4 t=512  fit       first      10 ms  sharp      68 ms  (12 tiles; overview 86 ms)
  pool w=4 t=512  zoom400   first       5 ms  sharp      31 ms  (24 tiles; overview 86 ms)
  pool w=4 t=512  pan       first       5 ms  sharp      31 ms  (24 tiles; overview 86 ms)
  pool w=4 t=512  zoom1600  first       2 ms  sharp      33 ms  (24 tiles; overview 86 ms)
  pool w=4 t=256  fit       first       4 ms  sharp      96 ms  (48 tiles; overview 94 ms)
  pool w=4 t=256  zoom400   first       2 ms  sharp      29 ms  (66 tiles; overview 94 ms)
  pool w=4 t=256  pan       first       1 ms  sharp      30 ms  (66 tiles; overview 94 ms)
  pool w=4 t=256  zoom1600  first       1 ms  sharp      16 ms  (77 tiles; overview 94 ms)
  pool w=4 t=1024 fit       first      32 ms  sharp      98 ms  (4 tiles; overview 90 ms)
  pool w=4 t=1024 zoom400   first      21 ms  sharp      64 ms  (12 tiles; overview 90 ms)
  pool w=4 t=1024 pan       first      20 ms  sharp      48 ms  (9 tiles; overview 90 ms)
  pool w=4 t=1024 zoom1600  first       5 ms  sharp      44 ms  (12 tiles; overview 90 ms)

## many-pages-600.pdf — 600 pages, text + light vector content (page 301)
  v1.1      fit       first       5 ms  sharp      12 ms  (4 tiles)
  v1.1      zoom400   first       8 ms  sharp      67 ms  (12 tiles)
  v1.1      pan       first       7 ms  sharp      76 ms  (15 tiles)
  v1.1      zoom1600  first       3 ms  sharp      33 ms  (12 tiles)
  pool w=1 t=512  fit       first       1 ms  sharp      15 ms  (9 tiles; overview 22 ms)
  pool w=1 t=512  zoom400   first       5 ms  sharp      98 ms  (24 tiles; overview 22 ms)
  pool w=1 t=512  pan       first       5 ms  sharp      77 ms  (24 tiles; overview 22 ms)
  pool w=1 t=512  zoom1600  first       3 ms  sharp      48 ms  (24 tiles; overview 22 ms)
  pool w=2 t=512  fit       first       1 ms  sharp      16 ms  (9 tiles; overview 23 ms)
  pool w=2 t=512  zoom400   first       5 ms  sharp      52 ms  (24 tiles; overview 23 ms)
  pool w=2 t=512  pan       first       6 ms  sharp      45 ms  (24 tiles; overview 23 ms)
  pool w=2 t=512  zoom1600  first       3 ms  sharp      27 ms  (24 tiles; overview 23 ms)
  pool w=3 t=512  fit       first       1 ms  sharp      19 ms  (9 tiles; overview 22 ms)
  pool w=3 t=512  zoom400   first       5 ms  sharp      41 ms  (24 tiles; overview 22 ms)
  pool w=3 t=512  pan       first       5 ms  sharp      40 ms  (24 tiles; overview 22 ms)
  pool w=3 t=512  zoom1600  first       2 ms  sharp      25 ms  (24 tiles; overview 22 ms)
  pool w=4 t=512  fit       first       1 ms  sharp      18 ms  (9 tiles; overview 21 ms)
  pool w=4 t=512  zoom400   first       7 ms  sharp      41 ms  (24 tiles; overview 21 ms)
  pool w=4 t=512  pan       first       5 ms  sharp      33 ms  (24 tiles; overview 21 ms)
  pool w=4 t=512  zoom1600  first       2 ms  sharp      21 ms  (24 tiles; overview 21 ms)
  pool w=4 t=256  fit       first       1 ms  sharp      20 ms  (30 tiles; overview 22 ms)
  pool w=4 t=256  zoom400   first       3 ms  sharp      54 ms  (77 tiles; overview 22 ms)
  pool w=4 t=256  pan       first       4 ms  sharp      57 ms  (77 tiles; overview 22 ms)
  pool w=4 t=256  zoom1600  first       2 ms  sharp      40 ms  (77 tiles; overview 22 ms)
  pool w=4 t=1024 fit       first       1 ms  sharp      22 ms  (4 tiles; overview 21 ms)
  pool w=4 t=1024 zoom400   first      12 ms  sharp      28 ms  (6 tiles; overview 21 ms)
  pool w=4 t=1024 pan       first      11 ms  sharp      25 ms  (8 tiles; overview 21 ms)
  pool w=4 t=1024 zoom1600  first       4 ms  sharp      28 ms  (8 tiles; overview 21 ms)

| Document | Engine | Workers | Tile | View | First tile | Viewport sharp | Tiles |
|---|---|---:|---:|---|---:|---:|---:|
| cad-dense-e1.pdf | v1.1 | 1 | 768 | fit | 311 | 1412 | 6 |
| cad-dense-e1.pdf | v1.1 | 1 | 768 | zoom400 | 177 | 2681 | 15 |
| cad-dense-e1.pdf | v1.1 | 1 | 768 | pan | 177 | 2124 | 12 |
| cad-dense-e1.pdf | v1.1 | 1 | 768 | zoom1600 | 165 | 1305 | 8 |
| cad-dense-e1.pdf | v1.2 pool | 1 | 512 | fit | 80 | 490 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 1 | 512 | zoom400 | 8 | 165 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 1 | 512 | pan | 8 | 152 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 1 | 512 | zoom1600 | 3 | 69 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 2 | 512 | fit | 88 | 404 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 2 | 512 | zoom400 | 10 | 111 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 2 | 512 | pan | 10 | 103 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 2 | 512 | zoom1600 | 4 | 39 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 3 | 512 | fit | 103 | 370 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 3 | 512 | zoom400 | 11 | 82 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 3 | 512 | pan | 10 | 104 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 3 | 512 | zoom1600 | 5 | 79 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 512 | fit | 125 | 383 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 512 | zoom400 | 10 | 65 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 512 | pan | 9 | 1003 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 512 | zoom1600 | 4 | 38 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 256 | fit | 48 | 538 | 48 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 256 | zoom400 | 5 | 100 | 77 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 256 | pan | 5 | 91 | 77 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 256 | zoom1600 | 3 | 55 | 66 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 1024 | fit | 134 | 601 | 4 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 1024 | zoom400 | 31 | 105 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 1024 | pan | 27 | 94 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 1024 | zoom1600 | 9 | 31 | 8 |
| cad-extreme-e1.pdf | v1.1 | 1 | 768 | fit | 1172 | 5646 | 6 |
| cad-extreme-e1.pdf | v1.1 | 1 | 768 | zoom400 | 682 | 10256 | 15 |
| cad-extreme-e1.pdf | v1.1 | 1 | 768 | pan | 664 | 8095 | 12 |
| cad-extreme-e1.pdf | v1.1 | 1 | 768 | zoom1600 | 654 | 5148 | 8 |
| cad-extreme-e1.pdf | v1.2 pool | 1 | 512 | fit | 288 | 1759 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 1 | 512 | zoom400 | 24 | 568 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 1 | 512 | pan | 24 | 549 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 1 | 512 | zoom1600 | 11 | 257 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 2 | 512 | fit | 304 | 1336 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 2 | 512 | zoom400 | 26 | 373 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 2 | 512 | pan | 33 | 375 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 2 | 512 | zoom1600 | 12 | 182 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 3 | 512 | fit | 397 | 1320 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 3 | 512 | zoom400 | 30 | 270 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 3 | 512 | pan | 24 | 261 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 3 | 512 | zoom1600 | 12 | 122 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 512 | fit | 445 | 1466 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 512 | zoom400 | 25 | 408 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 512 | pan | 35 | 220 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 512 | zoom1600 | 18 | 116 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 256 | fit | 94 | 1871 | 48 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 256 | zoom400 | 17 | 371 | 77 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 256 | pan | 13 | 369 | 77 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 256 | zoom1600 | 13 | 229 | 66 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 1024 | fit | 832 | 1996 | 4 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 1024 | zoom400 | 111 | 342 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 1024 | pan | 99 | 315 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 1024 | zoom1600 | 38 | 80 | 8 |
| image-heavy.pdf | v1.1 | 1 | 768 | fit | 48 | 242 | 6 |
| image-heavy.pdf | v1.1 | 1 | 768 | zoom400 | 35 | 538 | 15 |
| image-heavy.pdf | v1.1 | 1 | 768 | pan | 37 | 526 | 15 |
| image-heavy.pdf | v1.1 | 1 | 768 | zoom1600 | 32 | 493 | 15 |
| image-heavy.pdf | v1.2 pool | 1 | 512 | fit | 7 | 68 | 12 |
| image-heavy.pdf | v1.2 pool | 1 | 512 | zoom400 | 4 | 77 | 24 |
| image-heavy.pdf | v1.2 pool | 1 | 512 | pan | 3 | 77 | 24 |
| image-heavy.pdf | v1.2 pool | 1 | 512 | zoom1600 | 1 | 32 | 24 |
| image-heavy.pdf | v1.2 pool | 2 | 512 | fit | 9 | 65 | 12 |
| image-heavy.pdf | v1.2 pool | 2 | 512 | zoom400 | 3 | 43 | 24 |
| image-heavy.pdf | v1.2 pool | 2 | 512 | pan | 3 | 52 | 24 |
| image-heavy.pdf | v1.2 pool | 2 | 512 | zoom1600 | 2 | 17 | 24 |
| image-heavy.pdf | v1.2 pool | 3 | 512 | fit | 9 | 61 | 12 |
| image-heavy.pdf | v1.2 pool | 3 | 512 | zoom400 | 4 | 35 | 24 |
| image-heavy.pdf | v1.2 pool | 3 | 512 | pan | 4 | 35 | 24 |
| image-heavy.pdf | v1.2 pool | 3 | 512 | zoom1600 | 2 | 17 | 24 |
| image-heavy.pdf | v1.2 pool | 4 | 512 | fit | 10 | 68 | 12 |
| image-heavy.pdf | v1.2 pool | 4 | 512 | zoom400 | 5 | 31 | 24 |
| image-heavy.pdf | v1.2 pool | 4 | 512 | pan | 5 | 31 | 24 |
| image-heavy.pdf | v1.2 pool | 4 | 512 | zoom1600 | 2 | 33 | 24 |
| image-heavy.pdf | v1.2 pool | 4 | 256 | fit | 4 | 96 | 48 |
| image-heavy.pdf | v1.2 pool | 4 | 256 | zoom400 | 2 | 29 | 66 |
| image-heavy.pdf | v1.2 pool | 4 | 256 | pan | 1 | 30 | 66 |
| image-heavy.pdf | v1.2 pool | 4 | 256 | zoom1600 | 1 | 16 | 77 |
| image-heavy.pdf | v1.2 pool | 4 | 1024 | fit | 32 | 98 | 4 |
| image-heavy.pdf | v1.2 pool | 4 | 1024 | zoom400 | 21 | 64 | 12 |
| image-heavy.pdf | v1.2 pool | 4 | 1024 | pan | 20 | 48 | 9 |
| image-heavy.pdf | v1.2 pool | 4 | 1024 | zoom1600 | 5 | 44 | 12 |
| many-pages-600.pdf | v1.1 | 1 | 768 | fit | 5 | 12 | 4 |
| many-pages-600.pdf | v1.1 | 1 | 768 | zoom400 | 8 | 67 | 12 |
| many-pages-600.pdf | v1.1 | 1 | 768 | pan | 7 | 76 | 15 |
| many-pages-600.pdf | v1.1 | 1 | 768 | zoom1600 | 3 | 33 | 12 |
| many-pages-600.pdf | v1.2 pool | 1 | 512 | fit | 1 | 15 | 9 |
| many-pages-600.pdf | v1.2 pool | 1 | 512 | zoom400 | 5 | 98 | 24 |
| many-pages-600.pdf | v1.2 pool | 1 | 512 | pan | 5 | 77 | 24 |
| many-pages-600.pdf | v1.2 pool | 1 | 512 | zoom1600 | 3 | 48 | 24 |
| many-pages-600.pdf | v1.2 pool | 2 | 512 | fit | 1 | 16 | 9 |
| many-pages-600.pdf | v1.2 pool | 2 | 512 | zoom400 | 5 | 52 | 24 |
| many-pages-600.pdf | v1.2 pool | 2 | 512 | pan | 6 | 45 | 24 |
| many-pages-600.pdf | v1.2 pool | 2 | 512 | zoom1600 | 3 | 27 | 24 |
| many-pages-600.pdf | v1.2 pool | 3 | 512 | fit | 1 | 19 | 9 |
| many-pages-600.pdf | v1.2 pool | 3 | 512 | zoom400 | 5 | 41 | 24 |
| many-pages-600.pdf | v1.2 pool | 3 | 512 | pan | 5 | 40 | 24 |
| many-pages-600.pdf | v1.2 pool | 3 | 512 | zoom1600 | 2 | 25 | 24 |
| many-pages-600.pdf | v1.2 pool | 4 | 512 | fit | 1 | 18 | 9 |
| many-pages-600.pdf | v1.2 pool | 4 | 512 | zoom400 | 7 | 41 | 24 |
| many-pages-600.pdf | v1.2 pool | 4 | 512 | pan | 5 | 33 | 24 |
| many-pages-600.pdf | v1.2 pool | 4 | 512 | zoom1600 | 2 | 21 | 24 |
| many-pages-600.pdf | v1.2 pool | 4 | 256 | fit | 1 | 20 | 30 |
| many-pages-600.pdf | v1.2 pool | 4 | 256 | zoom400 | 3 | 54 | 77 |
| many-pages-600.pdf | v1.2 pool | 4 | 256 | pan | 4 | 57 | 77 |
| many-pages-600.pdf | v1.2 pool | 4 | 256 | zoom1600 | 2 | 40 | 77 |
| many-pages-600.pdf | v1.2 pool | 4 | 1024 | fit | 1 | 22 | 4 |
| many-pages-600.pdf | v1.2 pool | 4 | 1024 | zoom400 | 12 | 28 | 6 |
| many-pages-600.pdf | v1.2 pool | 4 | 1024 | pan | 11 | 25 | 8 |
| many-pages-600.pdf | v1.2 pool | 4 | 1024 | zoom1600 | 4 | 28 | 8 |
