# EK FastPDF engine benchmark
Machine: Microsoft Windows 10.0.20348; 4 logical / 2 cores, 16 GB RAM; recommended workers 2
Viewport 2560x1440 device px. Times in ms (median of runs where noted).

## cad-dense-e1.pdf — 42x30 in sheet, ~90k path objects + 8k labels (Revit-like) (page 1)
  v1.1      fit       first     330 ms  sharp    1461 ms  (6 tiles)
  v1.1      zoom400   first     187 ms  sharp    2714 ms  (15 tiles)
  v1.1      pan       first     181 ms  sharp    2168 ms  (12 tiles)
  v1.1      zoom1600  first     167 ms  sharp    1304 ms  (8 tiles)
  pool w=1 t=512  fit       first      80 ms  sharp     497 ms  (12 tiles; overview 516 ms)
  pool w=1 t=512  zoom400   first       8 ms  sharp     169 ms  (24 tiles; overview 516 ms)
  pool w=1 t=512  pan       first       8 ms  sharp     157 ms  (24 tiles; overview 516 ms)
  pool w=1 t=512  zoom1600  first       4 ms  sharp      70 ms  (24 tiles; overview 516 ms)
  pool w=2 t=512  fit       first      84 ms  sharp     398 ms  (12 tiles; overview 500 ms)
  pool w=2 t=512  zoom400   first      11 ms  sharp     117 ms  (24 tiles; overview 500 ms)
  pool w=2 t=512  pan       first       8 ms  sharp     109 ms  (24 tiles; overview 500 ms)
  pool w=2 t=512  zoom1600  first       4 ms  sharp      45 ms  (24 tiles; overview 500 ms)
  pool w=3 t=512  fit       first     122 ms  sharp     351 ms  (12 tiles; overview 512 ms)
  pool w=3 t=512  zoom400   first      10 ms  sharp      78 ms  (24 tiles; overview 512 ms)
  pool w=3 t=512  pan       first       9 ms  sharp     170 ms  (24 tiles; overview 512 ms)
  pool w=3 t=512  zoom1600  first       6 ms  sharp      81 ms  (24 tiles; overview 512 ms)
  pool w=4 t=512  fit       first     126 ms  sharp     389 ms  (12 tiles; overview 519 ms)
  pool w=4 t=512  zoom400   first      11 ms  sharp     612 ms  (24 tiles; overview 519 ms)
  pool w=4 t=512  pan       first       9 ms  sharp     138 ms  (24 tiles; overview 519 ms)
  pool w=4 t=512  zoom1600  first       5 ms  sharp      34 ms  (24 tiles; overview 519 ms)
  pool w=4 t=256  fit       first      48 ms  sharp     612 ms  (48 tiles; overview 512 ms)
  pool w=4 t=256  zoom400   first       5 ms  sharp      97 ms  (77 tiles; overview 512 ms)
  pool w=4 t=256  pan       first       5 ms  sharp     118 ms  (77 tiles; overview 512 ms)
  pool w=4 t=256  zoom1600  first       4 ms  sharp     111 ms  (66 tiles; overview 512 ms)
  pool w=4 t=1024 fit       first     208 ms  sharp     489 ms  (4 tiles; overview 519 ms)
  pool w=4 t=1024 zoom400   first      32 ms  sharp     114 ms  (12 tiles; overview 519 ms)
  pool w=4 t=1024 pan       first      31 ms  sharp     102 ms  (12 tiles; overview 519 ms)
  pool w=4 t=1024 zoom1600  first       8 ms  sharp      55 ms  (8 tiles; overview 519 ms)

