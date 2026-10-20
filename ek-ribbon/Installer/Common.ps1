# Shared by Install, Uninstall and Diagnostics.
$EkId = 'C6074EC5-BC4D-4C4D-846E-F84F3C597B12'
$EkVersion = '2.0.1'
$EkRoot = Join-Path $env:APPDATA 'EKRibbonCSharp'
$EkYears = 2024..2027

function Get-RevitYears {
  foreach ($year in $EkYears) {
    $dir = "C:\Program Files\Autodesk\Revit $year"
    if (Test-Path -LiteralPath (Join-Path $dir 'RevitAPI.dll')) { [pscustomobject]@{ Year = $year; Dir = $dir } }
  }
}

function Assert-RevitClosed {
  $running = @(Get-Process -Name Revit -ErrorAction SilentlyContinue)
  if ($running.Count -gt 0) { throw 'Close every Revit window first (Revit.exe is still running).' }
}

function Test-EkEntry($entry) {
  [string]$entry.AddInId -ieq $EkId -and [string]$entry.FullClassName -ceq 'EKRibbon.Application'
}

# Every .addin entry that registers EK Ribbon for a Revit year, user or machine wide.
function Get-EkRegistrations([int]$year) {
  foreach ($scope in 'User', 'Machine') {
    $base = if ($scope -eq 'User') { $env:APPDATA } else { $env:ProgramData }
    $folder = Join-Path $base "Autodesk\Revit\Addins\$year"
    if (-not (Test-Path -LiteralPath $folder)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $folder -Filter '*.addin' -File) {
      try { [xml]$doc = [IO.File]::ReadAllText($file.FullName) }
      catch { Write-Warning "Unreadable manifest: $($file.FullName)"; continue }
      foreach ($entry in @($doc.RevitAddIns.AddIn)) {
        if ([string]$entry.AddInId -ieq $EkId -or [string]$entry.FullClassName -ceq 'EKRibbon.Application') {
          [pscustomobject]@{ Path = $file.FullName; Scope = $scope; Owned = (Test-EkEntry $entry); Assembly = [string]$entry.Assembly }
        }
      }
    }
  }
}

# Removes only EK Ribbon entries; other add-ins in a shared manifest are kept.
function Remove-EkEntries([string]$path) {
  [xml]$doc = [IO.File]::ReadAllText($path)
  foreach ($entry in @($doc.RevitAddIns.AddIn)) { if (Test-EkEntry $entry) { $null = $entry.ParentNode.RemoveChild($entry) } }
  if (@($doc.RevitAddIns.AddIn | Where-Object { $null -ne $_ }).Count -eq 0) { Remove-Item -LiteralPath $path -Force }
  else { $doc.Save($path) }
}
