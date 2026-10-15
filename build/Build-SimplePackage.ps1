param([string]$Payload='out/payload',[string]$Destination='out/simple-package')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$Payload=(Resolve-Path $Payload).Path
$Destination=[IO.Path]::GetFullPath((Join-Path (Get-Location) $Destination))
if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
$package=Join-Path $Destination 'EK_Triangle_SIMPLE_Installer'
New-Item -ItemType Directory -Path (Join-Path $package 'engine') -Force | Out-Null
dotnet build (Join-Path $root 'src/core/EKTriangle.Core.csproj') -f net48 -c Release --nologo -v q -o (Join-Path $root 'out/simple-engine')
if ($LASTEXITCODE -ne 0) { throw 'Existing Core DLL build failed' }
Copy-Item (Join-Path $root 'out/simple-engine/EKTriangle.Core.dll') (Join-Path $package 'engine')
foreach ($year in 2024,2025,2026,2027) {
    $target=Join-Path $package "payload/Revit$year"
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($name in 'EKTriangle.dll','EKTriangleHook.dll') { Copy-Item (Join-Path $Payload "Revit$year/$name") $target }
}
Copy-Item (Join-Path $root 'installer/glyph.ini') (Join-Path $package 'payload/glyph.ini')
Copy-Item (Join-Path $root 'installer/cmd/*') $package
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt'), (Join-Path $root 'docs/ACCEPTANCE-TEST.md') $package
Copy-Item (Join-Path $root 'src/native/third_party/minhook/LICENSE.txt') (Join-Path $package 'MINHOOK-LICENSE.txt')
Copy-Item (Join-Path $root 'docs/LAUNCH-INVESTIGATION.md') $package
if (Test-Path (Join-Path $root 'out/TEST-RESULTS.md')) { Copy-Item (Join-Path $root 'out/TEST-RESULTS.md') $package }
# Windows PowerShell 5.1 reliably reads UTF-8 BOM scripts; CMD stays ASCII CRLF.
foreach ($file in Get-ChildItem $package -File | Where-Object Extension -In '.cmd','.ps1') {
    $text=[IO.File]::ReadAllText($file.FullName).Replace("`r`n","`n").Replace("`n","`r`n")
    $encoding=if ($file.Extension -eq '.cmd') { [Text.Encoding]::ASCII } else { [Text.UTF8Encoding]::new($true) }
    [IO.File]::WriteAllText($file.FullName,$text,$encoding)
}
if (@(Get-ChildItem $package -Recurse -Filter '*.exe').Count) { throw 'EXEs must not be delivered in the SIMPLE package' }
Get-ChildItem $package -Recurse -File | Where-Object Extension -In '.dll','.ps1','.cmd' | Sort-Object FullName | ForEach-Object {
    $role=if ($_.Name -eq 'EKTriangle.Core.dll') { 'Existing .NET Framework 4.8 compatibility, installation and restoration library; loaded directly by PowerShell' }
    elseif ($_.Extension -eq '.ps1') { 'Readable Windows PowerShell 5.1 installer/status script' }
    elseif ($_.Extension -eq '.cmd') { 'CMD entry point: invokes ordinary Windows PowerShell' }
    elseif ($_.Name -eq 'EKTriangleHook.dll') { 'Existing x64 native glyph engine' }
    else { 'Existing compiled Revit loader: '+$_.Directory.Name }
    [pscustomobject]@{Path=$_.FullName.Substring($package.Length+1).Replace('\','/');Role=$role;Bytes=$_.Length;SHA256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
} | Export-Csv (Join-Path $package 'CODE-INVENTORY.csv') -NoTypeInformation -Encoding UTF8
Get-ChildItem $package -Recurse -File | Sort-Object FullName | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)  $($_.FullName.Substring($package.Length+1).Replace('\','/'))"
} | Set-Content (Join-Path $package 'SHA256SUMS.txt') -Encoding ASCII
$zip=Join-Path $Destination 'EK_Triangle_SIMPLE_Installer.zip'
Compress-Archive -Path $package -DestinationPath $zip -CompressionLevel Optimal
"$((Get-FileHash $zip -Algorithm SHA256).Hash)  EK_Triangle_SIMPLE_Installer.zip" | Set-Content (Join-Path $Destination 'SIMPLE-SHA256SUMS.txt') -Encoding ASCII
Write-Host "Built $zip (no EXE files)"