## cad-extreme-e1.pdf — 42x30 in sheet, ~350k path objects + 33k labels (page 1)
  v1.1      fit       first    1150 ms  sharp    5696 ms  (6 tiles)
  v1.1      zoom400   first     689 ms  sharp   10281 ms  (15 tiles)
  v1.1      pan       first     694 ms  sharp    8164 ms  (12 tiles)
  v1.1      zoom1600  first     648 ms  sharp    5186 ms  (8 tiles)
  pool w=1 t=512  fit       first     291 ms  sharp    1776 ms  (12 tiles; overview 1879 ms)
  pool w=1 t=512  zoom400   first      25 ms  sharp     590 ms  (24 tiles; overview 1879 ms)
  pool w=1 t=512  pan       first      25 ms  sharp     574 ms  (24 tiles; overview 1879 ms)
  pool w=1 t=512  zoom1600  first      12 ms  sharp     287 ms  (24 tiles; overview 1879 ms)
  pool w=2 t=512  fit       first     293 ms  sharp    1495 ms  (12 tiles; overview 1863 ms)
  pool w=2 t=512  zoom400   first      25 ms  sharp     377 ms  (24 tiles; overview 1863 ms)
  pool w=2 t=512  pan       first      35 ms  sharp     360 ms  (24 tiles; overview 1863 ms)
  pool w=2 t=512  zoom1600  first      14 ms  sharp     175 ms  (24 tiles; overview 1863 ms)
  pool w=3 t=512  fit       first     357 ms  sharp    1378 ms  (12 tiles; overview 1929 ms)
  pool w=3 t=512  zoom400   first      28 ms  sharp     281 ms  (24 tiles; overview 1929 ms)
  pool w=3 t=512  pan       first      26 ms  sharp     282 ms  (24 tiles; overview 1929 ms)
  pool w=3 t=512  zoom1600  first      14 ms  sharp     131 ms  (24 tiles; overview 1929 ms)
  pool w=4 t=512  fit       first     314 ms  sharp    1267 ms  (12 tiles; overview 1906 ms)
  pool w=4 t=512  zoom400   first      38 ms  sharp     795 ms  (24 tiles; overview 1906 ms)
  pool w=4 t=512  pan       first      35 ms  sharp     214 ms  (24 tiles; overview 1906 ms)
  pool w=4 t=512  zoom1600  first      17 ms  sharp     143 ms  (24 tiles; overview 1906 ms)
  pool w=4 t=256  fit       first     140 ms  sharp    1662 ms  (48 tiles; overview 1857 ms)
  pool w=4 t=256  zoom400   first      18 ms  sharp     451 ms  (77 tiles; overview 1857 ms)
  pool w=4 t=256  pan       first      17 ms  sharp     336 ms  (77 tiles; overview 1857 ms)
  pool w=4 t=256  zoom1600  first      12 ms  sharp     236 ms  (66 tiles; overview 1857 ms)
  pool w=4 t=1024 fit       first     739 ms  sharp    1799 ms  (4 tiles; overview 1909 ms)
  pool w=4 t=1024 zoom400   first     102 ms  sharp     418 ms  (12 tiles; overview 1909 ms)
  pool w=4 t=1024 pan       first      77 ms  sharp     410 ms  (12 tiles; overview 1909 ms)
  pool w=4 t=1024 zoom1600  first      37 ms  sharp      82 ms  (8 tiles; overview 1909 ms)

