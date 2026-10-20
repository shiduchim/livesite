$ErrorActionPreference = 'Stop'
$Host.UI.RawUI.WindowTitle = 'EK Print C# Installer'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceDir = Join-Path $Root 'Source'
$AssetsDir = Join-Path $Root 'Assets'
$InstallRoot = Join-Path $env:APPDATA 'EKPrintCSharp'
$BinRoot = Join-Path $InstallRoot 'Bin'
$ToolsRoot = Join-Path $InstallRoot 'Tools'
$LogPath = Join-Path $env:TEMP 'EK_Print_CSharp_install.log'

function Log([string]$Text) {
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line = "$stamp  $Text"
    Write-Host $Text
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
}

function Fail([string]$Text) {
    Log "ERROR: $Text"
    Write-Host ''
    Write-Host 'INSTALLATION FAILED.' -ForegroundColor Red
    Write-Host $Text -ForegroundColor Red
    Write-Host ''
    Write-Host "Log: $LogPath"
    Write-Host ''
    Read-Host 'Press Enter to close'
    exit 1
}

function Get-RevitInstall([int]$Year) {
    $dir = Join-Path ${env:ProgramFiles} "Autodesk\Revit $Year"
    $api = Join-Path $dir 'RevitAPI.dll'
    $ui = Join-Path $dir 'RevitAPIUI.dll'
    $exe = Join-Path $dir 'Revit.exe'
    if ((Test-Path -LiteralPath $api) -and (Test-Path -LiteralPath $ui) -and (Test-Path -LiteralPath $exe)) {
        return [PSCustomObject]@{ Year=$Year; Dir=$dir; Api=$api; Ui=$ui; Exe=$exe }
    }
    return $null
}

function Get-TargetRuntimeMajor([string]$ApiPath, [int]$Year) {
    if ($Year -eq 2024) { return 4 }
    try {
        $bytes = [IO.File]::ReadAllBytes($ApiPath)
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        if ($text -match '\.NETCoreApp,Version=v20\.0') { return 10 }
        if ($text -match '\.NETCoreApp,Version=v8\.0') { return 8 }
    } catch {}
    # Current Autodesk baselines: 2027 is .NET 10; 2025/2026 may be .NET 8 or .NET 10 after .5 updates.
    if ($Year -ge 2027) { return 10 }
    return 8
}

function Find-RoslynCsc {
    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if (-not $dotnet) { return $null }
    $sdks = @(& $dotnet.Source --list-sdks 2>$null)
    if (-not $sdks -or $sdks.Count -eq 0) { return $null }
    $items = @()
    foreach ($line in $sdks) {
        if ($line -match '^([^\s]+)\s+\[(.+)\]') {
            try {
                $ver = [Version]($matches[1] -replace '-.*$','')
                $base = $matches[2]
                $csc = Join-Path (Join-Path $base $matches[1]) 'Roslyn\bincore\csc.dll'
                if (Test-Path -LiteralPath $csc) {
                    $items += [PSCustomObject]@{ Version=$ver; Csc=$csc; Dotnet=$dotnet.Source }
                }
            } catch {}
        }
    }
    if ($items.Count -eq 0) { return $null }
    return $items | Sort-Object Version -Descending | Select-Object -First 1
}

