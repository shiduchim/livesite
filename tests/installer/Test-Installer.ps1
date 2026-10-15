<#
  End-to-end test of EK_Triangle_Setup.exe on a real Windows machine (CI).

  Builds simulated Revit 2024 / 2025 / 2027 installations (2026 absent) with
  synthetic Project Browser bundles, an unrelated add-in, the experimental
  EKTreeGlyphProbe2024 add-in, and runs the real installer and uninstaller
  silently through install / reinstall / upgrade / uninstall cycles.

  WARNING: modifies C:\Program Files\Autodesk\Revit 20xx, %APPDATA%\Autodesk
  and C:\ProgramData\EKTriangle. Run only on a disposable CI machine.
#>
param(
    [Parameter(Mandatory = $true)][string]$Setup,
    [Parameter(Mandatory = $true)][string]$Fixtures,
    [Parameter(Mandatory = $true)][string]$FakeRevitExe,
    [string]$ResultDir = (Join-Path $PSScriptRoot 'results')
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $ResultDir | Out-Null
$script:failed = 0
$script:passed = 0
$report = New-Object System.Collections.Generic.List[string]

function Ok([bool]$cond, [string]$what) {
    if ($cond) { $script:passed++; $line = "PASS $what"; Write-Host $line -ForegroundColor Green }
    else { $script:failed++; $line = "FAIL $what"; Write-Host $line -ForegroundColor Red }
    $report.Add($line)
}
function Section([string]$name) { Write-Host "`n=== $name" -ForegroundColor Cyan; $report.Add("`n=== $name") }
function Hash([string]$p) { if (Test-Path -LiteralPath $p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash } else { 'MISSING' } }

$AppId = '{6FC522F7-EAC3-427C-9E28-5613722867E8}_is1'
$UninstKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppId"
$AppDir = Join-Path $env:LOCALAPPDATA 'Programs\EK Triangle'
$OurId = 'D24DC5EF-6D2B-4013-B5D0-37B8F2763892'
$Store = Join-Path $env:ProgramData 'EKTriangle'

function RevitDir([int]$y) { Join-Path $env:ProgramFiles "Autodesk\Revit $y" }
function MainJs([int]$y) { Join-Path (RevitDir $y) 'AddIns\ProjectBrowser\resource\static\js\main.js' }
function Addins([int]$y) { Join-Path $env:APPDATA "Autodesk\Revit\Addins\$y" }
function Manifest([int]$y) { Join-Path (Addins $y) 'EKTriangle.addin' }
function IsPatched([int]$y) { (Get-Content -LiteralPath (MainJs $y) -Raw).Contains('M6 4L10 8L6 12Z') }

$runIndex = 0
function RunSetup([string[]]$extra) {
    $script:runIndex++
    $log = Join-Path $ResultDir ("setup-$script:runIndex.log")
    $argList = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$log") + $extra
    $p = Start-Process -FilePath $Setup -ArgumentList $argList -Wait -PassThru
    return $p.ExitCode
}
function RunUninstall {
    $script:runIndex++
    $log = Join-Path $ResultDir ("uninstall-$script:runIndex.log")
    $cmd = (Get-ItemProperty $UninstKey).UninstallString.Trim('"')
    $p = Start-Process -FilePath $cmd -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$log" -Wait -PassThru
    for ($i = 0; $i -lt 30 -and (Test-Path $UninstKey); $i++) { Start-Sleep -Seconds 1 }
    Start-Sleep -Seconds 2
    Write-Host "--- uninstall log ($log), exit $($p.ExitCode):"
    if (Test-Path $log) { Get-Content $log | ForEach-Object { Write-Host "    $_" } }
    Get-Process -Name Revit -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "    running: Revit pid $($_.Id) $($_.Path)" }
    return $p.ExitCode
}

