# EKPDF reader setup: installs the PDF viewer for the current user (no admin rights,
# no Revit needed). INSTALL.cmd and UNINSTALL.cmd in the package run this script;
# Windows "Apps & features" runs the copy kept in %LOCALAPPDATA%\EKPDF\Support.
param([ValidateSet('Install','Uninstall')][string]$Mode='Install')
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
$packageRoot=Split-Path $PSScriptRoot -Parent
$releases=Join-Path $EkpdfRoot 'Reader\releases'
$stateFile=Join-Path $EkpdfRoot 'reader.json'
$logRoot=Join-Path $EkpdfRoot 'Logs';$null=New-Item $logRoot -ItemType Directory -Force
$log=Join-Path $logRoot ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-reader-'+$Mode+'.log')
$shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'EKPDF.lnk'
$transcribing=$false

function Ensure-WebView([string]$Payload){
    $version=Get-WebView2Version;if($version -and $version.Major -ge 130){Write-Host ('Using installed WebView2 '+$version);return}
    if(-not (Confirm-Choice 'EKPDF needs Microsoft WebView2 Runtime for its 3D view. Windows 10 and 11 usually have it already. Download and run the official Microsoft installer now?')){throw 'WebView2 was not installed. Setup stopped without changes.'}
    $dependency=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
    $installer=Join-Path $EkpdfRoot 'MicrosoftEdgeWebview2Setup.exe'
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;$ProgressPreference='SilentlyContinue'
    Invoke-WebRequest -UseBasicParsing -Uri $dependency.webview2.url -OutFile $installer -TimeoutSec 900
    $signature=Get-AuthenticodeSignature -LiteralPath $installer
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'The WebView2 installer does not have a valid Microsoft signature.'}
    $process=Start-Process -FilePath $installer -ArgumentList '/silent','/install' -Wait -PassThru
    if($process.ExitCode -ne 0 -and $process.ExitCode -ne 3010){throw ('WebView2 setup failed: '+$process.ExitCode)}
}

# Each install goes to a new release folder and the old one is removed, so shortcuts the user
# pinned (taskbar, Start, desktop) would point at a deleted file. Any shortcut to the viewer,
# including ones from "Offline 3D PDF" 0.5.x, is pointed at the new copy.
function Update-UserShortcuts([string]$Exe){
    $folders=@([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('Programs'),
        (Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch'),
        (Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'),
        (Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\StartMenu'))
    $shell=New-Object -ComObject WScript.Shell;$fixed=0
    foreach($folder in $folders){
        if(-not $folder -or -not (Test-Path -LiteralPath $folder)){continue}
        foreach($file in @(Get-ChildItem -LiteralPath $folder -Filter '*.lnk' -File -ErrorAction SilentlyContinue)){
            try{
                $link=$shell.CreateShortcut($file.FullName);$target=[string]$link.TargetPath
                if(-not $target -or (Split-Path $target -Leaf) -ne 'Offline3D.FastPDF.Dev.exe' -or $target -eq $Exe){continue}
                $link.TargetPath=$Exe;$link.WorkingDirectory=Split-Path $Exe -Parent;$link.IconLocation=$Exe+',0';$link.Save();$fixed++
                Write-Host ('Shortcut updated: '+$file.FullName)
            }catch{Write-Host ('Shortcut not changed: '+$file.FullName+' ('+$_.Exception.Message+')')}
        }
    }
    return $fixed
}

function Install-Reader {
    $payload=Join-Path $packageRoot 'payload'
    $package=Test-Payload $payload 'EKPDF' @('Viewer/Offline3D.FastPDF.Dev.exe')
    Ensure-WebView $payload
    $release=Join-Path $releases $package.releaseId
    Copy-Release $payload $release
    $null=Test-Payload $release 'EKPDF' @('Viewer/Offline3D.FastPDF.Dev.exe')
    $exe=Join-Path $release 'Viewer\Offline3D.FastPDF.Dev.exe'
    $support=Join-Path $EkpdfRoot 'Support\reader';if(Test-Path $support){Remove-Item -LiteralPath $support -Recurse -Force}
    $null=New-Item $support -ItemType Directory -Force;Copy-Item (Join-Path $PSScriptRoot '*') $support -Recurse
    Write-Shortcut $shortcut $exe 'EKPDF - fast PDF reader with clickable 3D'
    $null=Update-UserShortcuts $exe
    # Where the Revit add-in (and anything else) finds the reader.
    $null=New-Item $EkpdfKey -Force
    Set-ItemProperty $EkpdfKey -Name ViewerExe -Value $exe;Set-ItemProperty $EkpdfKey -Name Version -Value $package.version;Set-ItemProperty $EkpdfKey -Name InstallRoot -Value $EkpdfRoot
    Set-UninstallEntry 'EKPDF' 'EKPDF' $package.version $exe $release (Join-Path $support 'Setup-Reader.ps1')
    Write-AtomicJson $stateFile ([ordered]@{schema=1;product='EKPDF';version=$package.version;release=$release;shortcut=$shortcut})
    Remove-OtherReleases $releases $release
    Notify ('EKPDF '+$package.version+' is installed.'+"`r`n`r`n"+'Open it from Start > EKPDF.')
}

function Uninstall-Reader {
    if(-not (Test-Path $stateFile)){Notify 'EKPDF is not installed.';return}
    $state=Get-Content $stateFile -Raw | ConvertFrom-Json
    if($state.product -ne 'EKPDF'){throw 'Installation ownership could not be verified.'}
    if(Test-Path (Join-Path $EkpdfRoot 'revit-addin.json')){
        if(-not (Confirm-Choice 'The EKPDF Revit add-in is installed and needs EKPDF to finish 3D PDF exports. Uninstall EKPDF anyway?')){return}
    }
    if(Test-Path -LiteralPath $shortcut){Remove-Item -LiteralPath $shortcut}
    if(Test-Path $releases){Remove-Item -LiteralPath $releases -Recurse -Force}
    foreach($name in 'ViewerExe','Version','InstallRoot'){Remove-ItemProperty $EkpdfKey -Name $name -ErrorAction SilentlyContinue}
    Remove-UninstallEntry 'EKPDF'
    Remove-Item -LiteralPath $stateFile
    # Shared Microsoft WebView2, settings and PDFs are kept. The support copy of this script
    # is removed last (Windows allows deleting a running script).
    Remove-Item -LiteralPath (Join-Path $EkpdfRoot 'Support\reader') -Recurse -Force -ErrorAction SilentlyContinue
    Notify 'EKPDF was uninstalled. Your PDFs, settings and Microsoft WebView2 were kept.'
}

try{
    Start-Transcript -LiteralPath $log | Out-Null;$transcribing=$true
    if(-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess){throw 'Windows x64 and 64-bit PowerShell are required.'}
    if(Get-Process Offline3D.FastPDF.Dev -ErrorAction SilentlyContinue){throw 'Close EKPDF, then run setup again.'}
    if($Mode -eq 'Uninstall'){Uninstall-Reader}else{Install-Reader}
    exit 0
}catch{
    $message=Get-SetupErrorMessage $_
    Write-Host ($_ | Format-List * -Force | Out-String) -ForegroundColor Red
    Notify ('Setup could not finish.'+"`r`n`r`n"+$message+"`r`n`r`n"+'Details: '+$log) $true
    exit 1
}finally{if($transcribing){Stop-Transcript | Out-Null}}
