$ErrorActionPreference = "Stop"

$packageFolder = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExtension = Join-Path $packageFolder "EKScriptLauncher.extension"
$extensionRoot = Join-Path $env:APPDATA "pyRevit\Extensions"
$destinationExtension = Join-Path $extensionRoot "EKScriptLauncher.extension"
$desktopFolder = [Environment]::GetFolderPath("Desktop")
$scriptsFolder = Join-Path $desktopFolder "RevitScripts"

if (-not (Test-Path -LiteralPath $sourceExtension)) {
    throw "The EKScriptLauncher.extension folder is missing from this package."
}

New-Item -ItemType Directory -Force -Path $extensionRoot | Out-Null

if (Test-Path -LiteralPath $destinationExtension) {
    Remove-Item -LiteralPath $destinationExtension -Recurse -Force
}

Copy-Item -LiteralPath $sourceExtension -Destination $destinationExtension -Recurse -Force
New-Item -ItemType Directory -Force -Path $scriptsFolder | Out-Null

Write-Host ""
Write-Host "EK Script Launcher installed successfully." -ForegroundColor Green
Write-Host "Extension: $destinationExtension"
Write-Host "Scripts folder: $scriptsFolder"
Write-Host ""
Write-Host "In each open Revit version, use pyRevit > Reload once (or restart Revit)."
Write-Host "Then open the EK tab. It now contains Script Launcher and Quick Launch."
Write-Host ""
Read-Host "Press Enter to close"

