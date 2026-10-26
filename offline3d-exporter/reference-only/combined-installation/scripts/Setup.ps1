param([ValidateSet('BuildInstall','Install','Uninstall')][string]$Mode='Install')
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'Setup.Library.ps1')
$packageRoot=Split-Path $PSScriptRoot -Parent
$productRoot=Join-Path $env:LOCALAPPDATA 'Offline3DPdfPrototype'
$appRoot=Join-Path $productRoot 'App'
$stateFile=Join-Path $productRoot 'installed.json'
$logRoot=Join-Path $productRoot 'Logs'
$null=New-Item $logRoot -ItemType Directory -Force
$log=Join-Path $logRoot ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+$Mode+'.log')
$transcribing=$false
function Notify([string]$Text,[bool]$ErrorState=$false){
    $icon=[Windows.Forms.MessageBoxIcon]::Information;if($ErrorState){$icon=[Windows.Forms.MessageBoxIcon]::Error}
    $null=[Windows.Forms.MessageBox]::Show($Text,'Offline 3D PDF',[Windows.Forms.MessageBoxButtons]::OK,$icon)
}
function Consent([string]$Text){
    $result=[Windows.Forms.MessageBox]::Show($Text,'Offline 3D PDF — download permission',[Windows.Forms.MessageBoxButtons]::YesNo,[Windows.Forms.MessageBoxIcon]::Question)
    if($result -ne [Windows.Forms.DialogResult]::Yes){throw 'Download permission was declined. No application installation was changed.'}
}
function Download([string]$Url,[string]$Destination){
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    $ProgressPreference='SilentlyContinue'
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile ($Destination+'.download') -TimeoutSec 900
    Move-Item -LiteralPath ($Destination+'.download') -Destination $Destination -Force
}
function Get-BuildSdk {
    $candidates=@((Join-Path $productRoot 'BuildTools\dotnet\dotnet.exe'))
    $command=Get-Command dotnet.exe -ErrorAction SilentlyContinue;if($command){$candidates+=$command.Source}
    foreach($candidate in $candidates){if(Test-Path -LiteralPath $candidate){$probe=Invoke-NativeTool $candidate @('--list-sdks');if($probe.ExitCode -eq 0 -and $probe.StdOut -match '(?m)^10\.0\.'){return $candidate}}}
    $dependency=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
    $cache=Join-Path $productRoot 'BuildTools';$null=New-Item $cache -ItemType Directory -Force
    $zip=Join-Path $cache 'dotnet-sdk-win-x64.zip'
    if(-not (Test-Path -LiteralPath $zip) -or (Get-FileHash $zip -Algorithm SHA512).Hash -ne $dependency.sdk.sha512){Download $dependency.sdk.url $zip}
    if((Get-FileHash $zip -Algorithm SHA512).Hash -ne $dependency.sdk.sha512){throw 'Microsoft SDK SHA-512 verification failed.'}
    $stage=Join-Path $cache ('sdk-'+[Guid]::NewGuid().ToString('N'));Expand-Archive -LiteralPath $zip -DestinationPath $stage
    $target=Join-Path $cache 'dotnet';if(Test-Path $target){throw 'Private SDK directory exists but is unusable. See setup log; no installed application was changed.'}
    Move-Item -LiteralPath $stage -Destination $target
    return (Join-Path $target 'dotnet.exe')
}
function Ensure-WebView([string]$Payload){
    if(Test-Path -LiteralPath (Join-Path $Payload 'Viewer\WebView2Runtime\msedgewebview2.exe')){return}
    $version=Get-WebView2Version;if($version -and $version.Major -ge 130){Write-Host ('Using installed WebView2 '+$version);return}
    Consent 'The viewer needs Microsoft WebView2 Runtime. Allow its official Microsoft installer to run? If it is not bundled, setup will download it. Microsoft terms: https://developer.microsoft.com/microsoft-edge/webview2/ . This shared runtime is retained on uninstall. Viewing works offline after setup.'
    $installer=Join-Path $packageRoot 'prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
    if(-not (Test-Path -LiteralPath $installer)){
        $installer=Join-Path $productRoot 'MicrosoftEdgeWebview2Setup.exe'
        $bundled=Join-Path $packageRoot 'prerequisites\MicrosoftEdgeWebview2Setup.exe'
        if(Test-Path -LiteralPath $bundled){Copy-Item -LiteralPath $bundled -Destination $installer -Force}else{
            $dependency=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
            Download $dependency.webview2.url $installer
        }
    }
    $signature=Get-AuthenticodeSignature -LiteralPath $installer
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'WebView2 installer does not have a valid Microsoft signature.'}
    $process=Start-Process -FilePath $installer -ArgumentList '/silent','/install' -Wait -PassThru
    if($process.ExitCode -ne 0 -and $process.ExitCode -ne 3010){throw ('WebView2 setup failed: '+$process.ExitCode)}
    $version=Get-WebView2Version;if(-not $version -or $version.Major -lt 130){throw 'WebView2 installation could not be verified. Restart Windows if requested, then rerun setup.'}
}
function Build-Payload($Revits){
    Consent 'This is the TEMPORARY BUILD-AND-INSTALL package. Allow setup to obtain Microsoft .NET SDK 10.0.100 if a compatible SDK is missing, and required NuGet build packages, then compile locally? Internet and several GB of free disk space may be needed. The private SDK does not change PATH or require Visual Studio. .NET license: https://github.com/dotnet/sdk/blob/main/LICENSE.TXT . Revit references come from your installed Revit. No commands or paths need to be entered.'
    $sdk=Get-BuildSdk
    $env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$env:MSBUILDDISABLENODEREUSE='1';$env:DOTNET_CLI_USE_MSBUILD_SERVER='0';$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1';$env:DOTNET_CLI_HOME=Join-Path $productRoot 'BuildTools\CliHome';$env:NUGET_PACKAGES=Join-Path $productRoot 'BuildTools\NuGet'
    $buildRoot=Join-Path $productRoot ('Builds\'+[Guid]::NewGuid().ToString('N'));$null=New-Item $buildRoot -ItemType Directory -Force
    foreach($name in 'src','fastpdf','assets','licenses','samples','Directory.Build.props','global.json'){
        $item=Join-Path $packageRoot $name;if(-not (Test-Path $item)){throw ('Build source missing: '+$name)}
        Copy-Item -LiteralPath $item -Destination $buildRoot -Recurse
    }
    # Clean generated outputs from a previously used source checkout without touching other apps.
    Get-ChildItem $buildRoot -Directory -Recurse | Where-Object {$_.Name -in @('bin','obj','node_modules','.git')} | Sort-Object {$_.FullName.Length} -Descending | ForEach-Object {Remove-Item -LiteralPath $_.FullName -Recurse -Force}
    $payload=Join-Path $buildRoot 'payload';$null=New-Item $payload -ItemType Directory
    Push-Location $buildRoot
    try{
        Run-Tool $sdk @('publish','fastpdf/src/EKFastPDF/EKFastPDF.csproj','-c','Release','-r','win-x64','--self-contained','true','-p:RestoreLockedMode=false','-p:UseSharedCompilation=false','-m:1','-nr:false','-o',(Join-Path $payload 'Viewer')) ($log+'.viewer-build.txt')
        Run-Tool (Join-Path $payload 'Viewer\Offline3D.FastPDF.Dev.exe') @('--verify-package',(Join-Path $buildRoot 'samples\SYNTHETIC-Offline3D.pdf')) ($log+'.viewer-check.txt')
        $exporters=@()
        foreach($revit in $Revits){
            $dir=Join-Path $payload ('Exporters\'+$revit.Year)
            Run-Tool $sdk @('build','src/RevitExporter/RevitExporter.csproj','-c','Release',('-p:RevitYear='+$revit.Year),('-p:RevitHostFramework='+$revit.Framework),('-p:RevitApiDir='+$revit.Directory),'-p:RestoreLockedMode=false','-p:UseSharedCompilation=false','-m:1','-nr:false','-o',$dir) ($log+'.revit-'+$revit.Year+'-build.txt')
            foreach($name in 'RevitAPI.dll','RevitAPIUI.dll'){if(Test-Path (Join-Path $dir $name)){throw 'Autodesk build references must not be redistributed.'}}
            $exporters+=[pscustomobject]@{year=$revit.Year;framework=$revit.Framework;path=('Exporters/'+$revit.Year+'/Offline3DPrototype.Revit'+$revit.Year+'.dll');apiVersion=$revit.ApiVersion;apiFileVersion=$revit.FileVersion}
        }
        Copy-Item (Join-Path $packageRoot 'licenses') (Join-Path $payload 'licenses') -Recurse
        Copy-Item (Join-Path $packageRoot 'samples\SYNTHETIC-Offline3D.pdf') (Join-Path $payload 'SYNTHETIC-Offline3D.pdf')
        $files=@(Get-ChildItem $payload -Recurse -File | ForEach-Object {[pscustomobject]@{path=$_.FullName.Substring($payload.Length+1).Replace('\','/');size=$_.Length;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
        Write-AtomicJson (Join-Path $payload 'package.json') ([ordered]@{schema=1;product='Offline3DPdfPrototype';version='0.5.2-experimental';releaseId=[Guid]::NewGuid().ToString('N');exporters=$exporters;files=$files})
    }finally{Pop-Location}
    return $payload
}
function Install-Payload([string]$Payload,$Revits){
    $package=Test-Payload $Payload
    foreach($revit in $Revits){if(-not @($package.exporters | Where-Object {$_.year -eq $revit.Year -and $_.framework -eq $revit.Framework})){throw ('No matching prebuilt exporter for Revit '+$revit.Year+' '+$revit.Framework+'. Use BUILD_AND_INSTALL.cmd for this installed update.')}}
    Ensure-WebView $Payload
    $release=Join-Path $appRoot ('releases\'+$package.releaseId)
    if(Test-Path $release){throw 'This exact release is already staged. Uninstall before reinstalling it.'}
    $null=New-Item $release -ItemType Directory -Force
    Copy-Item (Join-Path $Payload '*') $release -Recurse
    $null=Test-Payload $release
    Get-ChildItem -LiteralPath $release -File -Recurse | Unblock-File
    $snapshots=@();$shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'Offline 3D PDF Viewer.lnk'
    $shortcutBefore=$null;if(Test-Path -LiteralPath $shortcut){$shortcutBefore=[IO.File]::ReadAllBytes($shortcut)}
    try{
        foreach($revit in $Revits){
            $dir=Join-Path $env:APPDATA ('Autodesk\Revit\Addins\'+$revit.Year);$null=New-Item $dir -ItemType Directory -Force
            foreach($name in 'Offline3DPdfPrototype.addin','Offline3DPrototype.DevOnly.addin'){
                $path=Join-Path $dir $name
                $bytes=$null;if(Test-Path $path){if(-not (Test-OwnedManifest $path)){throw ('Existing manifest is not owned by this app: '+$path)};$bytes=[IO.File]::ReadAllBytes($path)}
                $snapshots+=[pscustomobject]@{path=$path;bytes=$bytes}
                if($name -eq 'Offline3DPrototype.DevOnly.addin'){if($bytes){Remove-Item -LiteralPath $path};continue}
                $entry=@($package.exporters | Where-Object {$_.year -eq $revit.Year -and $_.framework -eq $revit.Framework})[0]
                Write-AddinManifest $path (Get-SafeChild $release $entry.path)
            }
        }
        $shell=New-Object -ComObject WScript.Shell;$link=$shell.CreateShortcut($shortcut);$link.TargetPath=Join-Path $release 'Viewer\Offline3D.FastPDF.Dev.exe';$link.WorkingDirectory=Join-Path $release 'Viewer';$link.Save()
        $support=Join-Path $appRoot 'Support';$null=New-Item $support -ItemType Directory -Force
        $supportScripts=Join-Path $support 'scripts';if(Test-Path $supportScripts){Remove-Item -LiteralPath $supportScripts -Recurse -Force}
        Copy-Item $PSScriptRoot $supportScripts -Recurse -Force
        Copy-Item (Join-Path $packageRoot 'UNINSTALL.cmd') $support -Force
        Write-AtomicJson $stateFile ([ordered]@{schema=1;product='Offline3DPdfPrototype';release=$release;manifests=@($Revits | ForEach-Object {Join-Path $env:APPDATA ('Autodesk\Revit\Addins\'+$_.Year+'\Offline3DPdfPrototype.addin')});shortcut=$shortcut;versions=@($Revits)})
    }catch{
        foreach($snapshot in $snapshots){if($snapshot.bytes){[IO.File]::WriteAllBytes($snapshot.path,$snapshot.bytes)}elseif(Test-Path $snapshot.path){Remove-Item -LiteralPath $snapshot.path}}
        if($shortcutBefore){[IO.File]::WriteAllBytes($shortcut,$shortcutBefore)}elseif(Test-Path -LiteralPath $shortcut){Remove-Item -LiteralPath $shortcut}
        Remove-Item -LiteralPath $release -Recurse -Force
        throw
    }
    Notify ('Offline 3D PDF v0.5.2 installation complete.'+"`r`n`r`n"+'Reopen Revit. Under Add-Ins, look for Offline 3D PDF > Export 3D PDF.'+"`r`n"+'Viewer: Windows Start > Offline 3D PDF Viewer.'+"`r`n`r`n"+'Installed Revit versions: '+(($Revits | ForEach-Object {''+$_.Year+' ('+$_.Framework+')'}) -join ', '))
}
function Uninstall-App {
    if(-not (Test-Path $stateFile)){Notify 'No installation of Offline 3D PDF was found.';return}
    $state=Get-Content $stateFile -Raw | ConvertFrom-Json
    if($state.product -ne 'Offline3DPdfPrototype'){throw 'Installation ownership could not be verified.'}
    foreach($path in $state.manifests){
        $parent=[IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk\Revit\Addins')).TrimEnd('\')+'\'
        if(-not [IO.Path]::GetFullPath($path).StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($path) -ne 'Offline3DPdfPrototype.addin'){throw 'Unsafe uninstall manifest path.'}
        if(Test-Path $path){if(-not (Test-OwnedManifest $path)){throw ('Manifest ownership mismatch: '+$path)};Remove-Item -LiteralPath $path}
    }
    $shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'Offline 3D PDF Viewer.lnk';if(Test-Path $shortcut){Remove-Item -LiteralPath $shortcut}
    # Shared Microsoft WebView2 is retained, as other applications may depend on it.
    foreach($name in 'App\releases','Builds','BuildTools'){ $path=Join-Path $productRoot $name;if(Test-Path $path){Remove-Item -LiteralPath $path -Recurse -Force} }
    Remove-Item -LiteralPath $stateFile
    Notify 'Offline 3D PDF was uninstalled. ClickTo3D, released FastPDF, exported PDFs and shared Microsoft WebView2 were preserved. Setup logs are retained.'
}
try{
    Start-Transcript -LiteralPath $log | Out-Null;$transcribing=$true
    if(-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess){throw 'Windows x64 and 64-bit PowerShell are required.'}
    if(Get-Process Revit,RevitWorker,Offline3D.FastPDF.Dev -ErrorAction SilentlyContinue){throw 'Close Revit and Offline 3D PDF Viewer, then double-click the command again.'}
    if($Mode -eq 'Uninstall'){Uninstall-App}else{
        $revits=@(Get-RevitInstallations)
        if($revits.Count -eq 0){throw 'No supported full Revit 2024–2027 installation was detected. Revit LT is not supported.'}
        $revits | Format-Table Year,Framework,FileVersion,Directory | Out-Host
        $payload=Join-Path $packageRoot 'payload'
        if($Mode -eq 'BuildInstall'){$payload=Build-Payload $revits}
        elseif(-not (Test-Path (Join-Path $payload 'package.json'))){throw 'This is a source/build package, not the final prebuilt package. Double-click BUILD_AND_INSTALL.cmd. INSTALL.cmd never compiles or downloads an SDK.'}
        Install-Payload $payload $revits
    }
    exit 0
}catch{
    $failure=$_;$message=Get-SetupErrorMessage $failure
    Write-Host ($failure | Format-List * -Force | Out-String) -ForegroundColor Red
    Write-Host $failure.ScriptStackTrace -ForegroundColor Red
    Notify ('Setup could not finish.'+"`r`n`r`n"+$message+"`r`n`r`n"+'Details: '+$log) $true
    exit 1
}
finally{if($transcribing){Stop-Transcript | Out-Null}}
