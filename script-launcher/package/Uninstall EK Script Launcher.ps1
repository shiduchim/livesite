$ErrorActionPreference = "Stop"

# Removes EK Script Launcher from Revit 2024-2027 for the current user.
# Your scripts folders and saved settings are kept.

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins"

if (Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
    Write-Host ""
    Write-Host "Revit is open. Close every Revit window, then run the uninstaller again." -ForegroundColor Yellow
    exit 1
}

$removed = 0
foreach ($year in 2024, 2025, 2026, 2027) {
    $manifest = Join-Path $addinRoot "$year\EKScriptLauncher.addin"
    $installFolder = Join-Path $addinRoot "$year\EKScriptLauncher"

    if (Test-Path -LiteralPath $manifest) {
        Remove-Item -LiteralPath $manifest -Force
    }
    if (Test-Path -LiteralPath $installFolder) {
        Remove-Item -LiteralPath $installFolder -Recurse -Force
        Write-Host "Removed from Revit $year" -ForegroundColor Green
        $removed++
    }
}

if ($removed -eq 0) {
    Write-Host "EK Script Launcher was not installed for this Windows user."
}

Write-Host ""
Write-Host "Your scripts folders and saved settings were kept."