# ---------------------------------------------------------------- arrange --
Section 'Simulated machine'
$orig = @{}
foreach ($spec in @(@{Y = 2024; F = 'main-original.js' }, @{Y = 2025; F = 'main-unsupported.js' }, @{Y = 2027; F = 'main-original-bom.js' })) {
    $y = $spec.Y
    New-Item -ItemType Directory -Force -Path (Split-Path (MainJs $y)) | Out-Null
    Copy-Item $FakeRevitExe (Join-Path (RevitDir $y) 'Revit.exe') -Force
    Copy-Item (Join-Path $Fixtures $spec.F) (MainJs $y) -Force
    Set-Content (Join-Path (Split-Path (MainJs $y)) 'vendor.js') '/* other Autodesk file */'
    $orig[$y] = Hash (MainJs $y)
    New-Item -ItemType Directory -Force -Path (Join-Path (Addins $y) 'OtherVendor') | Out-Null
    Set-Content (Join-Path (Addins $y) 'OtherVendor.addin') '<?xml version="1.0"?><RevitAddIns><AddIn Type="Application"><Name>Other</Name><Assembly>OtherVendor\Other.dll</Assembly><FullClassName>Other.App</FullClassName><AddInId>11111111-2222-3333-4444-555555555555</AddInId><VendorId>OTHR</VendorId></AddIn></RevitAddIns>'
    Set-Content (Join-Path (Addins $y) 'OtherVendor\Other.dll') 'other'
}
# The experimental add-in, exactly as its installer laid it out.
$probeDir = Join-Path (Addins 2024) 'EKTreeGlyphProbe2024'
New-Item -ItemType Directory -Force -Path $probeDir | Out-Null
foreach ($f in 'EKGlyph.dll', 'EKGlyphHook.dll', 'glyph.ini') { Set-Content (Join-Path $probeDir $f) "legacy $f" }
Set-Content (Join-Path $probeDir 'EKTreeGlyphProbe2024.owner') 'Created by EKTreeGlyphProbe2024 isolated installer'
$probeManifest = Join-Path (Addins 2024) 'EKTreeGlyphProbe2024.addin'
Set-Content $probeManifest ('<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>EK Tree Glyph Probe 2024</Name><Assembly>' +
    (Join-Path $probeDir 'EKGlyph.dll') + '</Assembly><FullClassName>EKGlyph.App</FullClassName><AddInId>6F0C2E7B-6B0E-4C58-9E1B-3C1A3D9F2E41</AddInId><VendorId>EKHD</VendorId></AddIn></RevitAddIns>')
$otherBefore = @{}
foreach ($y in 2024, 2025, 2027) { $otherBefore[$y] = Hash (Join-Path (Addins $y) 'OtherVendor.addin') }
Ok (-not (Test-Path $UninstKey)) 'no EK Triangle installed at start'

# --------------------------------------------------------- Revit running --
Section 'Install is blocked while Revit runs'
$fake = Start-Process -FilePath (Join-Path (RevitDir 2024) 'Revit.exe') -ArgumentList 'sleep', '600' -PassThru
Start-Sleep -Seconds 2
$code = RunSetup @()
Ok ($code -ne 0) "setup refused while Revit 2024 runs (exit $code)"
Ok (-not (Test-Path (Manifest 2024))) 'no add-in written while Revit runs'
Ok ((Hash (MainJs 2024)) -eq $orig[2024]) 'main.js untouched while Revit runs'
Stop-Process -Id $fake.Id -Force; Start-Sleep -Seconds 1

