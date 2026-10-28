#!/usr/bin/env python3
"""Makes every app icon file from branding/ekpdf-icon-source.webp.

Usage: make-icons.py <repo root>

- Enclosed transparent areas (the white page, the hexagon) become opaque white,
  so the icon reads the same on dark taskbars and Revit's dark theme.
- Style guide method "C": exact-area (box) downsampling; the logo's height fills
  the square and its left edge lands on a whole pixel. Same colours, no redrawing.
Writes:
  offline3d-reader/fastpdf/src/EKFastPDF/Assets/FastPDF.ico   viewer exe/window (16-256)
  offline3d-exporter/src/RevitExporter/Resources/Export3DPdf*.png   ribbon (32 DIP and 16 DIP sets)
  branding/png/ekpdf-<n>.png                                   installers and docs
  offline3d-reader/fastpdf/src/EKFastPDF/Assets/ClickTo3D-<n>.png  ClickTo3D button (from clickto3d-icon-source.png)
"""
import sys
from collections import deque
from pathlib import Path
from PIL import Image

LARGE = [32, 40, 48, 56, 64, 72, 80, 96]
SMALL = [16, 20, 24, 28, 32, 36, 40, 48]
ICO = [16, 20, 24, 32, 40, 48, 64, 128, 256]

def prepared(source: Path) -> Image.Image:
    """Cropped to the logo, enclosed transparent areas filled white."""
    im = Image.open(source).convert('RGBA')
    im = im.crop(im.getchannel('A').getbbox())
    w, h = im.size; px = im.load()
    outside = [[False] * w for _ in range(h)]
    queue = deque((x, y) for x in range(w) for y in (0, h - 1)) ; queue.extend((x, y) for y in range(h) for x in (0, w - 1))
    while queue:
        x, y = queue.popleft()
        if x < 0 or y < 0 or x >= w or y >= h or outside[y][x] or px[x, y][3] >= 128: continue
        outside[y][x] = True
        queue.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    for y in range(h):
        for x in range(w):
            if not outside[y][x]:
                r, g, b, a = px[x, y]
                # Composite over white: enclosed areas and soft edges inside the outline.
                px[x, y] = (round(r * a / 255 + 255 * (1 - a / 255)), round(g * a / 255 + 255 * (1 - a / 255)), round(b * a / 255 + 255 * (1 - a / 255)), 255)
    return im

def square(im: Image.Image, n: int) -> Image.Image:
    w, h = im.size
    width = max(1, round(w * n / h))
    body = im.resize((width, n), Image.BOX)
    canvas = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    canvas.paste(body, ((n - width) // 2, 0))
    return canvas

def main(root: Path):
    im = prepared(root / 'branding' / 'ekpdf-icon-source.webp')
    pngs = root / 'branding' / 'png'; pngs.mkdir(parents=True, exist_ok=True)
    for n in sorted(set(LARGE + SMALL + ICO + [512])): square(im, n).save(pngs / f'ekpdf-{n}.png')
    res = root / 'offline3d-exporter' / 'src' / 'RevitExporter' / 'Resources'; res.mkdir(parents=True, exist_ok=True)
    for n in LARGE: square(im, n).save(res / ('Export3DPdf.png' if n == 32 else f'Export3DPdf_{n}.png'))
    for n in SMALL: square(im, n).save(res / ('Export3DPdf_16.png' if n == 16 else f'Export3DPdf_16_{n}.png'))
    ico = root / 'offline3d-reader' / 'fastpdf' / 'src' / 'EKFastPDF' / 'Assets' / 'FastPDF.ico'
    big = square(im, 256)
    big.save(ico, format='ICO', sizes=[(n, n) for n in ICO], append_images=[square(im, n) for n in ICO if n != 256])
    # ClickTo3D button icon: 16 DIP at 100-300 % display scale.
    click = prepared(root / 'branding' / 'clickto3d-icon-source.png')
    assets = root / 'offline3d-reader' / 'fastpdf' / 'src' / 'EKFastPDF' / 'Assets'
    for n in (16, 20, 24, 32, 48): square(click, n).save(assets / f'ClickTo3D-{n}.png')
    # The app logo inside the window (title bar 18 DIP, welcome 56 DIP), sharp at high DPI.
    for n in (64, 256): square(im, n).save(assets / f'EKPDF-{n}.png')
    square(click, 256).save(pngs / 'clickto3d-256.png')

if __name__ == '__main__':
    main(Path(sys.argv[1]).resolve())
