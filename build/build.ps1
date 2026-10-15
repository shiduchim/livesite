<#
  EK Triangle - complete Windows release build (what CI runs).

  Requirements on the build PC (never on the user's PC):
    * Visual Studio 2022 / Build Tools with "Desktop development with C++"
    * .NET SDK 8 and 10
    * Inno Setup 6 (ISCC.exe) - pass -Iscc or install to the default folder

  Usage:  powershell -ExecutionPolicy Bypass -File build\build.ps1 [-SkipTests] [-Iscc <path>]
  Output: out\installer\EK_Triangle_Setup.exe and out\installer\SHA256SUMS.txt
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [string]$Iscc = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'out'
$payload = Join-Path $out 'payload'
$version = (Select-Xml -Path (Join-Path $root 'Directory.Build.props') -XPath '//Version').Node.InnerText
Write-Host "EK Triangle $version" -ForegroundColor Cyan

function Step([string]$name) { Write-Host "== $name" -ForegroundColor Cyan }
function Check([string]$what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" } }

if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
if (Test-Path (Join-Path $out 'installer')) { Remove-Item (Join-Path $out 'installer') -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null

# ---- native (MSVC) ---------------------------------------------------------
Step 'native DLL (MSVC x64)'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual C++ x64 build tools not found' }
$native = Join-Path $out 'native'
cmd /c "call `"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul && `"$root\build\build-native-msvc.cmd`" `"$native`" $version"
Check 'native build'

# ---- managed ----------------------------------------------------------------
Step 'helper EXE (.NET Framework 4.8)'
dotnet build (Join-Path $root 'src\tool\EKTriangle.Tool.csproj') -c Release --nologo -v q -o (Join-Path $out 'tool'); Check 'tool build'
Copy-Item (Join-Path $out 'tool\EKTriangle.exe'), (Join-Path $out 'tool\EKTriangle.exe.config') $payload

foreach ($y in 2024, 2025, 2026, 2027) {
    Step "Revit $y loader"
    $lo = Join-Path $out "loader$y"
    dotnet build (Join-Path $root "src\loader\Revit$y\EKTriangle.Loader.$y.csproj") -c Release --nologo -v q -o $lo; Check "loader $y"
    $dest = Join-Path $payload "Revit$y"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item (Join-Path $lo 'EKTriangle.dll') $dest
    Copy-Item (Join-Path $native 'EKTriangleHook.dll') $dest
}

# ---- tests ------------------------------------------------------------------
if (-not $SkipTests) {
    Step 'unit tests (net8.0 + net48)'
    $results = Join-Path $out 'test-results'
    New-Item -ItemType Directory -Force -Path $results | Out-Null
    dotnet test (Join-Path $root 'tests\EKTriangle.Tests') -c Release --nologo --logger trx --results-directory $results; Check 'unit tests'
    Step 'native tests'
    & (Join-Path $root 'tests\native\run-native-tests.ps1') -NativeDir $native -ResultDir $results *>&1 | Tee-Object -FilePath (Join-Path $results 'native-tests.txt')
    Check 'native tests'
    Step 'payload checks'
    & (Join-Path $root 'tests\installer\Test-Payload.ps1') -Payload $payload *>&1 | Tee-Object -FilePath (Join-Path $results 'payload-checks.txt')
    Check 'payload checks'
}

# ---- installer --------------------------------------------------------------
Step 'installer (Inno Setup)'
if (-not $Iscc) {
    foreach ($c in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $c) { $Iscc = $c; break }
    }
}
if (-not $Iscc -or -not (Test-Path $Iscc)) { throw 'ISCC.exe (Inno Setup 6) not found; pass -Iscc' }
$instOut = Join-Path $out 'installer'
& $Iscc /Q "/DAppVersion=$version" "/DPayload=$payload" "/DOutDir=$instOut" (Join-Path $root 'installer\EKTriangle.iss'); Check 'ISCC'

# ---- hashes -----------------------------------------------------------------
Step 'hashes'
$lines = @()
$lines += Get-FileHash (Join-Path $instOut 'EK_Triangle_Setup.exe') -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  EK_Triangle_Setup.exe" }
Get-ChildItem $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
    $rel = $_.FullName.Substring($payload.Length + 1)
    $lines += "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)  payload\$rel"
}
$lines | Set-Content (Join-Path $instOut 'SHA256SUMS.txt') -Encoding ASCII
$lines | Write-Host
Write-Host "Built $instOut\EK_Triangle_Setup.exe" -ForegroundColor Green
