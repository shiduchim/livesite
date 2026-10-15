# EKTreeGlyphProbe2024 - isolated single-user first-test installer / restore.
# Revit 2024 only. Refuses to overwrite anything already present.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Install','Restore')][string]$Action)
$ErrorActionPreference = 'Stop'

$addonDir = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024'
$name = 'EKTreeGlyphProbe2024'
$manifest = Join-Path $addonDir ($name + '.addin')
$target = Join-Path $addonDir $name
$marker = Join-Path $target 'EKTreeGlyphProbe2024.owner'
$uuid = '6F0C2E7B-6B0E-4C58-9E1B-3C1A3D9F2E41'
$source = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $source 'dist\EKGlyph'
$stateDir = Join-Path $env:LOCALAPPDATA 'EKTreeGlyphProbe2024'
$state = Join-Path $stateDir 'install-state.json'

function Test-ManifestOwned {
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { return $false }
    $content = [IO.File]::ReadAllText($manifest)
    return ($content.Contains($uuid) -and $content.Contains('EKTreeGlyphProbe2024\EKGlyph.dll'))
}
function Check-RevitStopped {
    if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
        throw 'Close ALL Revit windows before installing or restoring this test add-in.'
    }
}
function Install {
    Check-RevitStopped
    foreach ($file in @('EKGlyph.dll','EKGlyphHook.dll','glyph.ini')) {
        if (-not (Test-Path -LiteralPath (Join-Path $dist $file) -PathType Leaf)) {
            throw ('Missing build output: '+$file+'. Run BUILD_ONLY.cmd first.')
        }
    }
    if ((Test-Path -LiteralPath $manifest) -or (Test-Path -LiteralPath $target)) {
        throw ('Test add-in already exists: '+$name+'. Refusing to overwrite it. Run RESTORE_REVT_2024.cmd first.')
    }
    New-Item -ItemType Directory -Force -Path $addonDir | Out-Null
    New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
    $stage = Join-Path $addonDir ($name+'.staging-'+[guid]::NewGuid().ToString('N'))
    $tempManifest = Join-Path $addonDir ($name+'.addin.staging-'+[guid]::NewGuid().ToString('N'))
    $createdTarget = $false
    $createdManifest = $false
    try {
        Copy-Item -LiteralPath $dist -Destination $stage -Recurse -ErrorAction Stop
        [IO.File]::WriteAllText((Join-Path $stage 'EKTreeGlyphProbe2024.owner'),
            'Created by EKTreeGlyphProbe2024 isolated installer; safe to uninstall with RESTORE_Revit2024.cmd.')
        foreach ($file in @('EKGlyph.dll','EKGlyphHook.dll','glyph.ini')) {
            $srcHash = (Get-FileHash -LiteralPath (Join-Path $dist $file) -Algorithm SHA256).Hash
            $dstHash = (Get-FileHash -LiteralPath (Join-Path $stage $file) -Algorithm SHA256).Hash
            if ($srcHash -ne $dstHash) { throw ('Copy verification failed for '+$file) }
        }
        # Revit add-in manifest must point to the absolute installed assembly location.
        # The user-specific folder is the only destination; Autodesk program files are untouched.
        $dllPath = Join-Path $target 'EKGlyph.dll'
        $xmlPath = [System.Security.SecurityElement]::Escape($dllPath)
        $addinText = '<?xml version="1.0" encoding="utf-8"?>' + "`r`n" +
        '<RevitAddIns><AddIn Type="Application">' +
        '<Name>EK Tree Glyph Probe 2024</Name>' +
        '<Assembly>' + $xmlPath + '</Assembly>' +
        '<FullClassName>EKGlyph.App</FullClassName>' +
        '<AddInId>' + $uuid + '</AddInId>' +
        '<VendorId>EKHD</VendorId>' +
        '<VendorDescription>Temporary reversible test of a tree glyph painter</VendorDescription>' +
        '</AddIn></RevitAddIns>'
        [IO.File]::WriteAllText($tempManifest,$addinText,[Text.Encoding]::UTF8)
        # Publish folder first. Publish manifest LAST so no partial folder is visible to Revit.
        Move-Item -LiteralPath $stage -Destination $target -ErrorAction Stop
        $createdTarget = $true
        Move-Item -LiteralPath $tempManifest -Destination $manifest -ErrorAction Stop
        $createdManifest = $true
        $details = [ordered]@{Installed=(Get-Date).ToString('o'); Assembly=$dllPath; Folder=$target;
          Manifest=$manifest; Note='Isolated test files only. No existing app was overwritten.'}
        $details | ConvertTo-Json | Set-Content -LiteralPath $state -Encoding UTF8
        Write-Host 'SUCCESS: Test add-in installed for Revit 2024 only.' -ForegroundColor Green
        Write-Host ('Undo: double-click RESTORE_Revit2024.cmd (with Revit closed).')
        Write-Host ('Log: '+(Join-Path $env:LOCALAPPDATA 'EKGlyph\EKGlyph.log'))
    } catch {
        $originalError = $_
        if ($createdManifest -and (Test-ManifestOwned)) { Remove-Item -LiteralPath $manifest -Force }
        if ($createdTarget -and (Test-Path -LiteralPath $marker)) { Remove-Item -LiteralPath $target -Recurse -Force }
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
        if (Test-Path -LiteralPath $tempManifest) { Remove-Item -LiteralPath $tempManifest -Force }
        throw $originalError
    }
}
function Restore {
    Check-RevitStopped
    $manifestFound = Test-Path -LiteralPath $manifest
    $folderFound = Test-Path -LiteralPath $target
    if ($manifestFound -and -not (Test-ManifestOwned)) {
        throw 'A different manifest occupies the test name. Not touching it.'
    }
    if ($folderFound -and -not (Test-Path -LiteralPath $marker)) {
        throw 'Test folder has no ownership marker. Not deleting it.'
    }
    if ($manifestFound) { Remove-Item -LiteralPath $manifest -Force -ErrorAction Stop }
    if ($folderFound) { Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop }
    if (Test-Path -LiteralPath $state) { Remove-Item -LiteralPath $state -Force }
    Write-Host 'RESTORED: EK tree glyph test add-in removed for Revit 2024.' -ForegroundColor Green
    Write-Host 'No Autodesk program DLLs or Windows theme files were modified.'
}
if ($Action -eq 'Install') { Install } else { Restore }
