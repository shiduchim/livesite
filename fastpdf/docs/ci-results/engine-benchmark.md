# EK FastPDF engine benchmark
Machine: Microsoft Windows 10.0.20348; 4 logical / 2 cores, 16 GB RAM; recommended workers 2
Viewport 2560x1440 device px. Times in ms (median of runs where noted).

## cad-dense-e1.pdf — 42x30 in sheet, ~90k path objects + 8k labels (Revit-like) (page 1)
  v1.1      fit       first     416 ms  sharp    1897 ms  (6 tiles)
  v1.1      zoom400   first     245 ms  sharp    3557 ms  (15 tiles)
  v1.1      pan       first     237 ms  sharp    2829 ms  (12 tiles)
  v1.1      zoom1600  first     225 ms  sharp    1745 ms  (8 tiles)
  pool w=1 t=512  fit       first     101 ms  sharp     612 ms  (12 tiles; overview 650 ms)
  pool w=1 t=512  zoom400   first      15 ms  sharp     231 ms  (24 tiles; overview 650 ms)
  pool w=1 t=512  pan       first      15 ms  sharp     211 ms  (24 tiles; overview 650 ms)
  pool w=1 t=512  zoom1600  first       4 ms  sharp      98 ms  (24 tiles; overview 650 ms)
  pool w=2 t=512  fit       first     107 ms  sharp     531 ms  (12 tiles; overview 650 ms)
  pool w=2 t=512  zoom400   first      11 ms  sharp     131 ms  (24 tiles; overview 650 ms)
  pool w=2 t=512  pan       first      10 ms  sharp     115 ms  (24 tiles; overview 650 ms)
  pool w=2 t=512  zoom1600  first       4 ms  sharp      50 ms  (24 tiles; overview 650 ms)
  pool w=3 t=512  fit       first      19 ms  sharp     478 ms  (12 tiles; overview 665 ms)
  pool w=3 t=512  zoom400   first      13 ms  sharp      99 ms  (24 tiles; overview 665 ms)
  pool w=3 t=512  pan       first      10 ms  sharp     203 ms  (24 tiles; overview 665 ms)
  pool w=3 t=512  zoom1600  first       7 ms  sharp     114 ms  (24 tiles; overview 665 ms)
  pool w=4 t=512  fit       first     164 ms  sharp     547 ms  (12 tiles; overview 666 ms)
  pool w=4 t=512  zoom400   first      13 ms  sharp      85 ms  (24 tiles; overview 666 ms)
  pool w=4 t=512  pan       first     891 ms  sharp    1089 ms  (24 tiles; overview 666 ms)
  pool w=4 t=512  zoom1600  first       4 ms  sharp      62 ms  (24 tiles; overview 666 ms)
  pool w=4 t=256  fit       first      50 ms  sharp     730 ms  (48 tiles; overview 657 ms)
  pool w=4 t=256  zoom400   first       7 ms  sharp     118 ms  (77 tiles; overview 657 ms)
  pool w=4 t=256  pan       first       7 ms  sharp     112 ms  (77 tiles; overview 657 ms)
  pool w=4 t=256  zoom1600  first       3 ms  sharp     130 ms  (66 tiles; overview 657 ms)
  pool w=4 t=1024 fit       first     267 ms  sharp     649 ms  (4 tiles; overview 650 ms)
  pool w=4 t=1024 zoom400   first      40 ms  sharp     141 ms  (12 tiles; overview 650 ms)
  pool w=4 t=1024 pan       first      36 ms  sharp     129 ms  (12 tiles; overview 650 ms)
  pool w=4 t=1024 zoom1600  first      10 ms  sharp      41 ms  (8 tiles; overview 650 ms)