function Get-ManagedRuntimeReferences([int]$Major) {
    $dotnetRoot = Join-Path ${env:ProgramFiles} 'dotnet'

    function Pick-VersionDir([string]$Base, [int]$M) {
        if (-not (Test-Path -LiteralPath $Base)) { return $null }
        $candidates = Get-ChildItem -LiteralPath $Base -Directory -ErrorAction SilentlyContinue | Where-Object {
            try { ([Version]($_.Name -replace '-.*$','')).Major -eq $M } catch { $false }
        } | Sort-Object { try { [Version]($_.Name -replace '-.*$','') } catch { [Version]'0.0' } } -Descending
        return $candidates | Select-Object -First 1
    }

    # Prefer reference packs when the matching SDK pack is installed.
    $corePackRoot = Join-Path $dotnetRoot 'packs\Microsoft.NETCore.App.Ref'
    $desktopPackRoot = Join-Path $dotnetRoot 'packs\Microsoft.WindowsDesktop.App.Ref'
    $corePack = Pick-VersionDir $corePackRoot $Major
    $desktopPack = Pick-VersionDir $desktopPackRoot $Major
    if ($corePack -and $desktopPack) {
        $tfm = "net$Major.0"
        $coreRef = Join-Path $corePack.FullName ("ref\" + $tfm)
        $desktopRef = Join-Path $desktopPack.FullName ("ref\" + $tfm)
        if ((Test-Path -LiteralPath $coreRef) -and (Test-Path -LiteralPath $desktopRef)) {
            $byFile = @{}
            foreach ($dir in @($coreRef, $desktopRef)) {
                foreach ($f in Get-ChildItem -LiteralPath $dir -Filter '*.dll' -File -ErrorAction SilentlyContinue) {
                    $byFile[$f.Name] = $f.FullName
                }
            }
            Log "Using .NET $Major reference packs."
            return @($byFile.Values)
        }
    }

    # Fallback for machines that have the matching Revit runtime but not its targeting pack.
    $coreRoot = Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App'
    $desktopRoot = Join-Path $dotnetRoot 'shared\Microsoft.WindowsDesktop.App'
    $core = Pick-VersionDir $coreRoot $Major
    $desktop = Pick-VersionDir $desktopRoot $Major
    if (-not $core) { throw ".NET $Major Microsoft.NETCore.App runtime was not found." }
    if (-not $desktop) { throw ".NET $Major Microsoft.WindowsDesktop.App runtime was not found." }

    $byName = @{}
    foreach ($dir in @($core.FullName, $desktop.FullName)) {
        foreach ($f in Get-ChildItem -LiteralPath $dir -Filter '*.dll' -File -ErrorAction SilentlyContinue) {
            try {
                $an = [Reflection.AssemblyName]::GetAssemblyName($f.FullName)
                $byName[$an.Name] = $f.FullName
            } catch {}
        }
    }
    Log "Using .NET $Major runtime assemblies as compiler references."
    return @($byName.Values)
}

function Compile-2024($Revit, [string]$OutDll) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $csc = Join-Path $framework 'csc.exe'
    if (-not (Test-Path -LiteralPath $csc)) { throw '.NET Framework C# compiler was not found.' }

    $refs = @(
        (Join-Path $framework 'System.dll'),
        (Join-Path $framework 'System.Core.dll'),
        (Join-Path $framework 'System.Drawing.dll'),
        (Join-Path $framework 'System.Windows.Forms.dll'),
        (Join-Path $framework 'System.Xaml.dll'),
        (Join-Path $framework 'WPF\WindowsBase.dll'),
        (Join-Path $framework 'WPF\PresentationCore.dll'),
        (Join-Path $framework 'WPF\PresentationFramework.dll'),
        $Revit.Api,
        $Revit.Ui
    ) | Where-Object { Test-Path -LiteralPath $_ }

    $rsp = Join-Path $env:TEMP ('EKPrint_2024_' + [Guid]::NewGuid().ToString('N') + '.rsp')
    $lines = @('/nologo','/target:library','/optimize+','/platform:x64',('/out:"' + $OutDll + '"'))
    foreach ($r in $refs) { $lines += ('/reference:"' + $r + '"') }
    foreach ($src in Get-ChildItem -LiteralPath $SourceDir -Filter '*.cs' -File | Sort-Object Name) { $lines += ('"' + $src.FullName + '"') }
    [IO.File]::WriteAllLines($rsp, $lines, [Text.Encoding]::UTF8)
    try {
        $output = & $csc ('@' + $rsp) 2>&1
        foreach ($line in $output) { Log ([string]$line) }
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $OutDll)) { throw 'C# compilation failed for Revit 2024.' }
    } finally { Remove-Item -LiteralPath $rsp -Force -ErrorAction SilentlyContinue }
}

