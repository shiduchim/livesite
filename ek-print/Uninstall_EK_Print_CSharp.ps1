$ErrorActionPreference = 'SilentlyContinue'
if (Get-Process Revit -ErrorAction SilentlyContinue) {
    Write-Host 'Close all Revit windows first.' -ForegroundColor Yellow
    Read-Host 'Press Enter to close'
    exit 1
}
foreach ($year in @(2024,2025,2026,2027)) {
    $manifest = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year\EKPrint.addin"
    Remove-Item -LiteralPath $manifest -Force -ErrorAction SilentlyContinue
}
$bin = Join-Path $env:APPDATA 'EKPrintCSharp\Bin'
$tools = Join-Path $env:APPDATA 'EKPrintCSharp\Tools'
Remove-Item -LiteralPath $bin -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $tools -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $env:TEMP 'EKPrint') -Recurse -Force -ErrorAction SilentlyContinue
$packageRoots = @()
if ($env:NUGET_PACKAGES) { $packageRoots += $env:NUGET_PACKAGES }
$packageRoots += (Join-Path $env:USERPROFILE '.nuget\packages')
foreach ($root in ($packageRoots | Select-Object -Unique)) {
    if (Test-Path -LiteralPath $root) {
        Get-ChildItem -LiteralPath $root -Directory -Filter 'pdfsharp*' -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
foreach ($cache in @((Join-Path $env:LOCALAPPDATA 'NuGet\v3-cache'), (Join-Path $env:LOCALAPPDATA 'NuGet\http-cache'))) {
    if (Test-Path -LiteralPath $cache) {
        Get-ChildItem -LiteralPath $cache -Recurse -File -Filter '*pdfsharp*' -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
    }
}
Write-Host 'EK Print C# add-in files removed (including the old PDF merge helper and its PDFsharp downloads).' -ForegroundColor Green
Write-Host 'Your saved EK Print settings were kept.'
Read-Host 'Press Enter to close'
