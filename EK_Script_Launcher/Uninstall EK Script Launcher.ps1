$ErrorActionPreference = "Stop"

$destinationExtension = Join-Path $env:APPDATA "pyRevit\Extensions\EKScriptLauncher.extension"

if (Test-Path -LiteralPath $destinationExtension) {
    Remove-Item -LiteralPath $destinationExtension -Recurse -Force
    Write-Host "EK Script Launcher removed." -ForegroundColor Green
} else {
    Write-Host "EK Script Launcher was not installed in the standard pyRevit extensions folder."
}

Write-Host "Your Desktop\RevitScripts folder and all of your scripts were kept."
Write-Host "Use pyRevit > Reload in open Revit versions, or restart Revit."
Write-Host ""
Read-Host "Press Enter to close"

