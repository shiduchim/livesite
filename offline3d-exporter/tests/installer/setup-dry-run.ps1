# Dry run of the EKPDF setup functions on extracted packages (PowerShell 7 on any OS; no install).
# Usage: extract EKPDF-<v>.zip into <root>/reader and EKPDF-RevitAddin-<v>.zip into <root>/addin
# (their top folders renamed), then: pwsh setup-dry-run.ps1 -Root <root>
# The last check modifies one payload file on purpose; re-extract before reuse.
param([string]$Root)
$ErrorActionPreference='Stop'
$env:LOCALAPPDATA=Join-Path $Root 'localappdata'
. (Join-Path $Root 'reader/scripts/Setup.Common.ps1')
$r=Test-Payload (Join-Path $Root 'reader/payload') 'EKPDF' @('Viewer/Offline3D.FastPDF.Dev.exe')
Write-Host ('PASS reader payload verified: '+$r.files.Count+' files, version '+$r.version)
$a=Test-Payload (Join-Path $Root 'addin/payload') 'EKPDF-RevitAddin' @()
Write-Host ('PASS add-in payload verified: '+$a.files.Count+' files, exporters '+(($a.exporters|%{''+$_.year+'/'+$_.framework}) -join ', '))
try{Test-Payload (Join-Path $Root 'addin/payload') 'EKPDF' @() | Out-Null;throw 'wrong product accepted'}catch{if("$_" -notmatch 'Invalid application package'){throw};Write-Host 'PASS wrong product is refused'}
$m=Join-Path $Root 'test.addin';Write-AddinManifest $m 'C:\x\Offline3DPrototype.Revit2026.dll'
if(-not (Test-OwnedManifest $m)){throw 'manifest not owned'};Write-Host ('PASS add-in manifest written and recognised: '+([xml](Get-Content $m)).RevitAddIns.AddIn.Name)
try{Get-SafeChild $Root '../escape' | Out-Null;throw 'escape accepted'}catch{if("$_" -notmatch 'Unsafe'){throw};Write-Host 'PASS unsafe package path refused'}
# Tamper: change one byte, expect failure.
$f=Join-Path $Root 'addin/payload/THIRD-PARTY-NOTICES.md';Add-Content $f ' '
try{Test-Payload (Join-Path $Root 'addin/payload') 'EKPDF-RevitAddin' @() | Out-Null;throw 'tamper accepted'}catch{if("$_" -notmatch 'verification failed'){throw};Write-Host 'PASS modified payload file is refused'}