## cad-extreme-e1.pdf — 42x30 in sheet, ~350k path objects + 33k labels (page 1)
  v1.1      fit       first    1514 ms  sharp    7287 ms  (6 tiles)
  v1.1      zoom400   first     911 ms  sharp   13385 ms  (15 tiles)
  v1.1      pan       first     895 ms  sharp   10750 ms  (12 tiles)
  v1.1      zoom1600  first     864 ms  sharp    6821 ms  (8 tiles)
  pool w=1 t=512  fit       first     366 ms  sharp    2218 ms  (12 tiles; overview 2393 ms)
  pool w=1 t=512  zoom400   first      30 ms  sharp     724 ms  (24 tiles; overview 2393 ms)
  pool w=1 t=512  pan       first      31 ms  sharp     683 ms  (24 tiles; overview 2393 ms)
  pool w=1 t=512  zoom1600  first      14 ms  sharp     328 ms  (24 tiles; overview 2393 ms)
  pool w=2 t=512  fit       first     381 ms  sharp    1673 ms  (12 tiles; overview 2424 ms)
  pool w=2 t=512  zoom400   first      41 ms  sharp     422 ms  (24 tiles; overview 2424 ms)
  pool w=2 t=512  pan       first      29 ms  sharp     444 ms  (24 tiles; overview 2424 ms)
  pool w=2 t=512  zoom1600  first      14 ms  sharp     199 ms  (24 tiles; overview 2424 ms)
  pool w=3 t=512  fit       first     429 ms  sharp    1834 ms  (12 tiles; overview 2363 ms)
  pool w=3 t=512  zoom400   first      34 ms  sharp     312 ms  (24 tiles; overview 2363 ms)
  pool w=3 t=512  pan       first      28 ms  sharp     298 ms  (24 tiles; overview 2363 ms)
  pool w=3 t=512  zoom1600  first      15 ms  sharp     148 ms  (24 tiles; overview 2363 ms)
  pool w=4 t=512  fit       first     570 ms  sharp    1968 ms  (12 tiles; overview 2382 ms)
  pool w=4 t=512  zoom400   first      43 ms  sharp     274 ms  (24 tiles; overview 2382 ms)
  pool w=4 t=512  pan       first      29 ms  sharp     360 ms  (24 tiles; overview 2382 ms)
  pool w=4 t=512  zoom1600  first      21 ms  sharp     126 ms  (24 tiles; overview 2382 ms)
  pool w=4 t=256  fit       first     180 ms  sharp    2160 ms  (48 tiles; overview 2388 ms)
  pool w=4 t=256  zoom400   first      19 ms  sharp     379 ms  (77 tiles; overview 2388 ms)
  pool w=4 t=256  pan       first      19 ms  sharp     367 ms  (77 tiles; overview 2388 ms)
  pool w=4 t=256  zoom1600  first      13 ms  sharp     215 ms  (66 tiles; overview 2388 ms)
  pool w=4 t=1024 fit       first     955 ms  sharp    2434 ms  (4 tiles; overview 2377 ms)
  pool w=4 t=1024 zoom400   first     131 ms  sharp     407 ms  (12 tiles; overview 2377 ms)
  pool w=4 t=1024 pan       first     123 ms  sharp     387 ms  (12 tiles; overview 2377 ms)
  pool w=4 t=1024 zoom1600  first      42 ms  sharp      95 ms  (8 tiles; overview 2377 ms)

