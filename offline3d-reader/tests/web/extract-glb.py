#!/usr/bin/env python3
"""Copies the embedded GLB out of an Offline 3D PDF for the browser check.

Usage: extract-glb.py <file.pdf> <out.glb>
"""
import re, sys, zlib

def main(pdf, out):
    data = open(pdf, 'rb').read()
    for m in re.finditer(rb'stream\r?\n', data):
        raw = data[m.end():data.find(b'endstream', m.end())]
        try: body = zlib.decompress(raw)
        except zlib.error: body = raw
        if body[:4] == b'glTF':
            open(out, 'wb').write(body[:int.from_bytes(body[8:12], 'little')]); return
    sys.exit('No GLB found in ' + pdf)

if __name__ == '__main__':
    main(*sys.argv[1:3])
