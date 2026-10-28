# EKPDF Revit add-in setup: installs "Export 3D PDF" for every Revit 2024-2027 found,
# for the current user. Needs EKPDF (the reader), which finishes each export.
# Replaces the earlier combined "Offline 3D PDF" 0.5.x installation.
param([ValidateSet('Install','Uninstall')][string]$Mode='Install')
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
$packageRoot=Split-Path $PSScriptRoot -Parent
$releases=Join-Path $EkpdfRoot 'RevitAddin\releases'
$stateFile=Join-Path $EkpdfRoot 'revit-addin.json'
$logRoot=Join-Path $EkpdfRoot 'Logs';$null=New-Item $logRoot -ItemType Directory -Force
$log=Join-Path $logRoot ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-revit-addin-'+$Mode+'.log')
$manifestName='Offline3DPdfPrototype.addin'   # same file and AddInId as 0.5.x, so it replaces them
$legacyRoot=Join-Path $env:LOCALAPPDATA 'Offline3DPdfPrototype'
$transcribing=$false

function Manifest-Path([int]$Year){Join-Path $env:APPDATA ('Autodesk\Revit\Addins\'+$Year+'\'+$manifestName)}

# The 0.5.x combined package: its add-in manifests are replaced by this setup; its viewer
# copy and Start menu entry are removed (EKPDF replaces them). Logs and settings are kept.
function Remove-LegacyInstall {
    $legacyState=Join-Path $legacyRoot 'installed.json'
    if(-not (Test-Path -LiteralPath $legacyState)){return $false}
    $state=Get-Content $legacyState -Raw | ConvertFrom-Json
    if($state.product -ne 'Offline3DPdfPrototype'){return $false}
    $old=Join-Path ([Environment]::GetFolderPath('Programs')) 'Offline 3D PDF Viewer.lnk';if(Test-Path -LiteralPath $old){Remove-Item -LiteralPath $old}
    foreach($name in 'App\releases','App\Support','Builds','BuildTools'){$path=Join-Path $legacyRoot $name;if(Test-Path $path){Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue}}
    Remove-Item -LiteralPath $legacyState
    return $true
}

function Install-Addin {
    if(-not (Get-ReaderExe)){throw 'Install EKPDF (the PDF reader) first: run INSTALL.cmd in the EKPDF package, then run this setup again. The add-in uses EKPDF to finish each 3D PDF.'}
    $revits=@(Get-RevitInstallations)
    if($revits.Count -eq 0){throw 'No full Revit 2024-2027 installation was found. Revit LT is not supported.'}
    $revits | Format-Table Year,Framework,FileVersion,Directory | Out-Host
    $payload=Join-Path $packageRoot 'payload'
    $package=Test-Payload $payload 'EKPDF-RevitAddin' @()
    foreach($revit in $revits){if(-not @($package.exporters | Where-Object {$_.year -eq $revit.Year -and $_.framework -eq $revit.Framework})){throw ('This package has no add-in for Revit '+$revit.Year+' ('+$revit.Framework+').')}}
    $release=Join-Path $releases $package.releaseId
    Copy-Release $payload $release
    $null=Test-Payload $release 'EKPDF-RevitAddin' @()
    $snapshots=@()
    try{
        foreach($revit in $revits){
            $dir=Join-Path $env:APPDATA ('Autodesk\Revit\Addins\'+$revit.Year);$null=New-Item $dir -ItemType Directory -Force
            foreach($name in $manifestName,'Offline3DPrototype.DevOnly.addin'){
                $path=Join-Path $dir $name
                $bytes=$null;if(Test-Path $path){if(-not (Test-OwnedManifest $path)){throw ('An add-in manifest with the same name belongs to another app: '+$path)};$bytes=[IO.File]::ReadAllBytes($path)}
                $snapshots+=[pscustomobject]@{path=$path;bytes=$bytes}
                if($name -ne $manifestName){if($bytes){Remove-Item -LiteralPath $path};continue}
                $entry=@($package.exporters | Where-Object {$_.year -eq $revit.Year -and $_.framework -eq $revit.Framework})[0]
                Write-AddinManifest $path (Get-SafeChild $release $entry.path)
            }
        }
    }catch{
        foreach($snapshot in $snapshots){if($snapshot.bytes){[IO.File]::WriteAllBytes($snapshot.path,$snapshot.bytes)}elseif(Test-Path $snapshot.path){Remove-Item -LiteralPath $snapshot.path}}
        Remove-Item -LiteralPath $release -Recurse -Force
        throw
    }
    $replaced=Remove-LegacyInstall
    $support=Join-Path $EkpdfRoot 'Support\revit-addin';if(Test-Path $support){Remove-Item -LiteralPath $support -Recurse -Force}
    $null=New-Item $support -ItemType Directory -Force;Copy-Item (Join-Path $PSScriptRoot '*') $support -Recurse
    Set-UninstallEntry 'EKPDF-RevitAddin' 'EKPDF Revit add-in' $package.version (Get-ReaderExe) $release (Join-Path $support 'Setup-Addin.ps1')
    Write-AtomicJson $stateFile ([ordered]@{schema=1;product='EKPDF-RevitAddin';version=$package.version;release=$release;manifests=@($revits | ForEach-Object {Manifest-Path $_.Year});versions=@($revits)})
    Remove-OtherReleases $releases $release
    $text='EKPDF Revit add-in '+$package.version+' is installed for Revit '+(($revits | ForEach-Object {$_.Year}) -join ', ')+'.'+"`r`n`r`n"+'Reopen Revit: Add-Ins > Export 3D PDF.'
    if($replaced){$text+="`r`n`r`n"+'The earlier Offline 3D PDF installation was replaced.'}
    Notify $text
}

function Uninstall-Addin {
    if(-not (Test-Path $stateFile)){Notify 'The EKPDF Revit add-in is not installed.';return}
    $state=Get-Content $stateFile -Raw | ConvertFrom-Json
    if($state.product -ne 'EKPDF-RevitAddin'){throw 'Installation ownership could not be verified.'}
    $parent=[IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk\Revit\Addins')).TrimEnd('\')+'\'
    foreach($path in $state.manifests){
        if(-not [IO.Path]::GetFullPath($path).StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($path) -ne $manifestName){throw 'Unsafe uninstall manifest path.'}
        if(Test-Path $path){if(-not (Test-OwnedManifest $path)){throw ('Manifest ownership mismatch: '+$path)};Remove-Item -LiteralPath $path}
    }
    if(Test-Path $releases){Remove-Item -LiteralPath $releases -Recurse -Force}
    Remove-UninstallEntry 'EKPDF-RevitAddin'
    Remove-Item -LiteralPath $stateFile
    Remove-Item -LiteralPath (Join-Path $EkpdfRoot 'Support\revit-addin') -Recurse -Force -ErrorAction SilentlyContinue
    Notify 'The EKPDF Revit add-in was uninstalled. EKPDF itself and your PDFs were kept.'
}

try{
    Start-Transcript -LiteralPath $log | Out-Null;$transcribing=$true
    if(-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess){throw 'Windows x64 and 64-bit PowerShell are required.'}
    if(Get-Process Revit,RevitWorker -ErrorAction SilentlyContinue){throw 'Close Revit, then run setup again.'}
    if($Mode -eq 'Uninstall'){Uninstall-Addin}else{Install-Addin}
    exit 0
}catch{
    $message=Get-SetupErrorMessage $_
    Write-Host ($_ | Format-List * -Force | Out-String) -ForegroundColor Red
    Notify ('Setup could not finish.'+"`r`n`r`n"+$message+"`r`n`r`n"+'Details: '+$log) $true
    exit 1
}finally{if($transcribing){Stop-Transcript | Out-Null}}