## image-heavy.pdf — 8 pages, each a 4000x3000 RGB image (page 1)
  v1.1      fit       first      65 ms  sharp     308 ms  (6 tiles)
  v1.1      zoom400   first      45 ms  sharp     649 ms  (15 tiles)
  v1.1      pan       first      44 ms  sharp     653 ms  (15 tiles)
  v1.1      zoom1600  first      41 ms  sharp     615 ms  (15 tiles)
  pool w=1 t=512  fit       first       8 ms  sharp      81 ms  (12 tiles; overview 100 ms)
  pool w=1 t=512  zoom400   first       4 ms  sharp      91 ms  (24 tiles; overview 100 ms)
  pool w=1 t=512  pan       first       4 ms  sharp      89 ms  (24 tiles; overview 100 ms)
  pool w=1 t=512  zoom1600  first       2 ms  sharp      41 ms  (24 tiles; overview 100 ms)
  pool w=2 t=512  fit       first      11 ms  sharp      84 ms  (12 tiles; overview 101 ms)
  pool w=2 t=512  zoom400   first       4 ms  sharp      50 ms  (24 tiles; overview 101 ms)
  pool w=2 t=512  pan       first       4 ms  sharp      52 ms  (24 tiles; overview 101 ms)
  pool w=2 t=512  zoom1600  first       1 ms  sharp      21 ms  (24 tiles; overview 101 ms)
  pool w=3 t=512  fit       first      11 ms  sharp      75 ms  (12 tiles; overview 101 ms)
  pool w=3 t=512  zoom400   first       4 ms  sharp      42 ms  (24 tiles; overview 101 ms)
  pool w=3 t=512  pan       first       4 ms  sharp      41 ms  (24 tiles; overview 101 ms)
  pool w=3 t=512  zoom1600  first       3 ms  sharp      18 ms  (24 tiles; overview 101 ms)
  pool w=4 t=512  fit       first      12 ms  sharp      98 ms  (12 tiles; overview 101 ms)
  pool w=4 t=512  zoom400   first       7 ms  sharp      47 ms  (24 tiles; overview 101 ms)
  pool w=4 t=512  pan       first       8 ms  sharp      40 ms  (24 tiles; overview 101 ms)
  pool w=4 t=512  zoom1600  first       2 ms  sharp      14 ms  (24 tiles; overview 101 ms)
  pool w=4 t=256  fit       first       5 ms  sharp      93 ms  (48 tiles; overview 99 ms)
  pool w=4 t=256  zoom400   first       2 ms  sharp      30 ms  (66 tiles; overview 99 ms)
  pool w=4 t=256  pan       first       2 ms  sharp      32 ms  (66 tiles; overview 99 ms)
  pool w=4 t=256  zoom1600  first       1 ms  sharp      18 ms  (77 tiles; overview 99 ms)
  pool w=4 t=1024 fit       first      31 ms  sharp      87 ms  (4 tiles; overview 100 ms)
  pool w=4 t=1024 zoom400   first      20 ms  sharp      78 ms  (12 tiles; overview 100 ms)
  pool w=4 t=1024 pan       first      20 ms  sharp      48 ms  (9 tiles; overview 100 ms)
  pool w=4 t=1024 zoom1600  first       8 ms  sharp      25 ms  (12 tiles; overview 100 ms)