## image-heavy.pdf — 8 pages, each a 4000x3000 RGB image (page 1)
  v1.1      fit       first      51 ms  sharp     247 ms  (6 tiles)
  v1.1      zoom400   first      36 ms  sharp     529 ms  (15 tiles)
  v1.1      pan       first      34 ms  sharp     518 ms  (15 tiles)
  v1.1      zoom1600  first      33 ms  sharp     490 ms  (15 tiles)
  pool w=1 t=512  fit       first       7 ms  sharp      68 ms  (12 tiles; overview 85 ms)
  pool w=1 t=512  zoom400   first       3 ms  sharp      84 ms  (24 tiles; overview 85 ms)
  pool w=1 t=512  pan       first       3 ms  sharp      81 ms  (24 tiles; overview 85 ms)
  pool w=1 t=512  zoom1600  first       1 ms  sharp      33 ms  (24 tiles; overview 85 ms)
  pool w=2 t=512  fit       first       8 ms  sharp      66 ms  (12 tiles; overview 82 ms)
  pool w=2 t=512  zoom400   first       3 ms  sharp      47 ms  (24 tiles; overview 82 ms)
  pool w=2 t=512  pan       first       3 ms  sharp      43 ms  (24 tiles; overview 82 ms)
  pool w=2 t=512  zoom1600  first       2 ms  sharp      17 ms  (24 tiles; overview 82 ms)
  pool w=3 t=512  fit       first      10 ms  sharp      65 ms  (12 tiles; overview 83 ms)
  pool w=3 t=512  zoom400   first       3 ms  sharp      35 ms  (24 tiles; overview 83 ms)
  pool w=3 t=512  pan       first       4 ms  sharp      34 ms  (24 tiles; overview 83 ms)
  pool w=3 t=512  zoom1600  first       2 ms  sharp      17 ms  (24 tiles; overview 83 ms)
  pool w=4 t=512  fit       first      10 ms  sharp      64 ms  (12 tiles; overview 85 ms)
  pool w=4 t=512  zoom400   first       6 ms  sharp      31 ms  (24 tiles; overview 85 ms)
  pool w=4 t=512  pan       first       4 ms  sharp      29 ms  (24 tiles; overview 85 ms)
  pool w=4 t=512  zoom1600  first       2 ms  sharp      11 ms  (24 tiles; overview 85 ms)
  pool w=4 t=256  fit       first       3 ms  sharp      93 ms  (48 tiles; overview 81 ms)
  pool w=4 t=256  zoom400   first       1 ms  sharp      27 ms  (66 tiles; overview 81 ms)
  pool w=4 t=256  pan       first       1 ms  sharp      27 ms  (66 tiles; overview 81 ms)
  pool w=4 t=256  zoom1600  first       1 ms  sharp      15 ms  (77 tiles; overview 81 ms)
  pool w=4 t=1024 fit       first      21 ms  sharp      52 ms  (4 tiles; overview 83 ms)
  pool w=4 t=1024 zoom400   first      10 ms  sharp      70 ms  (12 tiles; overview 83 ms)
  pool w=4 t=1024 pan       first      11 ms  sharp      86 ms  (9 tiles; overview 83 ms)
  pool w=4 t=1024 zoom1600  first       7 ms  sharp      24 ms  (12 tiles; overview 83 ms)

