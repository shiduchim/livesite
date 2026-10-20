"""
EK Print ribbon icons - rebuilt from the original high-resolution artwork.

The artwork itself is NOT redesigned. This script only:
  * separates the blue artwork from the white page background,
  * keeps the white fills inside the shapes (page, badge, checkbox, letters),
  * downsamples with premultiplied alpha straight to each exact pixel size,
  * writes PNGs with no DPI metadata (WPF then treats them as exactly 96 DPI).

Sizes written per icon:
  Large ribbon button (32 DIP): Name.png (32), Name_40.png, Name_48.png, Name_64.png
  Small ribbon button (16 DIP): Name_16.png (16), Name_16_20.png, Name_16_24.png, Name_16_32.png

Settings has no high-resolution source, so its original 32/16 PNGs are kept as-is.

Usage:  python build_icons.py <source.png> <Assets folder>
"""
import os
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

# Left-to-right order of the icons in the source artwork.
ORDER = ["QuickPDF", "SelectPDF", "QuickDWG", "SelectDWG", "SheetPrint"]
# Icons whose page outline is crossed by a badge: seal that thin white gap so
# the page interior stays white instead of leaking to the outside.
SEAL_RADIUS = {"QuickPDF": 9, "SelectPDF": 9, "SheetPrint": 9, "QuickDWG": 0, "SelectDWG": 0}
# Text-only icons: only fill large enclosed areas (checkbox, play triangle),
# not small letter counters (inside D / G stay transparent like the originals).
MIN_HOLE = {"QuickPDF": 0, "SelectPDF": 0, "SheetPrint": 0, "QuickDWG": 4000, "SelectDWG": 4000}

LARGE = [32, 40, 48, 64]
SMALL = [16, 20, 24, 32]


def disk(r):
    y, x = np.ogrid[-r:r + 1, -r:r + 1]
    return x * x + y * y <= r * r


def separate(rgb):
    """Return (blue_alpha, white_alpha, blue_rgb) for one icon crop."""
    R = rgb[:, :, 0].astype(np.float64)
    solid = R < 80
    blue = np.median(rgb[solid].reshape(-1, 3), axis=0) if solid.any() else np.array([47, 107, 197.0])
    a = np.clip((254.0 - R) / (254.0 - blue[0]), 0.0, 1.0)
    return a, blue


def white_fill(a, name):
    shape = a > 0.5
    r = SEAL_RADIUS[name]
    closed = ndimage.binary_closing(shape, structure=disk(r), iterations=1) if r > 0 else shape
    filled = ndimage.binary_fill_holes(closed)
    interior = filled & ~shape
    if MIN_HOLE[name] > 0:
        lab, n = ndimage.label(interior)
        sizes = ndimage.sum(interior, lab, range(1, n + 1))
        keep = np.zeros(n + 1, bool)
        keep[1:] = sizes >= MIN_HOLE[name]
        interior = keep[lab]
        filled = shape | interior
    # Slide the white under the blue anti-aliased edges, never past the silhouette.
    w = ndimage.binary_dilation(interior, structure=disk(3)) & ndimage.binary_erosion(filled, structure=disk(2))
    return ndimage.gaussian_filter(w.astype(np.float64), 0.8)


def render(a, w, blue, size, fit):
    # premultiplied RGBA at source resolution
    out_a = a + w * (1.0 - a)
    pm = [a * blue[i] + w * (1.0 - a) * 255.0 for i in range(3)]

    ys, xs = np.where(out_a > 0.04)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    side = max(y1 - y0, x1 - x0)
    canvas = int(round(side / fit))
    cy, cx = (y0 + y1) / 2.0, (x0 + x1) / 2.0
    top, left = int(round(cy - canvas / 2.0)), int(round(cx - canvas / 2.0))

    def crop(ch):
        pad = np.zeros((canvas, canvas))
        sy0, sx0 = max(top, 0), max(left, 0)
        sy1, sx1 = min(top + canvas, ch.shape[0]), min(left + canvas, ch.shape[1])
        pad[sy0 - top:sy1 - top, sx0 - left:sx1 - left] = ch[sy0:sy1, sx0:sx1]
        return Image.fromarray(pad.astype(np.float32), "F").resize((size, size), Image.LANCZOS)

    A = np.clip(np.array(crop(out_a)), 0, 1)
    C = [np.array(crop(c)) for c in pm]
    rgba = np.zeros((size, size, 4), np.uint8)
    safe = np.where(A > 1e-4, A, 1.0)
    for i in range(3):
        rgba[:, :, i] = np.clip(C[i] / safe, 0, 255).round().astype(np.uint8)
    rgba[:, :, 3] = (A * 255).round().astype(np.uint8)
    rgba[rgba[:, :, 3] == 0, :3] = 0
    return Image.fromarray(rgba, "RGBA")


def main(src, out):
    rgb = np.array(Image.open(src).convert("RGB")).astype(np.float64)
    is_ink = (rgb[:, :, 0] < 200).any(0)
    runs, start = [], None
    for x, v in enumerate(is_ink):
        if v and start is None:
            start = x
        if not v and start is not None:
            runs.append((start, x))
            start = None
    if start is not None:
        runs.append((start, len(is_ink)))
    if len(runs) != len(ORDER):
        raise SystemExit("Expected %d icons in the source, found %d" % (len(ORDER), len(runs)))

    os.makedirs(out, exist_ok=True)
    for name, (x0, x1) in zip(ORDER, runs):
        piece = rgb[:, max(0, x0 - 12):x1 + 12, :]
        a, blue = separate(piece)
        w = white_fill(a, name)
        for s in LARGE:
            render(a, w, blue, s, 31.0 / 32.0).save(os.path.join(out, name + (".png" if s == 32 else "_%d.png" % s)), optimize=True)
        for s in SMALL:
            render(a, w, blue, s, 15.5 / 16.0).save(os.path.join(out, name + ("_16.png" if s == 16 else "_16_%d.png" % s)), optimize=True)
        print("built", name)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "Assets")