## many-pages-600.pdf — 600 pages, text + light vector content (page 301)
  v1.1      fit       first       6 ms  sharp      28 ms  (4 tiles)
  v1.1      zoom400   first       8 ms  sharp      79 ms  (12 tiles)
  v1.1      pan       first       7 ms  sharp      78 ms  (15 tiles)
  v1.1      zoom1600  first       3 ms  sharp      34 ms  (12 tiles)
  pool w=1 t=512  fit       first       2 ms  sharp      17 ms  (9 tiles; overview 24 ms)
  pool w=1 t=512  zoom400   first       5 ms  sharp     115 ms  (24 tiles; overview 24 ms)
  pool w=1 t=512  pan       first       5 ms  sharp      91 ms  (24 tiles; overview 24 ms)
  pool w=1 t=512  zoom1600  first       3 ms  sharp      57 ms  (24 tiles; overview 24 ms)
  pool w=2 t=512  fit       first       1 ms  sharp      24 ms  (9 tiles; overview 24 ms)
  pool w=2 t=512  zoom400   first       8 ms  sharp      63 ms  (24 tiles; overview 24 ms)
  pool w=2 t=512  pan       first       8 ms  sharp      55 ms  (24 tiles; overview 24 ms)
  pool w=2 t=512  zoom1600  first       2 ms  sharp      35 ms  (24 tiles; overview 24 ms)
  pool w=3 t=512  fit       first       1 ms  sharp      19 ms  (9 tiles; overview 25 ms)
  pool w=3 t=512  zoom400   first       6 ms  sharp      49 ms  (24 tiles; overview 25 ms)
  pool w=3 t=512  pan       first       5 ms  sharp      41 ms  (24 tiles; overview 25 ms)
  pool w=3 t=512  zoom1600  first       3 ms  sharp      24 ms  (24 tiles; overview 25 ms)
  pool w=4 t=512  fit       first       2 ms  sharp      24 ms  (9 tiles; overview 23 ms)
  pool w=4 t=512  zoom400   first       9 ms  sharp      44 ms  (24 tiles; overview 23 ms)
  pool w=4 t=512  pan       first       8 ms  sharp      55 ms  (24 tiles; overview 23 ms)
  pool w=4 t=512  zoom1600  first       5 ms  sharp      25 ms  (24 tiles; overview 23 ms)
  pool w=4 t=256  fit       first       1 ms  sharp      26 ms  (30 tiles; overview 23 ms)
  pool w=4 t=256  zoom400   first       4 ms  sharp      73 ms  (77 tiles; overview 23 ms)
  pool w=4 t=256  pan       first       4 ms  sharp      65 ms  (77 tiles; overview 23 ms)
  pool w=4 t=256  zoom1600  first       2 ms  sharp      41 ms  (77 tiles; overview 23 ms)
  pool w=4 t=1024 fit       first       1 ms  sharp      26 ms  (4 tiles; overview 23 ms)
  pool w=4 t=1024 zoom400   first      19 ms  sharp      36 ms  (6 tiles; overview 23 ms)
  pool w=4 t=1024 pan       first      14 ms  sharp      30 ms  (8 tiles; overview 23 ms)
  pool w=4 t=1024 zoom1600  first       9 ms  sharp      19 ms  (8 tiles; overview 23 ms)

