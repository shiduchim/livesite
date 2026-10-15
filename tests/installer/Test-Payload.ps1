<#
  Checks the files that go into the installer: presence, PE architecture,
  .NET target framework and Revit API reference of each loader, native exports.
#>
param([Parameter(Mandatory = $true)][string]$Payload)
$ErrorActionPreference = 'Stop'
$failed = 0
function Ok([bool]$cond, [string]$what) {
    if ($cond) { Write-Host "PASS $what" -ForegroundColor Green }
    else { Write-Host "FAIL $what" -ForegroundColor Red; $script:failed++ }
}

function PeMachine([string]$path) {
    $b = [IO.File]::ReadAllBytes($path)
    $pe = [BitConverter]::ToInt32($b, 0x3C)
    return [BitConverter]::ToUInt16($b, $pe + 4)
}

Add-Type -AssemblyName System.Reflection.Metadata -ErrorAction SilentlyContinue
function AssemblyInfo([string]$path) {
    $fs = [IO.File]::OpenRead($path)
    try {
        $pe = New-Object System.Reflection.PortableExecutable.PEReader($fs)
        $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $refs = @{}
        foreach ($h in $md.AssemblyReferences) { $r = $md.GetAssemblyReference($h); $refs[$md.GetString($r.Name)] = $r.Version }
        $tfm = ''
        foreach ($ch in $md.CustomAttributes) {
            $ca = $md.GetCustomAttribute($ch)
            $blob = $md.GetBlobBytes($ca.Value)
            $text = [Text.Encoding]::UTF8.GetString($blob)
            if ($text -match '(\.NETFramework,Version=v[\d.]+|\.NETCoreApp,Version=v[\d.]+)') { $tfm = $Matches[1] }
        }
        $types = @()
        foreach ($th in $md.TypeDefinitions) { $t = $md.GetTypeDefinition($th); $types += ($md.GetString($t.Namespace) + '.' + $md.GetString($t.Name)) }
        return @{ Refs = $refs; Tfm = $tfm; Types = $types; Machine = $pe.PEHeaders.CoffHeader.Machine; Corflags = $pe.PEHeaders.CorHeader.Flags }
    } finally { $fs.Dispose() }
}

$expect = @{
    2024 = @{ Tfm = '.NETFramework,Version=v4.8'; Api = 24 }
    2025 = @{ Tfm = '.NETCoreApp,Version=v8.0';   Api = 25 }
    2026 = @{ Tfm = '.NETCoreApp,Version=v8.0';   Api = 26 }
    2027 = @{ Tfm = '.NETCoreApp,Version=v10.0';  Api = 27 }
}
Ok (Test-Path (Join-Path $Payload 'EKTriangle.exe')) 'helper EKTriangle.exe present'
$tool = AssemblyInfo (Join-Path $Payload 'EKTriangle.exe')
Ok ($tool.Tfm -eq '.NETFramework,Version=v4.8') "helper targets .NET Framework 4.8 ($($tool.Tfm))"
Ok (-not $tool.Refs.ContainsKey('EKTriangle.Core')) 'helper has no separate DLL dependency'

foreach ($y in 2024, 2025, 2026, 2027) {
    $dir = Join-Path $Payload "Revit$y"
    $loader = Join-Path $dir 'EKTriangle.dll'
    $native = Join-Path $dir 'EKTriangleHook.dll'
    Ok (Test-Path $loader) "Revit $y loader present"
    Ok (Test-Path $native) "Revit $y native DLL present"
    Ok ((PeMachine $native) -eq 0x8664) "Revit $y native DLL is x64"
    $a = AssemblyInfo $loader
    Ok ($a.Tfm -eq $expect[$y].Tfm) "Revit $y loader framework $($a.Tfm)"
    Ok ($a.Refs.ContainsKey('RevitAPIUI') -and $a.Refs['RevitAPIUI'].Major -eq $expect[$y].Api) "Revit $y loader references RevitAPIUI $($a.Refs['RevitAPIUI'])"
    Ok ($a.Types -contains 'EKTriangle.Loader.App') "Revit $y loader class EKTriangle.Loader.App"
    Ok ($a.Machine -eq [System.Reflection.PortableExecutable.Machine]::Amd64 -or ($a.Corflags -band 2)) "Revit $y loader is x64"
    $bytes = [IO.File]::ReadAllBytes($native)
    $ascii = [Text.Encoding]::ASCII.GetString($bytes)
    foreach ($e in 'EKTriangle_Init', 'EKTriangle_Shutdown') { Ok ($ascii.Contains($e)) "Revit $y native export $e" }
    Ok (-not $ascii.Contains('EKGlyph_Init')) "Revit $y native DLL is not the legacy EKGlyphHook"
}
if ($failed) { exit 1 }
exit 0