## many-pages-600.pdf — 600 pages, text + light vector content (page 301)
  v1.1      fit       first       4 ms  sharp       9 ms  (4 tiles)
  v1.1      zoom400   first       5 ms  sharp      57 ms  (12 tiles)
  v1.1      pan       first       5 ms  sharp      58 ms  (15 tiles)
  v1.1      zoom1600  first       3 ms  sharp      25 ms  (12 tiles)
  pool w=1 t=512  fit       first       1 ms  sharp       9 ms  (9 tiles; overview 20 ms)
  pool w=1 t=512  zoom400   first       4 ms  sharp      79 ms  (24 tiles; overview 20 ms)
  pool w=1 t=512  pan       first       4 ms  sharp      68 ms  (24 tiles; overview 20 ms)
  pool w=1 t=512  zoom1600  first       2 ms  sharp      44 ms  (24 tiles; overview 20 ms)
  pool w=2 t=512  fit       first       1 ms  sharp      16 ms  (9 tiles; overview 19 ms)
  pool w=2 t=512  zoom400   first       6 ms  sharp      48 ms  (24 tiles; overview 19 ms)
  pool w=2 t=512  pan       first       5 ms  sharp      46 ms  (24 tiles; overview 19 ms)
  pool w=2 t=512  zoom1600  first       3 ms  sharp      26 ms  (24 tiles; overview 19 ms)
  pool w=3 t=512  fit       first       1 ms  sharp      16 ms  (9 tiles; overview 19 ms)
  pool w=3 t=512  zoom400   first       4 ms  sharp      39 ms  (24 tiles; overview 19 ms)
  pool w=3 t=512  pan       first       6 ms  sharp      35 ms  (24 tiles; overview 19 ms)
  pool w=3 t=512  zoom1600  first       2 ms  sharp      20 ms  (24 tiles; overview 19 ms)
  pool w=4 t=512  fit       first       1 ms  sharp      18 ms  (9 tiles; overview 20 ms)
  pool w=4 t=512  zoom400   first       7 ms  sharp      33 ms  (24 tiles; overview 20 ms)
  pool w=4 t=512  pan       first       5 ms  sharp      27 ms  (24 tiles; overview 20 ms)
  pool w=4 t=512  zoom1600  first       2 ms  sharp      18 ms  (24 tiles; overview 20 ms)
  pool w=4 t=256  fit       first       1 ms  sharp      20 ms  (30 tiles; overview 17 ms)
  pool w=4 t=256  zoom400   first       3 ms  sharp      53 ms  (77 tiles; overview 17 ms)
  pool w=4 t=256  pan       first       2 ms  sharp      49 ms  (77 tiles; overview 17 ms)
  pool w=4 t=256  zoom1600  first       1 ms  sharp      31 ms  (77 tiles; overview 17 ms)
  pool w=4 t=1024 fit       first       1 ms  sharp      24 ms  (4 tiles; overview 20 ms)
  pool w=4 t=1024 zoom400   first      14 ms  sharp      26 ms  (6 tiles; overview 20 ms)
  pool w=4 t=1024 pan       first      11 ms  sharp      22 ms  (8 tiles; overview 20 ms)
  pool w=4 t=1024 zoom1600  first       7 ms  sharp      15 ms  (8 tiles; overview 20 ms)

