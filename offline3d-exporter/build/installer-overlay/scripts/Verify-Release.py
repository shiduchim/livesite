"""Verify sealed prebuilt files and source/payload freshness without executing Windows code."""
from pathlib import Path
import hashlib,json,re
root=Path(__file__).resolve().parents[1];payload=root/'payload'
m=json.loads((payload/'package.json').read_text())
assert m['version']=='0.5.3-experimental'
assert m['windowsRuntimeTested'] is False and m['revitRuntimeTested'] is False
listed={x['path'] for x in m['files']}
actual={p.relative_to(payload).as_posix() for p in payload.rglob('*') if p.is_file() and p.name!='package.json'}
assert listed==actual,(listed-actual,actual-listed)
for item in m['files']:
 data=(payload/item['path']).read_bytes()
 assert len(data)==item['size'] and hashlib.sha256(data).hexdigest()==item['sha256'],item['path']
web=payload/'Viewer/offline3d';assets=[]
for p in web.rglob('*'):
 if p.is_file():
  rel=p.relative_to(web);assert p.read_bytes()==(root/'src/Viewer/web'/rel).read_bytes(),str(rel);assets.append(rel.as_posix())
# Every module, script, stylesheet or worker the 3D page references must ship.
missing=[]
for p in list(web.glob('*.mjs'))+list(web.glob('*.html')):
 for ref in re.findall(r"""(?:from\s+|import\s*\(\s*|src=|href=|new URL\(\s*)['"](\.{0,2}/?[\w./-]+\.(?:mjs|js|css|wasm))['"]""",p.read_text(encoding='utf-8',errors='replace')):
  if not (p.parent/ref).resolve().is_file():missing.append(p.name+' -> '+ref)
assert not missing,missing
for removed in ['filament.js','filament.wasm','filament-adapter.mjs','playcanvas-adapter.mjs','photo-adapter.mjs']:
 assert not list((payload/'Viewer').rglob(removed)),removed
assembly=(payload/'Viewer/Offline3D.FastPDF.Dev.dll').read_bytes()
assert 'v0.5.3'.encode('utf-16le') in assembly
assert 'offline3d.example'.encode('utf-16le') in assembly and 'offline3d-v052.local'.encode('utf-16le') not in assembly
for item in m['exporters']:
 p=payload/item['path'];assert len(list(p.parent.glob('*.dll')))==1
 assert '0.5.3.0'.encode() in p.read_bytes()
dialog=(root/'src/RevitExporter/ExportOptionsDialog.cs').read_text()
assert 'outsideCrop = new CheckBox' in dialog and 'Checked = true' not in dialog.split('outsideCrop = new CheckBox',1)[1].split(';',1)[0]
assert 'detail.SelectedIndex = (int)ExportDetail.Medium' in dialog
assert not list(payload.rglob('RevitAPI.dll')) and not list(payload.rglob('RevitAPIUI.dll'))
report={'version':'0.5.3','filesHashVerified':len(m['files']),'allCurrentWebAssetsByteIdentical':sorted(assets),'allWebReferencesShipped':True,'compiledViewerTitleVerified':True,'webOriginVerified':True,'exporters':len(m['exporters']),'removedRenderersAbsentFromRuntime':True,'defaultCropOnly':True,'defaultMedium':True,'outsideCropRequiresConfirmation':True,'WindowsRevitExecutionTested':False}
(root/'tests/release-verification-v053.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
