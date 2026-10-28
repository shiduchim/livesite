#!/usr/bin/env python3
"""Test fixture: adds a Revit-style cut pattern to every material of a GLB.

Usage: add-cut-pattern.py <in.glb> <out.glb>
The pattern is diagonal crosshatch, model target, 0.5 ft spacing (like Revit's "Crosshatch").
"""
import json, math, struct, sys

def main(src, dst):
    data = open(src, 'rb').read()
    length = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20 + length])
    grid = lambda a: {'angle': a, 'originXFeet': 0, 'originYFeet': 0, 'offsetFeet': .5, 'shiftFeet': 0, 'segmentsFeet': []}
    for m in doc.get('materials', []):
        m.setdefault('extras', {})['cutPattern'] = {'name': 'Crosshatch', 'target': 'Model', 'solid': False, 'grids': [grid(math.pi / 4), grid(-math.pi / 4)]}
    text = json.dumps(doc, separators=(',', ':')).encode()
    text += b' ' * (-len(text) % 4)
    rest = data[20 + length:]
    out = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(text) + len(rest)) + struct.pack('<II', len(text), 0x4E4F534A) + text + rest
    open(dst, 'wb').write(out)

if __name__ == '__main__':
    main(*sys.argv[1:3])
