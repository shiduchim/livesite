# Builds EKRibbon.dll for Revit 2024-2027, runs the tests and makes the install ZIP in out/.
# Needs the .NET 8 and .NET 10 SDKs. Works on Windows and Linux.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $here 'Source/EKRibbon.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "EK_Ribbon_Builder_$version"
$out = Join-Path $here 'out'
$pkg = Join-Path $out $name
Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $pkg | Out-Null

& dotnet test (Join-Path $here 'Tests/LayoutTests') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

foreach ($year in 2024..2027) {
  Write-Host "Building Revit $year" -ForegroundColor Cyan
  $bin = Join-Path $pkg "Bin/$year"
  & dotnet build (Join-Path $here 'Source/EKRibbon.csproj') -c Release "-p:RevitYear=$year" "-p:OutputPath=$bin/" --nologo -v q -warnaserror
  if ($LASTEXITCODE -ne 0) { throw "Build failed for Revit $year." }
  Get-ChildItem -LiteralPath $bin -File | Where-Object Name -ne 'EKRibbon.dll' | Remove-Item
}

Copy-Item (Join-Path $here 'INSTALL.cmd'), (Join-Path $here 'UNINSTALL.cmd'), (Join-Path $here 'DIAGNOSTICS.cmd'), (Join-Path $here 'README.md') $pkg
Copy-Item (Join-Path $here 'Installer') $pkg -Recurse
New-Item -ItemType Directory -Path (Join-Path $pkg 'Source') | Out-Null
Get-ChildItem (Join-Path $here 'Source') -Recurse -File -Include *.cs, *.xaml, *.csproj |
  Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } | ForEach-Object {
    $rel = $_.FullName.Substring((Join-Path $here 'Source').Length + 1)
    $dest = Join-Path $pkg "Source/$rel"
    New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
    Copy-Item $_.FullName $dest
  }
Copy-Item (Join-Path $here 'Directory.Build.props') $pkg

$sums = Get-ChildItem (Join-Path $pkg 'Bin') -Recurse -Filter EKRibbon.dll | Sort-Object FullName | ForEach-Object {
  "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  Bin/$($_.Directory.Name)/EKRibbon.dll" }
$sums | Set-Content (Join-Path $pkg 'SHA256SUMS.txt')
Compress-Archive -Path $pkg -DestinationPath (Join-Path $out "$name.zip")
Write-Host "Package: out/$name.zip" -ForegroundColor Green