| Document | Engine | Workers | Tile | View | First tile | Viewport sharp | Tiles |
|---|---|---:|---:|---|---:|---:|---:|
| cad-dense-e1.pdf | v1.1 | 1 | 768 | fit | 330 | 1461 | 6 |
| cad-dense-e1.pdf | v1.1 | 1 | 768 | zoom400 | 187 | 2714 | 15 |
| cad-dense-e1.pdf | v1.1 | 1 | 768 | pan | 181 | 2168 | 12 |
| cad-dense-e1.pdf | v1.1 | 1 | 768 | zoom1600 | 167 | 1304 | 8 |
| cad-dense-e1.pdf | v1.2 pool | 1 | 512 | fit | 80 | 497 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 1 | 512 | zoom400 | 8 | 169 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 1 | 512 | pan | 8 | 157 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 1 | 512 | zoom1600 | 4 | 70 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 2 | 512 | fit | 84 | 398 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 2 | 512 | zoom400 | 11 | 117 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 2 | 512 | pan | 8 | 109 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 2 | 512 | zoom1600 | 4 | 45 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 3 | 512 | fit | 122 | 351 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 3 | 512 | zoom400 | 10 | 78 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 3 | 512 | pan | 9 | 170 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 3 | 512 | zoom1600 | 6 | 81 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 512 | fit | 126 | 389 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 512 | zoom400 | 11 | 612 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 512 | pan | 9 | 138 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 512 | zoom1600 | 5 | 34 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 256 | fit | 48 | 612 | 48 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 256 | zoom400 | 5 | 97 | 77 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 256 | pan | 5 | 118 | 77 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 256 | zoom1600 | 4 | 111 | 66 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 1024 | fit | 208 | 489 | 4 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 1024 | zoom400 | 32 | 114 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 1024 | pan | 31 | 102 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 1024 | zoom1600 | 8 | 55 | 8 |
| cad-extreme-e1.pdf | v1.1 | 1 | 768 | fit | 1150 | 5696 | 6 |
| cad-extreme-e1.pdf | v1.1 | 1 | 768 | zoom400 | 689 | 10281 | 15 |
| cad-extreme-e1.pdf | v1.1 | 1 | 768 | pan | 694 | 8164 | 12 |
| cad-extreme-e1.pdf | v1.1 | 1 | 768 | zoom1600 | 648 | 5186 | 8 |
| cad-extreme-e1.pdf | v1.2 pool | 1 | 512 | fit | 291 | 1776 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 1 | 512 | zoom400 | 25 | 590 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 1 | 512 | pan | 25 | 574 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 1 | 512 | zoom1600 | 12 | 287 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 2 | 512 | fit | 293 | 1495 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 2 | 512 | zoom400 | 25 | 377 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 2 | 512 | pan | 35 | 360 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 2 | 512 | zoom1600 | 14 | 175 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 3 | 512 | fit | 357 | 1378 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 3 | 512 | zoom400 | 28 | 281 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 3 | 512 | pan | 26 | 282 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 3 | 512 | zoom1600 | 14 | 131 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 512 | fit | 314 | 1267 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 512 | zoom400 | 38 | 795 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 512 | pan | 35 | 214 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 512 | zoom1600 | 17 | 143 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 256 | fit | 140 | 1662 | 48 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 256 | zoom400 | 18 | 451 | 77 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 256 | pan | 17 | 336 | 77 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 256 | zoom1600 | 12 | 236 | 66 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 1024 | fit | 739 | 1799 | 4 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 1024 | zoom400 | 102 | 418 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 1024 | pan | 77 | 410 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 1024 | zoom1600 | 37 | 82 | 8 |
| image-heavy.pdf | v1.1 | 1 | 768 | fit | 51 | 247 | 6 |
| image-heavy.pdf | v1.1 | 1 | 768 | zoom400 | 36 | 529 | 15 |
| image-heavy.pdf | v1.1 | 1 | 768 | pan | 34 | 518 | 15 |
| image-heavy.pdf | v1.1 | 1 | 768 | zoom1600 | 33 | 490 | 15 |
| image-heavy.pdf | v1.2 pool | 1 | 512 | fit | 7 | 68 | 12 |
| image-heavy.pdf | v1.2 pool | 1 | 512 | zoom400 | 3 | 84 | 24 |
| image-heavy.pdf | v1.2 pool | 1 | 512 | pan | 3 | 81 | 24 |
| image-heavy.pdf | v1.2 pool | 1 | 512 | zoom1600 | 1 | 33 | 24 |
| image-heavy.pdf | v1.2 pool | 2 | 512 | fit | 8 | 66 | 12 |
| image-heavy.pdf | v1.2 pool | 2 | 512 | zoom400 | 3 | 47 | 24 |
| image-heavy.pdf | v1.2 pool | 2 | 512 | pan | 3 | 43 | 24 |
| image-heavy.pdf | v1.2 pool | 2 | 512 | zoom1600 | 2 | 17 | 24 |
| image-heavy.pdf | v1.2 pool | 3 | 512 | fit | 10 | 65 | 12 |
| image-heavy.pdf | v1.2 pool | 3 | 512 | zoom400 | 3 | 35 | 24 |
| image-heavy.pdf | v1.2 pool | 3 | 512 | pan | 4 | 34 | 24 |
| image-heavy.pdf | v1.2 pool | 3 | 512 | zoom1600 | 2 | 17 | 24 |
| image-heavy.pdf | v1.2 pool | 4 | 512 | fit | 10 | 64 | 12 |
| image-heavy.pdf | v1.2 pool | 4 | 512 | zoom400 | 6 | 31 | 24 |
| image-heavy.pdf | v1.2 pool | 4 | 512 | pan | 4 | 29 | 24 |
| image-heavy.pdf | v1.2 pool | 4 | 512 | zoom1600 | 2 | 11 | 24 |
| image-heavy.pdf | v1.2 pool | 4 | 256 | fit | 3 | 93 | 48 |
| image-heavy.pdf | v1.2 pool | 4 | 256 | zoom400 | 1 | 27 | 66 |
| image-heavy.pdf | v1.2 pool | 4 | 256 | pan | 1 | 27 | 66 |
| image-heavy.pdf | v1.2 pool | 4 | 256 | zoom1600 | 1 | 15 | 77 |
| image-heavy.pdf | v1.2 pool | 4 | 1024 | fit | 21 | 52 | 4 |
| image-heavy.pdf | v1.2 pool | 4 | 1024 | zoom400 | 10 | 70 | 12 |
| image-heavy.pdf | v1.2 pool | 4 | 1024 | pan | 11 | 86 | 9 |
| image-heavy.pdf | v1.2 pool | 4 | 1024 | zoom1600 | 7 | 24 | 12 |
| many-pages-600.pdf | v1.1 | 1 | 768 | fit | 4 | 9 | 4 |
| many-pages-600.pdf | v1.1 | 1 | 768 | zoom400 | 5 | 57 | 12 |
| many-pages-600.pdf | v1.1 | 1 | 768 | pan | 5 | 58 | 15 |
| many-pages-600.pdf | v1.1 | 1 | 768 | zoom1600 | 3 | 25 | 12 |
| many-pages-600.pdf | v1.2 pool | 1 | 512 | fit | 1 | 9 | 9 |
| many-pages-600.pdf | v1.2 pool | 1 | 512 | zoom400 | 4 | 79 | 24 |
| many-pages-600.pdf | v1.2 pool | 1 | 512 | pan | 4 | 68 | 24 |
| many-pages-600.pdf | v1.2 pool | 1 | 512 | zoom1600 | 2 | 44 | 24 |
| many-pages-600.pdf | v1.2 pool | 2 | 512 | fit | 1 | 16 | 9 |
| many-pages-600.pdf | v1.2 pool | 2 | 512 | zoom400 | 6 | 48 | 24 |
| many-pages-600.pdf | v1.2 pool | 2 | 512 | pan | 5 | 46 | 24 |
| many-pages-600.pdf | v1.2 pool | 2 | 512 | zoom1600 | 3 | 26 | 24 |
| many-pages-600.pdf | v1.2 pool | 3 | 512 | fit | 1 | 16 | 9 |
| many-pages-600.pdf | v1.2 pool | 3 | 512 | zoom400 | 4 | 39 | 24 |
| many-pages-600.pdf | v1.2 pool | 3 | 512 | pan | 6 | 35 | 24 |
| many-pages-600.pdf | v1.2 pool | 3 | 512 | zoom1600 | 2 | 20 | 24 |
| many-pages-600.pdf | v1.2 pool | 4 | 512 | fit | 1 | 18 | 9 |
| many-pages-600.pdf | v1.2 pool | 4 | 512 | zoom400 | 7 | 33 | 24 |
| many-pages-600.pdf | v1.2 pool | 4 | 512 | pan | 5 | 27 | 24 |
| many-pages-600.pdf | v1.2 pool | 4 | 512 | zoom1600 | 2 | 18 | 24 |
| many-pages-600.pdf | v1.2 pool | 4 | 256 | fit | 1 | 20 | 30 |
| many-pages-600.pdf | v1.2 pool | 4 | 256 | zoom400 | 3 | 53 | 77 |
| many-pages-600.pdf | v1.2 pool | 4 | 256 | pan | 2 | 49 | 77 |
| many-pages-600.pdf | v1.2 pool | 4 | 256 | zoom1600 | 1 | 31 | 77 |
| many-pages-600.pdf | v1.2 pool | 4 | 1024 | fit | 1 | 24 | 4 |
| many-pages-600.pdf | v1.2 pool | 4 | 1024 | zoom400 | 14 | 26 | 6 |
| many-pages-600.pdf | v1.2 pool | 4 | 1024 | pan | 11 | 22 | 8 |
| many-pages-600.pdf | v1.2 pool | 4 | 1024 | zoom1600 | 7 | 15 | 8 |