| Document | Engine | Workers | Tile | View | First tile | Viewport sharp | Tiles |
|---|---|---:|---:|---|---:|---:|---:|
| cad-dense-e1.pdf | v1.1 | 1 | 768 | fit | 416 | 1897 | 6 |
| cad-dense-e1.pdf | v1.1 | 1 | 768 | zoom400 | 245 | 3557 | 15 |
| cad-dense-e1.pdf | v1.1 | 1 | 768 | pan | 237 | 2829 | 12 |
| cad-dense-e1.pdf | v1.1 | 1 | 768 | zoom1600 | 225 | 1745 | 8 |
| cad-dense-e1.pdf | v1.2 pool | 1 | 512 | fit | 101 | 612 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 1 | 512 | zoom400 | 15 | 231 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 1 | 512 | pan | 15 | 211 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 1 | 512 | zoom1600 | 4 | 98 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 2 | 512 | fit | 107 | 531 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 2 | 512 | zoom400 | 11 | 131 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 2 | 512 | pan | 10 | 115 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 2 | 512 | zoom1600 | 4 | 50 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 3 | 512 | fit | 19 | 478 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 3 | 512 | zoom400 | 13 | 99 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 3 | 512 | pan | 10 | 203 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 3 | 512 | zoom1600 | 7 | 114 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 512 | fit | 164 | 547 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 512 | zoom400 | 13 | 85 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 512 | pan | 891 | 1089 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 512 | zoom1600 | 4 | 62 | 24 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 256 | fit | 50 | 730 | 48 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 256 | zoom400 | 7 | 118 | 77 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 256 | pan | 7 | 112 | 77 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 256 | zoom1600 | 3 | 130 | 66 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 1024 | fit | 267 | 649 | 4 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 1024 | zoom400 | 40 | 141 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 1024 | pan | 36 | 129 | 12 |
| cad-dense-e1.pdf | v1.2 pool | 4 | 1024 | zoom1600 | 10 | 41 | 8 |
| cad-extreme-e1.pdf | v1.1 | 1 | 768 | fit | 1514 | 7287 | 6 |
| cad-extreme-e1.pdf | v1.1 | 1 | 768 | zoom400 | 911 | 13385 | 15 |
| cad-extreme-e1.pdf | v1.1 | 1 | 768 | pan | 895 | 10750 | 12 |
| cad-extreme-e1.pdf | v1.1 | 1 | 768 | zoom1600 | 864 | 6821 | 8 |
| cad-extreme-e1.pdf | v1.2 pool | 1 | 512 | fit | 366 | 2218 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 1 | 512 | zoom400 | 30 | 724 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 1 | 512 | pan | 31 | 683 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 1 | 512 | zoom1600 | 14 | 328 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 2 | 512 | fit | 381 | 1673 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 2 | 512 | zoom400 | 41 | 422 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 2 | 512 | pan | 29 | 444 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 2 | 512 | zoom1600 | 14 | 199 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 3 | 512 | fit | 429 | 1834 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 3 | 512 | zoom400 | 34 | 312 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 3 | 512 | pan | 28 | 298 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 3 | 512 | zoom1600 | 15 | 148 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 512 | fit | 570 | 1968 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 512 | zoom400 | 43 | 274 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 512 | pan | 29 | 360 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 512 | zoom1600 | 21 | 126 | 24 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 256 | fit | 180 | 2160 | 48 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 256 | zoom400 | 19 | 379 | 77 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 256 | pan | 19 | 367 | 77 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 256 | zoom1600 | 13 | 215 | 66 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 1024 | fit | 955 | 2434 | 4 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 1024 | zoom400 | 131 | 407 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 1024 | pan | 123 | 387 | 12 |
| cad-extreme-e1.pdf | v1.2 pool | 4 | 1024 | zoom1600 | 42 | 95 | 8 |
| image-heavy.pdf | v1.1 | 1 | 768 | fit | 65 | 308 | 6 |
| image-heavy.pdf | v1.1 | 1 | 768 | zoom400 | 45 | 649 | 15 |
| image-heavy.pdf | v1.1 | 1 | 768 | pan | 44 | 653 | 15 |
| image-heavy.pdf | v1.1 | 1 | 768 | zoom1600 | 41 | 615 | 15 |
| image-heavy.pdf | v1.2 pool | 1 | 512 | fit | 8 | 81 | 12 |
| image-heavy.pdf | v1.2 pool | 1 | 512 | zoom400 | 4 | 91 | 24 |
| image-heavy.pdf | v1.2 pool | 1 | 512 | pan | 4 | 89 | 24 |
| image-heavy.pdf | v1.2 pool | 1 | 512 | zoom1600 | 2 | 41 | 24 |
| image-heavy.pdf | v1.2 pool | 2 | 512 | fit | 11 | 84 | 12 |
| image-heavy.pdf | v1.2 pool | 2 | 512 | zoom400 | 4 | 50 | 24 |
| image-heavy.pdf | v1.2 pool | 2 | 512 | pan | 4 | 52 | 24 |
| image-heavy.pdf | v1.2 pool | 2 | 512 | zoom1600 | 1 | 21 | 24 |
| image-heavy.pdf | v1.2 pool | 3 | 512 | fit | 11 | 75 | 12 |
| image-heavy.pdf | v1.2 pool | 3 | 512 | zoom400 | 4 | 42 | 24 |
| image-heavy.pdf | v1.2 pool | 3 | 512 | pan | 4 | 41 | 24 |
| image-heavy.pdf | v1.2 pool | 3 | 512 | zoom1600 | 3 | 18 | 24 |
| image-heavy.pdf | v1.2 pool | 4 | 512 | fit | 12 | 98 | 12 |
| image-heavy.pdf | v1.2 pool | 4 | 512 | zoom400 | 7 | 47 | 24 |
| image-heavy.pdf | v1.2 pool | 4 | 512 | pan | 8 | 40 | 24 |
| image-heavy.pdf | v1.2 pool | 4 | 512 | zoom1600 | 2 | 14 | 24 |
| image-heavy.pdf | v1.2 pool | 4 | 256 | fit | 5 | 93 | 48 |
| image-heavy.pdf | v1.2 pool | 4 | 256 | zoom400 | 2 | 30 | 66 |
| image-heavy.pdf | v1.2 pool | 4 | 256 | pan | 2 | 32 | 66 |
| image-heavy.pdf | v1.2 pool | 4 | 256 | zoom1600 | 1 | 18 | 77 |
| image-heavy.pdf | v1.2 pool | 4 | 1024 | fit | 31 | 87 | 4 |
| image-heavy.pdf | v1.2 pool | 4 | 1024 | zoom400 | 20 | 78 | 12 |
| image-heavy.pdf | v1.2 pool | 4 | 1024 | pan | 20 | 48 | 9 |
| image-heavy.pdf | v1.2 pool | 4 | 1024 | zoom1600 | 8 | 25 | 12 |
| many-pages-600.pdf | v1.1 | 1 | 768 | fit | 6 | 28 | 4 |
| many-pages-600.pdf | v1.1 | 1 | 768 | zoom400 | 8 | 79 | 12 |
| many-pages-600.pdf | v1.1 | 1 | 768 | pan | 7 | 78 | 15 |
| many-pages-600.pdf | v1.1 | 1 | 768 | zoom1600 | 3 | 34 | 12 |
| many-pages-600.pdf | v1.2 pool | 1 | 512 | fit | 2 | 17 | 9 |
| many-pages-600.pdf | v1.2 pool | 1 | 512 | zoom400 | 5 | 115 | 24 |
| many-pages-600.pdf | v1.2 pool | 1 | 512 | pan | 5 | 91 | 24 |
| many-pages-600.pdf | v1.2 pool | 1 | 512 | zoom1600 | 3 | 57 | 24 |
| many-pages-600.pdf | v1.2 pool | 2 | 512 | fit | 1 | 24 | 9 |
| many-pages-600.pdf | v1.2 pool | 2 | 512 | zoom400 | 8 | 63 | 24 |
| many-pages-600.pdf | v1.2 pool | 2 | 512 | pan | 8 | 55 | 24 |
| many-pages-600.pdf | v1.2 pool | 2 | 512 | zoom1600 | 2 | 35 | 24 |
| many-pages-600.pdf | v1.2 pool | 3 | 512 | fit | 1 | 19 | 9 |
| many-pages-600.pdf | v1.2 pool | 3 | 512 | zoom400 | 6 | 49 | 24 |
| many-pages-600.pdf | v1.2 pool | 3 | 512 | pan | 5 | 41 | 24 |
| many-pages-600.pdf | v1.2 pool | 3 | 512 | zoom1600 | 3 | 24 | 24 |
| many-pages-600.pdf | v1.2 pool | 4 | 512 | fit | 2 | 24 | 9 |
| many-pages-600.pdf | v1.2 pool | 4 | 512 | zoom400 | 9 | 44 | 24 |
| many-pages-600.pdf | v1.2 pool | 4 | 512 | pan | 8 | 55 | 24 |
| many-pages-600.pdf | v1.2 pool | 4 | 512 | zoom1600 | 5 | 25 | 24 |
| many-pages-600.pdf | v1.2 pool | 4 | 256 | fit | 1 | 26 | 30 |
| many-pages-600.pdf | v1.2 pool | 4 | 256 | zoom400 | 4 | 73 | 77 |
| many-pages-600.pdf | v1.2 pool | 4 | 256 | pan | 4 | 65 | 77 |
| many-pages-600.pdf | v1.2 pool | 4 | 256 | zoom1600 | 2 | 41 | 77 |
| many-pages-600.pdf | v1.2 pool | 4 | 1024 | fit | 1 | 26 | 4 |
| many-pages-600.pdf | v1.2 pool | 4 | 1024 | zoom400 | 19 | 36 | 6 |
| many-pages-600.pdf | v1.2 pool | 4 | 1024 | pan | 14 | 30 | 8 |
| many-pages-600.pdf | v1.2 pool | 4 | 1024 | zoom1600 | 9 | 19 | 8 |