function Compile-Modern($Revit, [int]$Major, [string]$OutDll, $Roslyn) {
    if (-not $Roslyn) { throw "A .NET SDK is required to compile the Revit $($Revit.Year) add-in. Install a .NET 10 SDK and run this installer again." }
    $refs = Get-ManagedRuntimeReferences $Major
    $refs += @($Revit.Api, $Revit.Ui)

    $rsp = Join-Path $env:TEMP ('EKPrint_' + $Revit.Year + '_' + [Guid]::NewGuid().ToString('N') + '.rsp')
    $lines = @('/nologo','/nostdlib+','/target:library','/optimize+','/platform:x64','/langversion:latest',('/out:"' + $OutDll + '"'))
    foreach ($r in $refs) { $lines += ('/reference:"' + $r + '"') }
    foreach ($src in Get-ChildItem -LiteralPath $SourceDir -Filter '*.cs' -File | Sort-Object Name) { $lines += ('"' + $src.FullName + '"') }
    [IO.File]::WriteAllLines($rsp, $lines, [Text.Encoding]::UTF8)
    try {
        $output = & $Roslyn.Dotnet $Roslyn.Csc ('@' + $rsp) 2>&1
        foreach ($line in $output) { Log ([string]$line) }
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $OutDll)) { throw "C# compilation failed for Revit $($Revit.Year) (.NET $Major)." }
    } finally { Remove-Item -LiteralPath $rsp -Force -ErrorAction SilentlyContinue }
}

function Remove-PdfSharpLeftovers {
    # Removes everything the old Combine helper (EK Print v30 / v6.1) put on this PC.
    # 1. The EKPdfMerge helper program.
    $helperDir = Join-Path $ToolsRoot 'PdfMergeHelper'
    if (Test-Path -LiteralPath $helperDir) {
        try {
            Remove-Item -LiteralPath $helperDir -Recurse -Force -ErrorAction Stop
            Log "Removed old PDF merge helper: $helperDir"
        } catch {
            Log "Warning: could not remove $helperDir : $($_.Exception.Message)"
        }
    }
    if ((Test-Path -LiteralPath $ToolsRoot) -and -not (Get-ChildItem -LiteralPath $ToolsRoot -Force -ErrorAction SilentlyContinue)) {
        Remove-Item -LiteralPath $ToolsRoot -Force -ErrorAction SilentlyContinue
    }

    # 2. PDFsharp package files that NuGet downloaded for it.
    $packageRoots = @()
    if ($env:NUGET_PACKAGES) { $packageRoots += $env:NUGET_PACKAGES }
    $packageRoots += (Join-Path $env:USERPROFILE '.nuget\packages')
    foreach ($root in ($packageRoots | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($dir in Get-ChildItem -LiteralPath $root -Directory -Filter 'pdfsharp*' -ErrorAction SilentlyContinue) {
            try {
                Remove-Item -LiteralPath $dir.FullName -Recurse -Force -ErrorAction Stop
                Log "Removed downloaded package: $($dir.FullName)"
            } catch {
                Log "Warning: could not remove $($dir.FullName) : $($_.Exception.Message)"
            }
        }
    }

    # 3. NuGet's download cache copies.
    foreach ($cache in @((Join-Path $env:LOCALAPPDATA 'NuGet\v3-cache'), (Join-Path $env:LOCALAPPDATA 'NuGet\http-cache'))) {
        if (-not (Test-Path -LiteralPath $cache)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $cache -Recurse -File -Filter '*pdfsharp*' -ErrorAction SilentlyContinue) {
            try {
                Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
                Log "Removed cached download: $($file.FullName)"
            } catch {}
        }
    }
}

function XmlEscape([string]$Text) {
    return [Security.SecurityElement]::Escape($Text)
}

function Write-Manifest([int]$Year, [string]$DllPath) {
    $addinDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$Year"
    New-Item -ItemType Directory -Force -Path $addinDir | Out-Null
    $manifest = Join-Path $addinDir 'EKPrint.addin'
    $assemblyEscaped = XmlEscape $DllPath
    $xml = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>EK Print</Name>
    <Assembly>$assemblyEscaped</Assembly>
    <AddInId>AE61B9AE-2CB2-4E6F-83C4-2DE7A2FA83F1</AddInId>
    <FullClassName>EKPrint.App</FullClassName>
    <VendorId>EKTL</VendorId>
    <VendorDescription>EK Tools</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
    [IO.File]::WriteAllText($manifest, $xml, (New-Object Text.UTF8Encoding($false)))
    return $manifest
}

function Disable-OldPyRevitExtension {
    $roots = @()
    foreach ($d in Get-ChildItem -LiteralPath $env:APPDATA -Directory -ErrorAction SilentlyContinue) {
        if ($d.Name -like '*pyRevit*') { $roots += $d.FullName }
    }
    foreach ($root in $roots) {
        $candidates = Get-ChildItem -LiteralPath $root -Directory -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'EKPrint.extension' }
        foreach ($candidate in $candidates) {
            $backup = $candidate.FullName + '.DISABLED_BY_CSHARP'
            if (Test-Path -LiteralPath $backup) {
                $backup = $candidate.FullName + '.DISABLED_BY_CSHARP_' + (Get-Date -Format 'yyyyMMdd_HHmmss')
            }
            try {
                Rename-Item -LiteralPath $candidate.FullName -NewName (Split-Path -Leaf $backup) -ErrorAction Stop
                Log "Disabled old pyRevit EK Print extension: $($candidate.FullName)"
            } catch {
                Log "Warning: could not disable old pyRevit extension $($candidate.FullName): $($_.Exception.Message)"
            }
        }
    }
}

