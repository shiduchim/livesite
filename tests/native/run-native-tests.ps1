<#
  Runs the native harness against EKTriangleHook.dll on real Windows.
  Exit code 0 = all checks passed.
#>
param(
    [Parameter(Mandatory = $true)][string]$NativeDir,
    [string]$ResultDir = ''
)
$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) ('ekt-native-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $work | Out-Null
foreach ($f in 'EKTriangleHook.dll', 'Revit.exe', 'NotRevit.exe') { Copy-Item (Join-Path $NativeDir $f) $work }

$failed = 0
$modes = @(
    @{ Mode = 'exports';  Exe = 'Revit.exe' },
    @{ Mode = 'render';   Exe = 'Revit.exe' },
    @{ Mode = 'notrevit'; Exe = 'NotRevit.exe' },
    @{ Mode = 'disabled'; Exe = 'Revit.exe' },
    @{ Mode = 'legacy';   Exe = 'Revit.exe' },
    @{ Mode = 'dup';      Exe = 'Revit.exe' },
    @{ Mode = 'hook';     Exe = 'Revit.exe' }
)
foreach ($m in $modes) {
    $cfg = Join-Path $work ('cfg-' + $m.Mode)
    New-Item -ItemType Directory -Force -Path $cfg | Out-Null
    $output = & (Join-Path $work $m.Exe) $m.Mode (Join-Path $work 'EKTriangleHook.dll') $cfg 2>&1
    $code = $LASTEXITCODE
    $output | ForEach-Object { Write-Host "   $_" }
    if ($code -eq 0) { Write-Host "PASS native:$($m.Mode)" -ForegroundColor Green }
    elseif ($code -eq 77) { Write-Host "SKIP native:$($m.Mode) (no visual styles in this session)" -ForegroundColor Yellow }
    else { Write-Host "FAIL native:$($m.Mode) (exit $code)" -ForegroundColor Red; $failed++ }
}
if ($ResultDir) {
    New-Item -ItemType Directory -Force -Path $ResultDir | Out-Null
    $bmp = Join-Path $work 'cfg-render\glyphs.bmp'
    if (Test-Path $bmp) { Copy-Item $bmp (Join-Path $ResultDir 'native-glyphs.bmp') }
    $log = Join-Path $env:LOCALAPPDATA 'EKTriangle\Logs\Native.log'
    if (Test-Path $log) { Copy-Item $log (Join-Path $ResultDir 'native-test-Native.log') }
}
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
if ($failed) { Write-Host "$failed native test(s) failed" -ForegroundColor Red; exit 1 }
exit 0
