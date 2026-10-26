# Offline 3D PDF - replace the installed Revit exporter DLLs with 0.5.3.
# Usage: UPDATE.cmd (or this script with no arguments) to update,
#        ROLLBACK.cmd (-Rollback) to put the previous DLLs back.
# Only the exporter DLL that each Revit add-in manifest already points to is
# replaced. The viewer, the helper and the PDF format are not touched.
param([switch]$Rollback)
Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
$AddInId = '347FDBDE-BA6B-44D8-877F-9BFDA4CC2929'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Get-TargetFramework([string]$Dll) {
    # The TargetFrameworkAttribute string is stored as plain text in the metadata.
    $text = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($Dll))
    if ($text.Contains('.NETCoreApp,Version=v10.0')) { return 'net10.0-windows' }
    if ($text.Contains('.NETCoreApp,Version=v8.0')) { return 'net8.0-windows' }
    if ($text.Contains('.NETFramework,Version=v4.8')) { return 'net48' }
    return $null
}
function Get-Sha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) { throw 'Close every Revit window first, then run this again.' }

$sums = @{}
foreach ($line in Get-Content -LiteralPath ([IO.Path]::Combine($here, 'SHA256SUMS.txt'))) {
    if ($line -match '^([0-9a-f]{64})\s+\*?(.+)$') { $sums[$Matches[2].Replace('\', '/')] = $Matches[1] }
}

$appData = [Environment]::GetFolderPath('ApplicationData')
$changed = 0
foreach ($year in 2024..2027) {
    $manifest = [IO.Path]::Combine($appData, 'Autodesk', 'Revit', 'Addins', [string]$year, 'Offline3DPdfPrototype.addin')
    if (-not (Test-Path -LiteralPath $manifest)) { continue }
    $xml = New-Object xml; $xml.XmlResolver = $null; $xml.Load($manifest)
    $addIn = $xml.RevitAddIns.AddIn
    if ($addIn.AddInId -ne $AddInId) { Write-Warning ("Revit {0}: manifest is not Offline 3D PDF; skipped." -f $year); continue }
    $target = [string]$addIn.Assembly
    if (-not (Test-Path -LiteralPath $target)) { Write-Warning ("Revit {0}: installed exporter not found at {1}; skipped." -f $year, $target); continue }
    $backup = $target + '.v0.5.2.bak'
    $pdb = [IO.Path]::ChangeExtension($target, '.pdb')

    if ($Rollback) {
        if (Test-Path -LiteralPath $backup) {
            Copy-Item -LiteralPath $backup -Destination $target -Force
            if (Test-Path -LiteralPath $pdb) { Remove-Item -LiteralPath $pdb }
            Write-Host ("Revit {0}: previous exporter restored." -f $year); $changed++
        } else { Write-Host ("Revit {0}: no backup found; nothing to restore." -f $year) }
        continue
    }

    $framework = Get-TargetFramework $target
    if (-not $framework -and (Test-Path -LiteralPath $backup)) { $framework = Get-TargetFramework $backup }
    if (-not $framework) { Write-Warning ("Revit {0}: could not tell which .NET version the installed exporter uses; skipped." -f $year); continue }
    $relative = 'Exporters/' + $year + '-' + $framework + '/Offline3DPrototype.Revit' + $year + '.dll'
    $source = [IO.Path]::Combine($here, 'Exporters', ([string]$year + '-' + $framework), ('Offline3DPrototype.Revit' + $year + '.dll'))
    if (-not (Test-Path -LiteralPath $source)) { Write-Warning ("Revit {0}: no 0.5.3 build for {1}; skipped." -f $year, $framework); continue }
    if (-not $sums.ContainsKey($relative) -or $sums[$relative] -ne (Get-Sha256 $source)) { throw ('Checksum mismatch for ' + $relative + '. Download the package again.') }
    $helper = [IO.Path]::Combine((Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $target))), 'Viewer', 'Offline3D.FastPDF.Dev.exe')
    if (-not (Test-Path -LiteralPath $helper)) { Write-Warning ("Revit {0}: the viewer helper is not at {1}; exports need it. Reinstall Offline 3D PDF if exports report it missing." -f $year, $helper) }

    if (-not (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $target -Destination $backup }
    Copy-Item -LiteralPath $source -Destination $target -Force
    Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($source, '.pdb')) -Destination $pdb -Force
    foreach ($file in $target, $pdb) { try { Unblock-File -LiteralPath $file } catch { } }
    if ((Get-Sha256 $target) -ne $sums[$relative]) { Copy-Item -LiteralPath $backup -Destination $target -Force; throw ('Copy verification failed for Revit ' + $year + '; the previous exporter was restored.') }
    Write-Host ("Revit {0} ({1}): exporter updated to 0.5.3. Backup: {2}" -f $year, $framework, $backup); $changed++
}
if ($changed -eq 0) { Write-Host 'Nothing was changed. Is Offline 3D PDF installed for this Windows user?' }
elseif ($Rollback) { Write-Host ''; Write-Host 'Done. Start Revit: the previous exporter is back.' }
else { Write-Host ''; Write-Host 'Done. Start Revit: the ribbon panel now reads "Offline 3D PDF v0.5.3".' }
