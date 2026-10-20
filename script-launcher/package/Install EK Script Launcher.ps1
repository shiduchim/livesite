$ErrorActionPreference = "Stop"

# EK Script Launcher installer (Revit 2024, 2025, 2026, 2027).
# Installs for the current Windows user only; no administrator rights needed.

$packageFolder = Split-Path -Parent $MyInvocation.MyCommand.Path
$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins"
$years = 2024, 2025, 2026, 2027

if (Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
    Write-Host ""
    Write-Host "Revit is open. Close every Revit window, then run the installer again." -ForegroundColor Yellow
    exit 1
}

# Install for the Revit versions on this computer. If none can be detected
# (for example a non-standard install folder), install for all four.
$targets = @($years | Where-Object {
    (Test-Path -LiteralPath (Join-Path $env:ProgramFiles "Autodesk\Revit $_\Revit.exe")) -or
    (Test-Path -LiteralPath (Join-Path $addinRoot "$_"))
})
if ($targets.Count -eq 0) {
    $targets = $years
}

foreach ($year in $targets) {
    $source = Join-Path $packageFolder "Revit$year\EKScriptLauncher.dll"
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Revit$year\EKScriptLauncher.dll is missing from this package. Extract the complete ZIP and try again."
    }
}

foreach ($year in $targets) {
    $yearFolder = Join-Path $addinRoot "$year"
    $installFolder = Join-Path $yearFolder "EKScriptLauncher"
    $dll = Join-Path $installFolder "EKScriptLauncher.dll"
    $manifest = Join-Path $yearFolder "EKScriptLauncher.addin"

    New-Item -ItemType Directory -Force -Path $installFolder | Out-Null
    Copy-Item -LiteralPath (Join-Path $packageFolder "Revit$year\EKScriptLauncher.dll") -Destination $dll -Force
    Unblock-File -LiteralPath $dll

    $assembly = [System.Security.SecurityElement]::Escape($dll)
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>EK Script Launcher</Name>
    <Assembly>$assembly</Assembly>
    <AddInId>542cfb2d-f7e9-44f7-88a2-73571e05de9b</AddInId>
    <FullClassName>EKScriptLauncher.Revit.App</FullClassName>
    <VendorId>EK</VendorId>
    <VendorDescription>EK</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
    [System.IO.File]::WriteAllText($manifest, $xml, (New-Object System.Text.UTF8Encoding($true)))
    Write-Host "Installed for Revit $year" -ForegroundColor Green
}

# The old pyRevit version would add a second EK tab. Move it aside (kept as a backup).
$oldExtension = Join-Path $env:APPDATA "pyRevit\Extensions\EKScriptLauncher.extension"
if (Test-Path -LiteralPath $oldExtension) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backup = Join-Path $env:LOCALAPPDATA "EK\ScriptLauncher\InstallBackups\$stamp"
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Move-Item -LiteralPath $oldExtension -Destination $backup
    Write-Host ""
    Write-Host "The old pyRevit version of EK Script Launcher was removed."
    Write-Host "Backup: $backup"
}

$scriptsFolder = Join-Path ([Environment]::GetFolderPath("Desktop")) "RevitScripts"
New-Item -ItemType Directory -Force -Path $scriptsFolder | Out-Null

Write-Host ""
Write-Host "EK Script Launcher installed successfully." -ForegroundColor Green
Write-Host "Default scripts folder: $scriptsFolder"
Write-Host "Your saved folders and settings were kept."
Write-Host ""
Write-Host "Open Revit and use the EK tab > Scripts panel."
Write-Host "pyRevit must stay installed: it is what runs the Python scripts."
