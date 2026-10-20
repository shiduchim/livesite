# Builds EK Script Launcher for Revit 2024-2027 and assembles the installer ZIP.
# Needs the .NET 8 and .NET 10 SDKs (Windows). Output: script-launcher/out/
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "out"
$packageName = "EK_Script_Launcher_2024_2027"
$stage = Join-Path $out $packageName

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

foreach ($year in 2024, 2025, 2026, 2027) {
    $project = Join-Path $root "Revit$year\EKScriptLauncher.$year.csproj"
    $buildOut = Join-Path $out "build\$year"

    Write-Host "== Revit $year"
    dotnet build $project -c Release --nologo -v q -o $buildOut
    if ($LASTEXITCODE -ne 0) { throw "Build failed for Revit $year" }

    $dll = Join-Path $buildOut "EKScriptLauncher.dll"
    if (-not (Test-Path $dll)) { throw "EKScriptLauncher.dll missing for Revit $year" }

    # Revit's own API DLLs must not be shipped.
    $extra = Get-ChildItem $buildOut -Filter "RevitAPI*.dll"
    if ($extra) { throw "Revit API DLLs were copied to the output for Revit $year" }

    $target = Join-Path $stage "Revit$year"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item $dll $target
}

Get-ChildItem (Join-Path $root "package") -File | Copy-Item -Destination $stage

# Checksums of everything in the package.
$sums = Get-ChildItem $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($stage.Length + 1)
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$sums | Set-Content (Join-Path $stage "SHA256SUMS.txt") -Encoding ASCII

$zip = Join-Path $out "$packageName.zip"
Compress-Archive -Path $stage -DestinationPath $zip
Write-Host "Package: $zip"