# --- Start ---
Remove-Item -LiteralPath $LogPath -Force -ErrorAction SilentlyContinue
Log 'EK Print C# installer v6.2 started.'

if (Get-Process Revit -ErrorAction SilentlyContinue) {
    Fail 'Revit is running. Close ALL Revit windows, then run the installer again.'
}

if (-not (Test-Path -LiteralPath $SourceDir)) { Fail 'Source folder is missing from the installer package.' }
if (-not (Test-Path -LiteralPath $AssetsDir)) { Fail 'Assets folder is missing from the installer package.' }

$installed = @()
foreach ($year in @(2024,2025,2026,2027)) {
    $r = Get-RevitInstall $year
    if ($r) { $installed += $r }
}
if ($installed.Count -eq 0) { Fail 'No Revit 2024-2027 installations were found.' }

$roslyn = Find-RoslynCsc
if ($roslyn) { Log "Using Roslyn compiler: $($roslyn.Csc)" }
else { Log 'No dotnet SDK compiler detected. Revit 2024 can still be compiled; modern Revit builds require a .NET SDK.' }


New-Item -ItemType Directory -Force -Path $BinRoot | Out-Null
$built = @()
foreach ($r in $installed) {
    try {
        $yearDir = Join-Path $BinRoot ([string]$r.Year)
        if (Test-Path -LiteralPath $yearDir) { Remove-Item -LiteralPath $yearDir -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $yearDir | Out-Null
        $dll = Join-Path $yearDir 'EKPrint.dll'
        $runtime = Get-TargetRuntimeMajor $r.Api $r.Year
        Log "Building Revit $($r.Year) (detected runtime: .NET $runtime)..."
        if ($r.Year -eq 2024) { Compile-2024 $r $dll }
        else { Compile-Modern $r $runtime $dll $roslyn }

        foreach ($asset in Get-ChildItem -LiteralPath $AssetsDir -Filter '*.png' -File) {
            Copy-Item -LiteralPath $asset.FullName -Destination (Join-Path $yearDir $asset.Name) -Force
        }
        $manifest = Write-Manifest $r.Year $dll
        Log "Installed Revit $($r.Year): $dll"
        Log "Manifest: $manifest"
        $built += $r.Year
    }
    catch {
        Fail "Revit $($r.Year) build/install failed: $($_.Exception.Message)"
    }
}

Disable-OldPyRevitExtension
Remove-PdfSharpLeftovers

Write-Host ''
Write-Host '============================================' -ForegroundColor Cyan
Write-Host '  EK Print C# v6.2 installed successfully' -ForegroundColor Green
Write-Host '============================================' -ForegroundColor Cyan
Write-Host ''
Write-Host ('Installed for Revit: ' + ($built -join ', '))
Write-Host 'Ribbon: EK > Print'
Write-Host ''
Write-Host 'Visible buttons:'
Write-Host '  Quick PDF   Select PDF   Quick DWG   Select DWG   Sheet Print'
Write-Host 'Hidden slide-out: Settings'
Write-Host ''
Write-Host 'Combine mode: Revit builds the combined PDF itself (no third-party code, no downloads).'
Write-Host 'Separate mode: multi-page jobs show Cancel Print and stop between sheets/views.'
Write-Host ''
Write-Host 'Your previous EK Print save-folder setting is migrated automatically on first use.'
Write-Host ''
Read-Host 'Press Enter to close'
