Set-StrictMode -Version 2
function ConvertTo-NativeArgument([AllowEmptyString()][string]$Value) {
    # ProcessStartInfo.Arguments uses the Windows C runtime quoting rules. Always
    # quote, including doubling a trailing backslash before the closing quote.
    # This is data for the process API, never cmd.exe/Invoke-Expression input.
    $escaped=[regex]::Replace($Value,'(\\*)"','$1$1\"')
    $escaped=[regex]::Replace($escaped,'(\\+)$','$1$1')
    return '"'+$escaped+'"'
}
function Invoke-NativeTool([string]$Exe,[string[]]$Arguments,[string]$LogPath) {
    $info=New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName=$Exe
    $info.Arguments=(@($Arguments | ForEach-Object {ConvertTo-NativeArgument $_}) -join ' ')
    $info.WorkingDirectory=(Get-Location).ProviderPath
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.StandardOutputEncoding=New-Object System.Text.UTF8Encoding($false)
    $info.StandardErrorEncoding=New-Object System.Text.UTF8Encoding($false)
    $process=New-Object System.Diagnostics.Process
    $process.StartInfo=$info
    try{
        if(-not $process.Start()){throw ('Could not start '+[IO.Path]::GetFileName($Exe))}
        # Drain both streams concurrently. PowerShell 5.1 must not interpret
        # stderr as ErrorRecord objects under ErrorActionPreference=Stop.
        $stdoutTask=$process.StandardOutput.ReadToEndAsync()
        $stderrTask=$process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout=$stdoutTask.GetAwaiter().GetResult()
        $stderr=$stderrTask.GetAwaiter().GetResult()
        $result=[pscustomobject]@{ExitCode=$process.ExitCode;StdOut=$stdout;StdErr=$stderr}
        if($LogPath){
            $text='Executable: '+$Exe+"`r`n"+'Arguments: '+$info.Arguments+"`r`n"+'Exit code: '+$result.ExitCode+"`r`n`r`n"+'STANDARD OUTPUT'+"`r`n"+$stdout+"`r`n"+'STANDARD ERROR'+"`r`n"+$stderr
            [IO.File]::WriteAllText($LogPath,$text,(New-Object System.Text.UTF8Encoding($false)))
        }
        return $result
    }finally{$process.Dispose()}
}
function Run-Tool([string]$Exe,[string[]]$Arguments,[string]$LogPath) {
    Write-Host ('Running '+[IO.Path]::GetFileName($Exe)+' '+($Arguments -join ' '))
    Write-Host 'Please wait. The complete tool output will appear when this step finishes.'
    if($LogPath){Write-Host ('Tool log: '+$LogPath)}
    $result=Invoke-NativeTool $Exe $Arguments $LogPath
    if($result.StdOut){Write-Host $result.StdOut}
    if($result.StdErr){Write-Host $result.StdErr}
    if($result.ExitCode -ne 0){
        $lines=@(($result.StdOut+"`n"+$result.StdErr) -split '\r?\n' | Where-Object {-not [string]::IsNullOrWhiteSpace($_)})
        $errors=@($lines | Where-Object {$_ -match '(?i)error|failed|exception'})
        if($errors.Count){$detail=($errors | Select-Object -Last 8) -join "`r`n"}else{$detail=($lines | Select-Object -Last 12) -join "`r`n"}
        if([string]::IsNullOrWhiteSpace($detail)){$detail='The tool returned no diagnostic output.'}
        if($detail.Length -gt 1600){$detail=$detail.Substring(0,1600)+'...'}
        $message=[IO.Path]::GetFileName($Exe)+' failed with exit code '+$result.ExitCode+".`r`n`r`n"+$detail
        if($LogPath){$message+="`r`n`r`n"+'Full tool output: '+$LogPath}
        throw $message
    }
}
function Get-SetupErrorMessage($Record) {
    if($Record.ErrorDetails -and -not [string]::IsNullOrWhiteSpace($Record.ErrorDetails.Message)){return $Record.ErrorDetails.Message}
    if($Record.Exception -and -not [string]::IsNullOrWhiteSpace($Record.Exception.Message)){return $Record.Exception.Message}
    $text=[string]$Record
    if(-not [string]::IsNullOrWhiteSpace($text)){return $text}
    if($Record.Exception){return ('Setup failed: '+$Record.Exception.GetType().FullName)}
    return 'Setup failed without an error description. See the diagnostic log.'
}
function Get-RevitFramework([int]$Year,[int]$Minor,[string]$Directory) {
    if($Year -eq 2024){ return 'net48' }
    if($Year -eq 2027){ return 'net10.0-windows' }
    foreach($name in 'Revit.runtimeconfig.json','RevitWorker.runtimeconfig.json'){
        $p=Join-Path $Directory $name
        if(Test-Path -LiteralPath $p){
            $runtime=Get-Content -LiteralPath $p -Raw | ConvertFrom-Json
            $frameworks=@()
            if($runtime.runtimeOptions.PSObject.Properties['framework']){$frameworks+=@($runtime.runtimeOptions.framework)}
            if($runtime.runtimeOptions.PSObject.Properties['frameworks']){$frameworks+=@($runtime.runtimeOptions.frameworks)}
            foreach($framework in $frameworks){$major=([version]$framework.version).Major;if($major -eq 10){return 'net10.0-windows'};if($major -eq 8){return 'net8.0-windows'};throw ('Unsupported Revit host runtime: '+$framework.version)}
        }
    }
    if(($Year -eq 2025 -or $Year -eq 2026) -and $Minor -ge 5){return 'net10.0-windows'}
    return 'net8.0-windows'
}
function Get-RevitInstallations {
    $candidates=New-Object 'System.Collections.Generic.List[string]'
    foreach($base in @($env:ProgramFiles,${env:ProgramFiles(x86)})){
        if($base){foreach($year in 2024..2027){$candidates.Add((Join-Path $base "Autodesk\Revit $year"))}}
    }
    foreach($root in 'HKLM:\SOFTWARE\Autodesk\Revit','HKLM:\SOFTWARE\WOW6432Node\Autodesk\Revit'){
        if(Test-Path $root){foreach($key in @(Get-Item $root)+@(Get-ChildItem $root -Recurse -ErrorAction SilentlyContinue)){
            $properties=Get-ItemProperty $key.PSPath -ErrorAction SilentlyContinue
            foreach($name in 'InstallationLocation','InstallLocation','InstallPath','ProductLocation','Location'){
                if($properties -and $properties.PSObject.Properties[$name] -and $properties.$name){$candidates.Add([string]$properties.$name)}
            }
        }}
    }
    foreach($root in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'){
        foreach($key in @(Get-ChildItem $root -ErrorAction SilentlyContinue)){
            $p=Get-ItemProperty $key.PSPath -ErrorAction SilentlyContinue
            if($p -and $p.PSObject.Properties['DisplayName'] -and $p.DisplayName -match 'Autodesk Revit 202[4-7]' -and $p.DisplayName -notmatch 'Revit LT'){
                if($p.PSObject.Properties['InstallLocation'] -and $p.InstallLocation){$candidates.Add([string]$p.InstallLocation)}
            }
        }
    }
    $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $found=@()
    foreach($candidateValue in $candidates){
        $candidate=$candidateValue.Trim('"')
        if(-not $candidate -or $candidate -match 'Revit LT' -or -not (Test-Path -LiteralPath (Join-Path $candidate 'Revit.exe'))){continue}
        $dir=[IO.Path]::GetFullPath($candidate.Trim('"'));if(-not $seen.Add($dir)){continue}
        $api=Join-Path $dir 'RevitAPI.dll';$ui=Join-Path $dir 'RevitAPIUI.dll'
        if(-not (Test-Path -LiteralPath $api) -or -not (Test-Path -LiteralPath $ui)){continue}
        $version=[Reflection.AssemblyName]::GetAssemblyName($api).Version;$uiVersion=[Reflection.AssemblyName]::GetAssemblyName($ui).Version
        $year=2000+$version.Major;if($year -lt 2024 -or $year -gt 2027 -or $version.Major -ne $uiVersion.Major){continue}
        $fileVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($api)
        $found+=[pscustomobject]@{Year=$year;Directory=$dir;Framework=(Get-RevitFramework $year $fileVersion.FileMinorPart $dir);ApiVersion=$version.ToString();FileVersion=$fileVersion.FileVersion;Minor=$fileVersion.FileMinorPart}
    }
    return @($found | Sort-Object Year -Descending | Group-Object Year | ForEach-Object { $_.Group | Sort-Object Minor -Descending | Select-Object -First 1 })
}
function Get-WebView2Version {
    foreach($key in 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}','HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}','HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'){
        $p=Get-ItemProperty $key -ErrorAction SilentlyContinue
        if($p -and $p.PSObject.Properties['pv'] -and $p.pv -and [version]$p.pv -gt [version]'0.0.0.0'){return [version]$p.pv}
    }
    return $null
}
function Get-SafeChild([string]$Root,[string]$Relative) {
    if([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.\.([\\/]|$)' -or $Relative.Contains(':')){throw 'Unsafe package path.'}
    $full=[IO.Path]::GetFullPath((Join-Path $Root $Relative));$prefix=[IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    if(-not $full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Package path escapes its root.'}
    return $full
}
function Write-AtomicJson([string]$Path,$Value) {
    $temp=$Path+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
    $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temp -Encoding UTF8
    if(Test-Path -LiteralPath $Path){$backup=$Path+'.'+[Guid]::NewGuid().ToString('N')+'.backup';[IO.File]::Replace($temp,$Path,$backup);Remove-Item -LiteralPath $backup}else{[IO.File]::Move($temp,$Path)}
}
function Write-AddinManifest([string]$Path,[string]$Assembly) {
    $doc=New-Object xml;$root=$doc.CreateElement('RevitAddIns');$null=$doc.AppendChild($root)
    $add=$doc.CreateElement('AddIn');$add.SetAttribute('Type','Application');$null=$root.AppendChild($add)
    $values=@{Name='Offline 3D PDF';Assembly=$Assembly;AddInId='347FDBDE-BA6B-44D8-877F-9BFDA4CC2929';FullClassName='Offline3D.RevitExporter.RibbonApplication';VendorId='O3DP';VendorDescription='Offline 3D PDF prototype'}
    foreach($key in $values.Keys){$node=$doc.CreateElement($key);$node.InnerText=$values[$key];$null=$add.AppendChild($node)}
    $doc.Save($Path)
}
function Test-OwnedManifest([string]$Path) {
    if(-not (Test-Path -LiteralPath $Path)){return $false}
    $settings=New-Object System.Xml.XmlReaderSettings;$settings.DtdProcessing=[System.Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($Path,$settings)
    try{$doc=New-Object xml;$doc.XmlResolver=$null;$doc.Load($reader);return $doc.RevitAddIns.AddIn.AddInId -eq '347FDBDE-BA6B-44D8-877F-9BFDA4CC2929'}finally{$reader.Dispose()}
}
function Test-Payload([string]$Payload) {
    $manifest=Get-Content -LiteralPath (Join-Path $Payload 'package.json') -Raw | ConvertFrom-Json
    if($manifest.schema -ne 1 -or $manifest.product -ne 'Offline3DPdfPrototype' -or $manifest.releaseId -notmatch '^[a-f0-9]{32}$'){throw 'Invalid application package manifest.'}
    $expected=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach($file in $manifest.files){
        $path=Get-SafeChild $Payload $file.path
        if(-not $expected.Add([string]$file.path)){throw 'Duplicate package entry.'}
        if(-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $file.size -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256){throw ('Package verification failed: '+$file.path)}
    }
    foreach($file in Get-ChildItem -LiteralPath $Payload -Recurse -File){
        if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Package reparse points are unsupported.'}
        $rel=$file.FullName.Substring(([IO.Path]::GetFullPath($Payload).TrimEnd([IO.Path]::DirectorySeparatorChar)).Length+1).Replace('\','/')
        if($rel -ne 'package.json' -and -not $expected.Contains($rel)){throw ('Unlisted package file: '+$rel)}
    }
    foreach($required in 'Viewer/Offline3D.FastPDF.Dev.exe'){if(-not (Test-Path -LiteralPath (Get-SafeChild $Payload $required))){throw ('Missing prebuilt application: '+$required)}}
    return $manifest
}
