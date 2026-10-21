<#
.SYNOPSIS
  Builds EK FastPDF on Windows: tests, self-contained publish, start-up smoke test,
  portable ZIP and the Inno Setup installer.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build\build.ps1
  (needs the .NET 8 SDK; Inno Setup 6 for the installer: ISCC.exe on PATH or -Iscc)
#>
param(
    [string]$Iscc = '',
    # Optional Authenticode signing, e.g. -SignTool signtool.exe -SignArgs '/a /fd SHA256 /tr http://timestamp.digicert.com /td SHA256'
    [string]$SignTool = '',
    [string]$SignArgs = '',
    [switch]$SkipTests,
    [string]$Version = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if (-not $Version) { $Version = (Select-Xml -Path Directory.Build.props -XPath '//Version').Node.InnerText }
$short = ($Version -split '\.')[0..1] -join '.'
$out = Join-Path $root 'out'
$publish = Join-Path $out 'publish\EKFastPDF'
New-Item -ItemType Directory -Force -Path $out | Out-Null

function Step($name) { Write-Host "`n== $name" -ForegroundColor Cyan }
function Check($ok, $message) { if (-not $ok) { throw $message } }

if (-not $SkipTests) {
    Step 'Unit and engine integration tests'
    dotnet test tests/EKFastPDF.Tests -c Release --nologo --logger "trx;LogFileName=engine-tests.trx" --results-directory "$out/test-results"
    Check ($LASTEXITCODE -eq 0) 'Tests failed'
}

Step "Publish self-contained win-x64 ($Version)"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet publish src/EKFastPDF/EKFastPDF.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publish --nologo
Check ($LASTEXITCODE -eq 0) 'Publish failed'

function Sign($file) {
    if (-not $SignTool) { return }
    & $SignTool sign @($SignArgs -split ' ' | Where-Object { $_ }) $file
    Check ($LASTEXITCODE -eq 0) "Signing failed: $file"
}
Sign (Join-Path $publish 'EKFastPDF.exe')

Step 'Verify payload'
foreach ($f in 'EKFastPDF.exe', 'EKFastPDF.dll', 'EKFastPDF.Core.dll', 'pdfium.dll', 'EKFastPDF.runtimeconfig.json', 'hostfxr.dll', 'coreclr.dll', 'PresentationFramework.dll', 'wpfgfx_cor3.dll') {
    Check (Test-Path (Join-Path $publish $f)) "Missing from publish output: $f"
}
$pe = [System.IO.File]::ReadAllBytes((Join-Path $publish 'pdfium.dll'))
$peOffset = [BitConverter]::ToInt32($pe, 0x3C)
Check ([BitConverter]::ToUInt16($pe, $peOffset + 4) -eq 0x8664) 'pdfium.dll is not x64'
$cfg = Get-Content (Join-Path $publish 'EKFastPDF.runtimeconfig.json') -Raw | ConvertFrom-Json
Check ($null -ne $cfg.runtimeOptions.includedFrameworks) 'Publish is not self-contained'
Write-Host "Payload OK: $((Get-ChildItem $publish -Recurse -File).Count) files, $([math]::Round((Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)) MB"

Step 'Start-up smoke test (diagnostics mode, different working directory)'
Push-Location $env:SystemRoot
try {
    $p = Start-Process -FilePath (Join-Path $publish 'EKFastPDF.exe') -ArgumentList '--diagnose' -PassThru -Wait -NoNewWindow
    Check ($p.ExitCode -eq 0) "EKFastPDF.exe --diagnose exited with $($p.ExitCode)"
} finally { Pop-Location }

Step 'Portable ZIP'
$portableDir = Join-Path $out "portable\EK FastPDF $short"
if (Test-Path (Split-Path $portableDir)) { Remove-Item -Recurse -Force (Split-Path $portableDir) }
New-Item -ItemType Directory -Force -Path $portableDir | Out-Null
Copy-Item "$publish\*" $portableDir -Recurse
Copy-Item THIRD-PARTY-NOTICES.txt $portableDir
@"
EK FastPDF $Version - portable version
=======================================
Extract the whole folder to a local disk (for example C:\Tools\EK FastPDF) and run EKFastPDF.exe.
Nothing else needs to be installed. Settings: %APPDATA%\EK FastPDF; log: %LOCALAPPDATA%\EK FastPDF\logs.

For the Start menu entry, PDF "Open with" registration and normal uninstall, use
EK_FastPDF_Setup_v$short.exe instead.

If Windows shows "Windows cannot access the specified device, path, or file" when you start the
portable copy, the folder is probably in a location Windows treats as untrusted (a downloaded
ZIP that kept its internet mark, a network share, or a synced cloud folder). Use the installer, or
right-click the downloaded ZIP > Properties > Unblock before extracting, and extract to a local folder.
"@ | Set-Content (Join-Path $portableDir 'README-PORTABLE.txt') -Encoding UTF8
$zip = Join-Path $out "installer\EK_FastPDF_Portable_v$short.zip"
New-Item -ItemType Directory -Force -Path (Split-Path $zip) | Out-Null
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $portableDir -DestinationPath $zip -CompressionLevel Optimal

Step 'Installer'
if (-not $Iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $Iscc = $cmd.Source }
    elseif (Test-Path "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") { $Iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
}
if ($Iscc) {
    & $Iscc "/DPublishDir=$publish" "/DAppVersion=$Version" "/DOutDir=$out\installer" "/DOutName=EK_FastPDF_Setup_v$short" installer\EKFastPDF.iss
    Check ($LASTEXITCODE -eq 0) 'Inno Setup failed'
    Sign "$out\installer\EK_FastPDF_Setup_v$short.exe"
} else {
    Write-Warning 'Inno Setup (ISCC.exe) not found: installer skipped. Install Inno Setup 6 or pass -Iscc.'
}

Step 'Checksums'
Get-ChildItem "$out\installer" -File | Where-Object { $_.Extension -in '.exe', '.zip' } | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)  $($_.Name)"
} | Set-Content "$out\installer\SHA256SUMS.txt"
Get-Content "$out\installer\SHA256SUMS.txt"