# --------------------------------------------------------------- install --
Section 'First install (all detected versions; Revit 2027 main.js read-only)'
Set-ItemProperty -LiteralPath (MainJs 2027) -Name IsReadOnly -Value $true
$code = RunSetup @()
Ok ($code -eq 0) "setup exit code $code"
Ok (Test-Path $UninstKey) 'registered in Installed apps (HKCU uninstall key)'
$u = Get-ItemProperty $UninstKey
Ok ($u.DisplayName -eq 'EK Triangle') "DisplayName '$($u.DisplayName)'"
Ok ($u.Publisher -eq 'EK') "Publisher '$($u.Publisher)'"
Ok ($u.DisplayVersion -like '1.*') "DisplayVersion '$($u.DisplayVersion)'"
foreach ($f in 'EKTriangle.exe', 'glyph.ini', 'README.txt', 'Revit2024\EKTriangle.dll', 'Revit2024\EKTriangleHook.dll', 'Revit2027\EKTriangle.dll') {
    Ok (Test-Path (Join-Path $AppDir $f)) "installed $f"
}
foreach ($y in 2024, 2025, 2027) {
    Ok ((Test-Path (Manifest $y)) -and (Get-Content (Manifest $y) -Raw).Contains($OurId)) "Revit $y add-in manifest installed"
}
Ok (-not (Test-Path (Manifest 2026))) 'no manifest for absent Revit 2026'
Ok (IsPatched 2024) 'Revit 2024 Project Browser patched'
Ok ((Hash (MainJs 2027)) -eq $orig[2027]) 'read-only Revit 2027 main.js reported and left untouched'
Set-ItemProperty -LiteralPath (MainJs 2027) -Name IsReadOnly -Value $false
Ok ((Hash (MainJs 2025)) -eq $orig[2025]) 'Revit 2025 unsupported main.js left untouched'
Ok ((Get-ChildItem (Join-Path $Store 'Browser\Revit2024') -Filter '*.original' | ForEach-Object { Hash $_.FullName }) -contains $orig[2024]) 'verified original backup for 2024'
Ok (Test-Path (Join-Path $Store 'EK Triangle Restore.exe')) 'emergency restore tool placed in ProgramData'
Ok (-not (Test-Path $probeManifest)) 'experimental probe manifest deactivated'
Ok ((Get-ChildItem (Join-Path $env:LOCALAPPDATA 'EKTriangle\LegacyBackup') -Recurse -Filter 'EKGlyphHook.dll').Count -eq 1) 'experimental probe kept as backup'
foreach ($y in 2024, 2025, 2027) { Ok ((Hash (Join-Path (Addins $y) 'OtherVendor.addin')) -eq $otherBefore[$y]) "Revit $y unrelated add-in untouched" }
Ok ((Get-ChildItem (Split-Path (MainJs 2024)) -Filter 'main.js.EKTriangle.*').Count -eq 0) 'no temporary files left in Revit folder'
$patched2024 = Hash (MainJs 2024)

