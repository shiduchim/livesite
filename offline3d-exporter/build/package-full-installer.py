#!/usr/bin/env python3
"""Builds the full Offline 3D PDF 0.5.3 installer from the 0.5.2 package.

Usage:
  package-full-installer.py --base <extracted .../Offline3DPdfPrototype> --viewer <published viewer>
      --exporters <dir of <year>-<framework> folders> --reader <offline3d-reader> --out <zip>

The 0.5.2 package keeps its installer scripts, licences, samples and tests.
This script then:
- replaces the prebuilt viewer and exporters;
- refreshes the source copies that BUILD_AND_INSTALL.cmd compiles;
- applies build/installer-overlay (README, status, release verifier);
- bumps the version strings;
- reseals payload/package.json, which lists the size and SHA-256 of every
  payload file, as Setup.ps1 requires;
- runs the release verifier and writes the ZIP.
"""
import argparse, os, shutil, subprocess, sys, tempfile, zipfile
from pathlib import Path

VARIANTS = ['2024-net48', '2025-net8.0-windows', '2025-net10.0-windows', '2026-net8.0-windows', '2026-net10.0-windows', '2027-net10.0-windows']
EXPORTER = Path(__file__).resolve().parents[1]
GENERATED = shutil.ignore_patterns('bin', 'obj', 'node_modules', '.git')

def replace_tree(source: Path, target: Path):
    if target.exists(): shutil.rmtree(target)
    shutil.copytree(source, target, ignore=GENERATED)

def edit(path: Path, pairs):
    raw = path.read_bytes(); bom = raw.startswith(b'\xef\xbb\xbf'); text = raw.decode('utf-8-sig')
    for old, new in pairs:
        if text.count(old) < 1: sys.exit(f'{path.name}: expected text not found: {old}')
        text = text.replace(old, new)
    path.write_bytes((b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8'))

def main():
    a = argparse.ArgumentParser()
    for name in ('base', 'viewer', 'exporters', 'reader', 'out'): a.add_argument('--' + name, required=True)
    args = a.parse_args()
    base, viewer, exporters, reader, out = (Path(getattr(args, n)).resolve() for n in ('base', 'viewer', 'exporters', 'reader', 'out'))
    stage_root = Path(tempfile.mkdtemp(prefix='o3d-installer-')); stage = stage_root / 'Offline3DPdfPrototype'
    try:
        shutil.copytree(base, stage, symlinks=False)
        payload = stage / 'payload'
        # Prebuilt viewer and exporters.
        replace_tree(viewer, payload / 'Viewer')
        for variant in VARIANTS:
            source, target = exporters / variant, payload / 'Exporters' / variant
            if not any(source.glob('*.dll')): sys.exit('Missing exporter build: ' + variant)
            replace_tree(source, target)
        # Sources compiled by BUILD_AND_INSTALL.cmd and checked by Verify-Release.py.
        replace_tree(EXPORTER / 'src' / 'RevitExporter', stage / 'src' / 'RevitExporter')
        for shared in ('Core', 'Packaging', 'AutomaticRegistration'): replace_tree(reader / 'src' / shared, stage / 'src' / shared)
        replace_tree(reader / 'src' / 'Viewer' / 'web', stage / 'src' / 'Viewer' / 'web')
        for project in ('EKFastPDF', 'EKFastPDF.Core'): replace_tree(reader / 'fastpdf' / 'src' / project, stage / 'fastpdf' / 'src' / project)
        # Documents and the release verifier.
        shutil.copytree(EXPORTER / 'build' / 'installer-overlay', stage, dirs_exist_ok=True)
        shutil.copy2(reader / 'CHANGES-0.5.3.md', stage / 'CHANGES-0.5.3-VIEWER.md')
        shutil.copy2(EXPORTER / 'CHANGES-0.5.3.md', stage / 'CHANGES-0.5.3-EXPORTER.md')
        shutil.copy2(EXPORTER / 'ACCEPTANCE-TEST.md', stage / 'ACCEPTANCE-TEST-0.5.3.md')
        (stage / 'CHANGES-0.5.3.md').write_text('# Offline 3D PDF 0.5.3\n\n- Viewer: [CHANGES-0.5.3-VIEWER.md](CHANGES-0.5.3-VIEWER.md)\n- Exporter: [CHANGES-0.5.3-EXPORTER.md](CHANGES-0.5.3-EXPORTER.md)\n- Revit acceptance steps: [ACCEPTANCE-TEST-0.5.3.md](ACCEPTANCE-TEST-0.5.3.md)\n', encoding='utf-8')
        # Version strings shown by setup and written into package.json.
        edit(stage / 'scripts' / 'Setup.ps1', [("'Offline 3D PDF v0.5.2 installation complete.'", "'Offline 3D PDF v0.5.3 installation complete.'"),
                                                ("version='0.5.2-experimental'", "version='0.5.3-experimental'")])
        edit(stage / 'scripts' / 'Seal-Payload.py', [("'version':'0.5.2-experimental'", "'version':'0.5.3-experimental'")])
        old_report = stage / 'tests' / 'release-verification-v052.json'
        if old_report.exists(): old_report.unlink()
        subprocess.run([sys.executable, '-I', str(stage / 'scripts' / 'Seal-Payload.py')], check=True)
        subprocess.run([sys.executable, '-I', str(stage / 'scripts' / 'Verify-Release.py')], check=True, stdout=subprocess.DEVNULL)
        out.parent.mkdir(parents=True, exist_ok=True)
        if out.exists(): out.unlink()
        with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
            for path in sorted(p for p in stage_root.rglob('*') if p.is_file()):
                z.write(path, path.relative_to(stage_root).as_posix())
        print(f'{out} ({out.stat().st_size / 1048576:.1f} MiB)')
    finally:
        shutil.rmtree(stage_root, ignore_errors=True)

if __name__ == '__main__':
    main()
