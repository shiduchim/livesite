#!/usr/bin/env python3
"""Writes Assets/ClickTo3D.cur: the standard arrow in blue, at 32, 48 and 64 px.

Usage: make-clickto3d-cursor.py <out.cur>
Each image is a 32-bit BMP with alpha (Windows Vista and later), hotspot at the tip.
"""
import struct, sys
from PIL import Image, ImageDraw

# Windows arrow outline on a 32 px grid (tip at 0,0).
ARROW = [(0, 0), (0, 22), (5.2, 17.3), (8.6, 25.2), (12.0, 23.7), (8.7, 16.0), (15.5, 16.0)]
FILL, EDGE, HALO = (47, 111, 222, 255), (20, 52, 120, 255), (255, 255, 255, 235)

def image(size):
    scale, ss = size / 32, 8
    big = Image.new('RGBA', (size * ss, size * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    pts = [((x * scale + 1.0) * ss, (y * scale + 1.0) * ss) for x, y in ARROW]
    d.polygon(pts, fill=HALO, outline=HALO, width=int(2.4 * scale * ss))   # light halo for dark drawings
    d.polygon(pts, fill=EDGE)
    inner = [((x * scale + 1.0) * ss, (y * scale + 1.0) * ss) for x, y in ARROW]
    d.polygon(inner, fill=FILL, outline=EDGE, width=int(1.1 * scale * ss))
    return big.resize((size, size), Image.LANCZOS)

def bmp(img):
    w, h = img.size
    header = struct.pack('<IiiHHIIiiII', 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    px = img.load(); xor = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = px[x, y]; xor += bytes((b, g, r, a))
    row = ((w + 31) // 32) * 4
    return header + bytes(xor) + bytes(row * h)   # AND mask unused with alpha

def main(out):
    sizes = (32, 48, 64); blobs = [bmp(image(s)) for s in sizes]
    data = struct.pack('<HHH', 0, 2, len(sizes)); offset = 6 + 16 * len(sizes)
    for s, blob in zip(sizes, blobs):
        hot = round(s / 32)   # the tip sits one grid unit in
        data += struct.pack('<BBBBHHII', s % 256, s % 256, 0, 0, hot, hot, len(blob), offset); offset += len(blob)
    open(out, 'wb').write(data + b''.join(blobs))

if __name__ == '__main__':
    main(sys.argv[1])