# ------------------------------------------------------------- reinstall --
Section 'Reinstall over itself (completes the partial install)'
$code = RunSetup @()
Ok ($code -eq 0) "reinstall exit code $code"
Ok (IsPatched 2027) 'Revit 2027 Project Browser patched on reinstall (BOM file)'
Ok ((Hash (MainJs 2024)) -eq $patched2024) 'Project Browser unchanged by reinstall'
Ok ((Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' | Where-Object { (Get-ItemProperty $_.PSPath).DisplayName -eq 'EK Triangle' }).Count -eq 1) 'exactly one Installed apps entry'
Ok ((Get-ChildItem (Addins 2024) -Filter '*.addin' | Where-Object { (Get-Content $_.FullName -Raw).Contains($OurId) }).Count -eq 1) 'exactly one EK Triangle manifest for 2024'
Ok ((Get-ChildItem (Join-Path $Store 'Browser\Revit2024') -Filter '*.original').Count -eq 1) 'backup not duplicated'

# ---------------------------------------------- upgrade with deselection --
Section 'Upgrade/modify: keep only Revit 2024'
$code = RunSetup @('/YEARS=2024')
Ok ($code -eq 0) "modify exit code $code"
Ok (-not (Test-Path (Manifest 2027))) 'Revit 2027 add-in removed when deselected'
Ok ((Hash (MainJs 2027)) -eq $orig[2027]) 'Revit 2027 Project Browser restored exactly when deselected'
Ok (IsPatched 2024) 'Revit 2024 still patched'
$code = RunSetup @()
Ok ((IsPatched 2027) -and (Test-Path (Manifest 2027))) 'Revit 2027 re-added'

# ---------------------------------------- uninstall blocked by Revit -----
Section 'Uninstall is blocked while Revit runs'
$fake = Start-Process -FilePath (Join-Path (RevitDir 2027) 'Revit.exe') -ArgumentList 'sleep', '600' -PassThru
Start-Sleep -Seconds 2
$code = RunUninstall
Ok (Test-Path $UninstKey) "uninstall refused while Revit 2027 runs (exit $code)"
Ok ((IsPatched 2024) -and (Test-Path (Manifest 2024))) 'nothing removed while Revit runs'
Stop-Process -Id $fake.Id -Force; Start-Sleep -Seconds 1

# ----------------------------------------------- Autodesk update + uninstall --
Section 'Uninstall after an Autodesk update of one version'
Copy-Item (Join-Path $Fixtures 'main-autodesk-update.js') (MainJs 2027) -Force
$updated2027 = Hash (MainJs 2027)
$code = RunUninstall
Ok (-not (Test-Path $UninstKey)) "uninstalled (exit $code)"
Ok ((Hash (MainJs 2024)) -eq $orig[2024]) 'Revit 2024 Project Browser restored byte-for-byte'
Ok ((Hash (MainJs 2027)) -eq $updated2027) 'Autodesk-updated 2027 file NOT overwritten by old backup'
Ok ((Hash (MainJs 2025)) -eq $orig[2025]) 'Revit 2025 still untouched'
foreach ($y in 2024, 2025, 2027) { Ok (-not (Test-Path (Manifest $y))) "Revit $y add-in removed" }
foreach ($y in 2024, 2025, 2027) { Ok ((Hash (Join-Path (Addins $y) 'OtherVendor.addin')) -eq $otherBefore[$y]) "Revit $y unrelated add-in untouched" }
Ok (-not (Test-Path (Join-Path $AppDir 'EKTriangle.exe'))) 'program files removed'
Ok ((Get-ChildItem (Join-Path $Store 'Browser\Revit2024') -Filter '*.original').Count -ge 1) 'original backups preserved after uninstall'
Ok (Test-Path (Join-Path (Split-Path (MainJs 2024)) 'vendor.js')) 'other Autodesk files untouched'
Copy-Item (Join-Path $Fixtures 'main-original-bom.js') (MainJs 2027) -Force

# --------------------------------------------------- repeated cycles --
Section 'Repeated install/uninstall cycles'
for ($c = 1; $c -le 3; $c++) {
    $code = RunSetup @()
    Ok (($code -eq 0) -and (IsPatched 2024) -and (IsPatched 2027)) "cycle $c install"
    $code = RunUninstall
    Ok ((-not (Test-Path $UninstKey)) -and (Hash (MainJs 2024)) -eq $orig[2024] -and (Hash (MainJs 2027)) -eq $orig[2027]) "cycle $c uninstall restored originals"
    Ok (-not (Test-Path (Manifest 2024))) "cycle $c manifests removed"
}

# ------------------------------------------- emergency restore tool --
Section 'Emergency restore without the installed app'
$code = RunSetup @()
$restoreExe = Join-Path $Store 'EK Triangle Restore.exe'
$out = Join-Path $ResultDir 'emergency.ini'
$p = Start-Process -FilePath $restoreExe -ArgumentList 'browser-restore', '--out', "`"$out`"" -Wait -PassThru
Ok ($p.ExitCode -eq 0) "emergency browser-restore exit $($p.ExitCode)"
Ok (((Hash (MainJs 2024)) -eq $orig[2024]) -and ((Hash (MainJs 2027)) -eq $orig[2027])) 'emergency tool restored originals'
$p = Start-Process -FilePath $restoreExe -ArgumentList 'uninstall', '--out', "`"$out`"" -Wait -PassThru
Ok (-not (Test-Path (Manifest 2024))) 'emergency tool removed add-in manifests'
$code = RunUninstall
Ok (-not (Test-Path $UninstKey)) 'uninstall after emergency restore succeeds'

# -------------------------------------------------- no Revit at all --
Section 'Clean state'
Ok ((Get-ChildItem (Addins 2024) -Filter '*.addin' | Where-Object { (Get-Content $_.FullName -Raw).Contains($OurId) }).Count -eq 0) 'no EK Triangle manifests remain'
Ok (-not (Test-Path $AppDir) -or ((Get-ChildItem $AppDir -Recurse -File).Count -eq 0)) 'no stale program files'

$summary = "`n$script:passed passed, $script:failed failed"
$report.Add($summary)
$report | Set-Content (Join-Path $ResultDir 'installer-e2e.txt') -Encoding UTF8
Write-Host $summary
if ($script:failed) { exit 1 }
exit 0
