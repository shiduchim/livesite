param([switch]$BuildFromSource)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

Write-Host "EK Ribbon Builder $EkVersion" -ForegroundColor Cyan
Assert-RevitClosed
$revits = @(Get-RevitYears)
if ($revits.Count -eq 0) { throw 'No Revit 2024, 2025, 2026 or 2027 found under C:\Program Files\Autodesk.' }
Write-Host ('Revit found: ' + (($revits | ForEach-Object Year) -join ', '))

# Stop before changing anything if another registration would load a second copy.
foreach ($r in $revits) {
  $conflicts = @(Get-EkRegistrations $r.Year | Where-Object { $_.Scope -eq 'Machine' -or -not $_.Owned })
  if ($conflicts.Count) { throw ('Remove these EK Ribbon registrations first, nothing was installed: ' + (($conflicts | ForEach-Object Path) -join '; ')) }
}

$package = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $env:TEMP ('EKRibbon_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
  # 1. Get a DLL per year: the prebuilt one, or compile it here.
  foreach ($r in $revits) {
    $dll = Join-Path $package "Bin\$($r.Year)\EKRibbon.dll"
    if ($BuildFromSource -or -not (Test-Path -LiteralPath $dll)) {
      if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Building from source needs the .NET 10 SDK (dotnet).' }
      Write-Host "  Building for Revit $($r.Year)..."
      $env:REVIT_INSTALL_DIR = $r.Dir
      $out = Join-Path $stage $r.Year
      & dotnet build (Join-Path $package 'Source\EKRibbon.csproj') -c Release "-p:RevitYear=$($r.Year)" "-p:OutputPath=$out\" --nologo -v q
      if ($LASTEXITCODE -ne 0) { throw "Build failed for Revit $($r.Year). Nothing was installed." }
      $dll = Join-Path $out 'EKRibbon.dll'
    }
    $version = [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3)
    if ($version -ne $EkVersion) { throw "Unexpected DLL version $version for Revit $($r.Year)." }
    $r | Add-Member NoteProperty Dll $dll
  }

  # 2. Back up what is there now: layout, icons stay untouched in place.
  $backup = Join-Path $EkRoot ('Backups\Install_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
  New-Item -ItemType Directory -Force -Path $backup | Out-Null
  foreach ($name in 'layout_shared.json', 'selected_commands.json') {
    $file = Join-Path $EkRoot $name
    if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination $backup }
  }

  # 3. Install DLL + manifest per year.
  foreach ($r in $revits) {
    $year = $r.Year
    $target = Join-Path $EkRoot "Bin\$year\EKRibbon.dll"
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination (Join-Path $backup "EKRibbon_$year.dll") }
    Copy-Item -LiteralPath $r.Dll -Destination $target -Force
    # Downloaded files carry an Internet mark that makes Revit 2024 refuse to load them.
    Unblock-File -LiteralPath $target
    if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $r.Dll).Hash) { throw "Copy of the Revit $year DLL is corrupt." }

    $manifest = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year\EKRibbon.addin"
    foreach ($entry in @(Get-EkRegistrations $year | Where-Object { $_.Path -ine $manifest })) {
      Copy-Item -LiteralPath $entry.Path -Destination (Join-Path $backup ("duplicate_${year}_" + (Split-Path $entry.Path -Leaf)))
      Remove-EkEntries $entry.Path
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $manifest) | Out-Null
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>EK Ribbon Builder</Name>
    <Assembly>$([Security.SecurityElement]::Escape($target))</Assembly>
    <AddInId>$EkId</AddInId>
    <FullClassName>EKRibbon.Application</FullClassName>
    <VendorId>EKTO</VendorId>
    <VendorDescription>EK Ribbon Builder - your own EK Custom tab</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
    [IO.File]::WriteAllText($manifest, $xml, [Text.UTF8Encoding]::new($false))
    Write-Host "  Revit ${year}: installed" -ForegroundColor Green
  }
}
finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host ''
Write-Host 'Done. Start Revit: the EK Custom tab keeps your saved layout.' -ForegroundColor Green
Write-Host "Backup: $backup"
