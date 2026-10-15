param([string]$Payload = 'out/payload', [string]$Destination = 'out/cmd-package')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$Payload = (Resolve-Path $Payload).Path
$Destination = [IO.Path]::GetFullPath((Join-Path (Get-Location) $Destination))
if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$package = Join-Path $Destination 'EK_Triangle_CMD_Installer'
New-Item -ItemType Directory -Force -Path $package | Out-Null
dotnet build (Join-Path $root 'src/cmd/EKTriangle.Cmd.csproj') -c Release --nologo -v q -o (Join-Path $root 'out/cmd-tool')
if ($LASTEXITCODE -ne 0) { throw 'CMD frontend build failed' }
Copy-Item (Join-Path $root 'out/cmd-tool/EKTriangle.CMD.exe'), (Join-Path $root 'out/cmd-tool/EKTriangle.CMD.exe.config') $package
Copy-Item $Payload (Join-Path $package 'payload') -Recurse
Copy-Item (Join-Path $root 'installer/glyph.ini') (Join-Path $package 'payload/glyph.ini')
Copy-Item (Join-Path $root 'installer/cmd/*') $package
Copy-Item (Join-Path $root 'docs/ACCEPTANCE-TEST.md'), (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $package
Copy-Item (Join-Path $root 'src/native/third_party/minhook/LICENSE.txt') (Join-Path $package 'MINHOOK-LICENSE.txt')
foreach ($f in Get-ChildItem $package -Filter '*.cmd') {
    $text = [IO.File]::ReadAllText($f.FullName).Replace("`r`n", "`n").Replace("`n", "`r`n")
    [IO.File]::WriteAllText($f.FullName, $text, [Text.Encoding]::ASCII)
}
if (Test-Path (Join-Path $root 'out/TEST-RESULTS.md')) {
    Copy-Item (Join-Path $root 'out/TEST-RESULTS.md') $package
}
$executables = Get-ChildItem $package -Recurse -File | Where-Object Extension -In '.exe', '.dll', '.cmd' | Sort-Object FullName
$executables | ForEach-Object {
    $rel = $_.FullName.Substring($package.Length + 1).Replace('\', '/')
    $role = if ($_.Extension -eq '.cmd') { 'Plain CMD entry point' }
    elseif ($_.Name -eq 'EKTriangle.CMD.exe') { '.NET Framework 4.8 console coordinator; shares existing Core' }
    elseif ($_.Name -eq 'EKTriangle.exe') { 'Existing .NET Framework 4.8 browser/restore worker' }
    elseif ($_.Name -eq 'EKTriangleHook.dll') { 'Existing x64 native glyph hook, static runtime' }
    else { 'Existing x64 managed Revit loader: ' + $_.Directory.Name }
    [pscustomobject]@{Path=$rel; Role=$role; Bytes=$_.Length; SHA256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
} | Export-Csv (Join-Path $package 'EXECUTABLE-INVENTORY.csv') -NoTypeInformation -Encoding UTF8
Get-ChildItem $package -Recurse -File | Sort-Object FullName | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)  $($_.FullName.Substring($package.Length + 1).Replace('\','/'))"
} | Set-Content (Join-Path $package 'SHA256SUMS.txt') -Encoding ASCII
Compress-Archive -Path $package -DestinationPath (Join-Path $Destination 'EK_Triangle_CMD_Installer.zip') -CompressionLevel Optimal
$zip = Join-Path $Destination 'EK_Triangle_CMD_Installer.zip'
"$((Get-FileHash $zip -Algorithm SHA256).Hash)  EK_Triangle_CMD_Installer.zip" | Set-Content (Join-Path $Destination 'SHA256SUMS.txt') -Encoding ASCII
Write-Host "Built $zip"
