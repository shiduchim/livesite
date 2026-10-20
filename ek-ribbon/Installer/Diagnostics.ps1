# Read-only: reports the install per Revit year and zips the logs to the Desktop.
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'Common.ps1')
$lines = New-Object System.Collections.Generic.List[string]
function Say([string]$text, [string]$color = 'Gray') { Write-Host $text -ForegroundColor $color; $lines.Add($text) }

Say "EK Ribbon Builder diagnostics, $(Get-Date -Format s)" Cyan
foreach ($year in $EkYears) {
  if (-not (Test-Path -LiteralPath "C:\Program Files\Autodesk\Revit $year\RevitAPI.dll")) { Say "Revit ${year}: not installed" DarkGray; continue }
  $dll = Join-Path $EkRoot "Bin\$year\EKRibbon.dll"
  $regs = @(Get-EkRegistrations $year)
  $problem = $null
  if (-not (Test-Path -LiteralPath $dll)) { $problem = 'DLL missing - run INSTALL.cmd' }
  elseif ($regs.Count -eq 0) { $problem = 'not registered - run INSTALL.cmd' }
  elseif ($regs.Count -gt 1) { $problem = 'registered more than once: ' + (($regs | ForEach-Object Path) -join '; ') }
  elseif ($regs[0].Assembly -ine $dll) { $problem = "manifest points to $($regs[0].Assembly)" }
  elseif (Get-Item -LiteralPath $dll -Stream Zone.Identifier -ErrorAction SilentlyContinue) { $problem = 'DLL is blocked as downloaded - run INSTALL.cmd again' }
  if ($problem) { Say "Revit ${year}: $problem" Red }
  else { Say "Revit ${year}: OK, version $([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3))" Green }
  $startup = Join-Path $EkRoot "startup_$year.txt"
  if (Test-Path -LiteralPath $startup) { Get-Content -LiteralPath $startup | ForEach-Object { Say "    $_" } }
}

$out = Join-Path ([Environment]::GetFolderPath('Desktop')) ('EKRibbon_Diagnostics_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Path $out | Out-Null
$lines | Set-Content -LiteralPath (Join-Path $out 'report.txt') -Encoding UTF8
foreach ($pattern in 'startup_*.txt', 'scan_*.txt', 'last-error.log*', 'layout_shared.json', 'catalog_*.json') {
  Get-ChildItem -LiteralPath $EkRoot -Filter $pattern -File -ErrorAction SilentlyContinue | Copy-Item -Destination $out
}
Compress-Archive -Path (Join-Path $out '*') -DestinationPath "$out.zip"
Remove-Item -LiteralPath $out -Recurse -Force
Say ''
Say "Saved $out.zip (no model files, but paths may include your Windows user name)." Cyan
