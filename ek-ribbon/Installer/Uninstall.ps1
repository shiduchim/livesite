$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-RevitClosed
$backup = Join-Path $EkRoot ('Backups\Uninstall_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
foreach ($year in $EkYears) {
  foreach ($entry in @(Get-EkRegistrations $year | Sort-Object Path -Unique)) {
    if (-not $entry.Owned -or $entry.Scope -ne 'User') { Write-Warning "Left in place, please check by hand: $($entry.Path)"; continue }
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Copy-Item -LiteralPath $entry.Path -Destination (Join-Path $backup ("${year}_" + (Split-Path $entry.Path -Leaf)))
    Remove-EkEntries $entry.Path
    Write-Host "Revit ${year}: removed"
  }
}
Write-Host "Your layout and icons are kept in $EkRoot, so reinstalling brings the tab back as it was."
