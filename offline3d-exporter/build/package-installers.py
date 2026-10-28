#!/usr/bin/env python3
"""Builds the two EKPDF installers (0.5.4 and later).

  EKPDF-<version>.zip            the PDF reader, for anyone (no Revit needed)
  EKPDF-RevitAddin-<version>.zip "Export 3D PDF" for Revit 2024-2027 (needs EKPDF)

Usage:
  package-installers.py --viewer <self-contained win-x64 viewer publish>
      --exporters <dir of <year>-<framework> folders> --notices <extracted 0.5.3 package>
      --version 0.5.4 --out <dir>

Each package is: INSTALL.cmd, UNINSTALL.cmd, README.txt, scripts/, and payload/ with
package.json listing the size and SHA-256 of every payload file (setup verifies it).
After sealing, the payload is checked: hashes, the viewer's 3D page files are
byte-identical to src/Viewer/web and every file they reference ships, the version and
name are in the compiled viewer, each exporter DLL is 0.5.4 and no Autodesk DLL ships.
"""
import argparse, hashlib, json, re, shutil, sys, tempfile, uuid, zipfile
from pathlib import Path

VARIANTS = ['2024-net48', '2025-net8.0-windows', '2025-net10.0-windows', '2026-net8.0-windows', '2026-net10.0-windows', '2027-net10.0-windows']
EXPORTER = Path(__file__).resolve().parents[1]
READER = EXPORTER.parent / 'offline3d-reader'

CMD = '''@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "PS=%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe"
if defined PROCESSOR_ARCHITEW6432 set "PS=%SystemRoot%\\Sysnative\\WindowsPowerShell\\v1.0\\powershell.exe"
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\\{script}" -Mode {mode}
exit /b %ERRORLEVEL%
'''

READER_README = '''EKPDF {version}

A fast PDF reader. PDFs exported from Revit with clickable 3D open in 3D:
press ClickTo3D and click the plan.

INSTALL
1. Extract this ZIP.
2. Close EKPDF if it is open.
3. Double-click INSTALL.cmd.

Installs for your Windows user only; no administrator rights are needed.
Open it from Start > EKPDF. Remove it from Windows Settings > Apps, or with
UNINSTALL.cmd. Your PDFs and settings are kept.

EKPDF needs Microsoft WebView2 Runtime for its 3D view. Windows 10 and 11
normally include it; if not, setup asks before installing Microsoft's
official runtime.

Revit users: install the EKPDF Revit add-in package after this one.
'''

ADDIN_README = '''EKPDF Revit add-in {version}

Adds Add-Ins > Export 3D PDF to Revit 2024, 2025, 2026 and 2027. It exports
a plan or sheet as a PDF with clickable offline 3D.

INSTALL
1. Install EKPDF first (the EKPDF-{version} package). The add-in uses it to
   finish each 3D PDF.
2. Extract this ZIP and close Revit.
3. Double-click INSTALL.cmd.

Installs for your Windows user only, for every Revit 2024-2027 found. It
replaces an earlier "Offline 3D PDF" 0.5.x installation automatically.
Remove it from Windows Settings > Apps, or with UNINSTALL.cmd.
'''

def write_ps1(source: Path, target: Path):
    """PowerShell 5.1 needs a BOM to read UTF-8 scripts correctly."""
    text = source.read_text(encoding='utf-8-sig')
    target.write_bytes(b'\xef\xbb\xbf' + text.replace('\r\n', '\n').replace('\n', '\r\n').encode('utf-8'))

def write_text(path: Path, text: str):
    path.write_bytes(text.replace('\r\n', '\n').replace('\n', '\r\n').encode('utf-8'))

def seal(payload: Path, product: str, version: str, extra: dict) -> dict:
    files = [{'path': p.relative_to(payload).as_posix(), 'size': p.stat().st_size, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
             for p in sorted(payload.rglob('*')) if p.is_file() and p.name != 'package.json']
    manifest = {'schema': 1, 'product': product, 'version': version, 'releaseId': uuid.uuid4().hex, **extra, 'files': files,
                'windowsRuntimeTested': False, 'revitRuntimeTested': False}
    (payload / 'package.json').write_text(json.dumps(manifest, indent=2) + '\n')
    return manifest

def verify_hashes(payload: Path, manifest: dict):
    listed = {f['path'] for f in manifest['files']}
    actual = {p.relative_to(payload).as_posix() for p in payload.rglob('*') if p.is_file() and p.name != 'package.json'}
    assert listed == actual, (listed - actual, actual - listed)
    for f in manifest['files']:
        data = (payload / f['path']).read_bytes()
        assert len(data) == f['size'] and hashlib.sha256(data).hexdigest() == f['sha256'], f['path']
    assert not list(payload.rglob('RevitAPI.dll')) and not list(payload.rglob('RevitAPIUI.dll')), 'Autodesk DLLs must not ship'

def verify_viewer(payload: Path, version: str) -> list:
    web = payload / 'Viewer' / 'offline3d'; assets = []
    for p in web.rglob('*'):
        if p.is_file():
            rel = p.relative_to(web); assert p.read_bytes() == (READER / 'src/Viewer/web' / rel).read_bytes(), str(rel); assets.append(rel.as_posix())
    missing = []
    for p in list(web.glob('*.mjs')) + list(web.glob('*.html')):
        for ref in re.findall(r"""(?:from\s+|import\s*\(\s*|src=|href=|new URL\(\s*)['"](\.{0,2}/?[\w./-]+\.(?:mjs|js|css|wasm))['"]""", p.read_text(encoding='utf-8', errors='replace')):
            if not (p.parent / ref).resolve().is_file(): missing.append(p.name + ' -> ' + ref)
    assert not missing, missing
    # BoxPanel serves only allow-listed page files: every shipped page file must be on it.
    allowed = set(re.findall(r'"(/[\w./-]+)"', re.search(r'Allowed=new\(StringComparer\.Ordinal\)\{(.*?)\};', (READER / 'fastpdf/src/EKFastPDF/Offline3D/BoxPanel.cs').read_text(), re.S).group(1)))
    blocked = [a for a in assets if a.endswith(('.mjs', '.html', '.css')) and '/' + a not in allowed]
    assert not blocked, ('page files not on the BoxPanel allow-list', blocked)
    dll = (payload / 'Viewer/Offline3D.FastPDF.Dev.dll').read_bytes()
    assert 'EKPDF'.encode('utf-16le') in dll and 'offline3d.example'.encode('utf-16le') in dll
    assert version.encode() in dll or (version + '.0').encode() in dll, 'viewer version'
    return sorted(assets)

def package(stage_root: Path, out: Path):
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists(): out.unlink()
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for path in sorted(p for p in stage_root.rglob('*') if p.is_file()):
            z.write(path, path.relative_to(stage_root).as_posix())
    digest = hashlib.sha256(out.read_bytes()).hexdigest()
    (out.parent / (out.name + '.sha256')).write_text(digest + '  ' + out.name + '\n')
    print(f'{out.name}: {out.stat().st_size / 1048576:.1f} MiB  sha256 {digest}')

def main():
    a = argparse.ArgumentParser()
    for name in ('viewer', 'exporters', 'notices', 'version', 'out'): a.add_argument('--' + name, required=True)
    args = a.parse_args()
    viewer, exporters, notices, out = (Path(getattr(args, n)).resolve() for n in ('viewer', 'exporters', 'notices', 'out'))
    version = args.version
    legal = notices / 'payload'
    work = Path(tempfile.mkdtemp(prefix='ekpdf-installers-'))
    report = {'version': version}
    try:
        # ---- EKPDF reader ----
        root = work / 'reader'; stage = root / f'EKPDF-{version}'; payload = stage / 'payload'
        shutil.copytree(viewer, payload / 'Viewer')
        for name in ('licenses',): shutil.copytree(legal / name, payload / name)
        for name in ('THIRD-PARTY-NOTICES.md', 'FASTPDF-THIRD-PARTY-NOTICES.txt', 'SYNTHETIC-Offline3D.pdf'): shutil.copy2(legal / name, payload / name)
        (stage / 'scripts').mkdir(parents=True)
        for name in ('Setup-Reader.ps1', 'Setup.Common.ps1'): write_ps1(READER / 'installer/scripts' / name, stage / 'scripts' / name)
        shutil.copy2(READER / 'installer/scripts/dependencies.json', stage / 'scripts/dependencies.json')
        write_text(stage / 'INSTALL.cmd', CMD.format(script='Setup-Reader.ps1', mode='Install'))
        write_text(stage / 'UNINSTALL.cmd', CMD.format(script='Setup-Reader.ps1', mode='Uninstall'))
        write_text(stage / 'README.txt', READER_README.format(version=version))
        manifest = seal(payload, 'EKPDF', version, {})
        verify_hashes(payload, manifest)
        report['reader'] = {'files': len(manifest['files']), 'webAssets': verify_viewer(payload, version)}
        package(root, out / f'EKPDF-{version}.zip')

        # ---- Revit add-in ----
        root = work / 'addin'; stage = root / f'EKPDF-RevitAddin-{version}'; payload = stage / 'payload'
        entries = []
        for variant in VARIANTS:
            year, framework = variant.split('-', 1)
            dll = exporters / variant / f'Offline3DPrototype.Revit{year}.dll'
            if not dll.is_file(): sys.exit('Missing exporter build: ' + variant)
            shutil.copytree(exporters / variant, payload / 'Exporters' / variant)
            assert f'{version}.0'.encode() in dll.read_bytes() or version.encode() in dll.read_bytes(), variant + ' version'
            entries.append({'year': int(year), 'framework': framework, 'path': f'Exporters/{variant}/Offline3DPrototype.Revit{year}.dll'})
        shutil.copy2(legal / 'THIRD-PARTY-NOTICES.md', payload / 'THIRD-PARTY-NOTICES.md')
        (stage / 'scripts').mkdir(parents=True)
        write_ps1(EXPORTER / 'build/addin-installer/scripts/Setup-Addin.ps1', stage / 'scripts/Setup-Addin.ps1')
        write_ps1(READER / 'installer/scripts/Setup.Common.ps1', stage / 'scripts/Setup.Common.ps1')
        write_text(stage / 'INSTALL.cmd', CMD.format(script='Setup-Addin.ps1', mode='Install'))
        write_text(stage / 'UNINSTALL.cmd', CMD.format(script='Setup-Addin.ps1', mode='Uninstall'))
        write_text(stage / 'README.txt', ADDIN_README.format(version=version))
        manifest = seal(payload, 'EKPDF-RevitAddin', version, {'exporters': entries})
        verify_hashes(payload, manifest)
        report['addin'] = {'files': len(manifest['files']), 'exporters': len(entries)}
        package(root, out / f'EKPDF-RevitAddin-{version}.zip')
        (out / f'verification-{version}.json').write_text(json.dumps(report, indent=2) + '\n')
    finally:
        shutil.rmtree(work, ignore_errors=True)

if __name__ == '__main__':
    main()
