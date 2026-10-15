# EK Triangle SIMPLE - ordinary Windows PowerShell 5.1 orchestration.
# Uses the existing precompiled Core library. Add-Type -Path loads a DLL;
# it does not compile C# and does not start another installer process.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:PackageRoot = $PSScriptRoot
$script:SupportedYears = @(2024, 2025, 2026, 2027)

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash
}

function Read-PackageInventory {
    $inventory = @{}
    $root = [IO.Path]::GetFullPath($script:PackageRoot).TrimEnd('\') + '\'
    foreach ($line in [IO.File]::ReadAllLines((Join-Path $script:PackageRoot 'SHA256SUMS.txt'))) {
        if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid package checksum inventory.' }
        $hash = $matches[1].ToUpperInvariant()
        $relative = $matches[2].Replace('/', '\')
        $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
        if ([IO.Path]::IsPathRooted($relative) -or -not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or $inventory.ContainsKey($relative)) {
            throw "Unsafe or duplicate inventory path: $relative"
        }
        if (-not [IO.File]::Exists($path) -or (Get-Sha256 $path) -ne $hash) {
            throw "Package file missing, unreadable or changed: $relative. Stop and ask IT to review the approved ZIP."
        }
        $inventory[$relative] = $hash
    }
    $required = @('INSTALL.cmd','INSTALL.ps1','UNINSTALL.cmd','UNINSTALL.ps1','CHECK_STATUS.cmd','CHECK_STATUS.ps1','COMMON.ps1','BROWSER.ps1','engine\EKTriangle.Core.dll','payload\glyph.ini')
    foreach ($year in $script:SupportedYears) {
        $required += "payload\Revit$year\EKTriangle.dll", "payload\Revit$year\EKTriangleHook.dll"
    }
    foreach ($relative in $required) {
        if (-not $inventory.ContainsKey($relative)) { throw "Incomplete package inventory: $relative" }
    }
    foreach ($file in Get-ChildItem -LiteralPath $script:PackageRoot -Recurse -File) {
        if ($file.Extension -eq '.exe') { throw 'Unexpected EXE in the SIMPLE package. Extract the approved SIMPLE ZIP.' }
        if ($file.Extension -in '.dll', '.ps1', '.cmd') {
            $relative = $file.FullName.Substring($root.Length)
            if (-not $inventory.ContainsKey($relative)) { throw "Unlisted code file: $relative" }
        }
    }
    return $inventory
}

function Initialize-Engine {
    if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') {
        throw 'PowerShell language restrictions prevent the required .NET operations. Ask IT to approve this installer; no policy will be changed.'
    }
    $script:Inventory = Read-PackageInventory
    try { Add-Type -Path (Join-Path $script:PackageRoot 'engine\EKTriangle.Core.dll') -ErrorAction Stop }
    catch { throw "The existing Core DLL could not load. File permissions, DLL trust or application-control policy may be responsible. $($_.Exception.Message)" }
    $script:Machine = [EKTriangle.Core.Env]::Machine()
}

function Show-LaunchContext {
    Write-Host ('PowerShell: ' + $PSVersionTable.PSVersion + '; language mode: ' + $ExecutionContext.SessionState.LanguageMode)
    $policies = @(Get-ExecutionPolicy -List | ForEach-Object { $_.Scope.ToString() + '=' + $_.ExecutionPolicy })
    Write-Host ('Execution policy (read-only): ' + ($policies -join '; '))
    Write-Host 'No EK installer EXE is launched. No execution policy or security setting is changed.'
    foreach ($name in @('CHECK_STATUS.ps1','COMMON.ps1','engine\EKTriangle.Core.dll')) {
        $stream = $null
        try {
            $stream = [IO.File]::Open((Join-Path $script:PackageRoot $name), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            Write-Host "File read allowed: $name"
        } catch { Write-Host "File read denied/missing: $name - $($_.Exception.Message)" }
        finally { if ($null -ne $stream) { $stream.Dispose() } }
    }
    # Read available operational logs only. The absence of a matching event
    # does not establish that application control allowed a launch.
    foreach ($logName in @('Microsoft-Windows-CodeIntegrity/Operational','Microsoft-Windows-AppLocker/EXE and DLL','Microsoft-Windows-AppLocker/MSI and Script')) {
        try {
            $events = @(Get-WinEvent -FilterHashtable @{ LogName=$logName; StartTime=(Get-Date).AddHours(-24) } -MaxEvents 30 -ErrorAction Stop |
                Where-Object { $_.Message -match 'EKTriangle|EK_Triangle|INSTALL.ps1|CHECK_STATUS.ps1' })
            if ($events.Count -eq 0) { Write-Host "No matching recent event in $logName (not proof of permission)." }
            foreach ($event in $events) { Write-Host ("Related event: {0}; ID={1}; time={2:o}" -f $logName,$event.Id,$event.TimeCreated) }
        } catch { Write-Host "No readable matching events in $logName; a policy block still cannot be ruled out." }
    }
}

function Show-Status {
    $addins = [EKTriangle.Core.AddinManager]::new($script:Machine, $null)
    $browser = [EKTriangle.Core.BrowserManager]::new($script:Machine, $null)
    foreach ($year in $script:SupportedYears) {
        $native = $addins.Inspect($year)
        $status = $browser.Inspect($year)
        $location = if ($native.RevitInstalled) { $script:Machine.FindRevitDir($year) } else { 'not detected' }
        $nativeText = if ($native.Conflict) { $native.Conflict } elseif ($native.Installed) { 'installed' } else { 'not installed' }
        Write-Host "Revit ${year}: $location"
        Write-Host "  Dialogs: $nativeText"
        Write-Host ('  Project Browser: ' + $status.Text)
        foreach ($legacy in $native.Legacy) { Write-Host ('  Legacy EKGlyph: ' + $legacy) }
        if ($null -ne $status.Record) { Write-Host ('  Recovery: ' + $status.Record['Phase'] + '; ' + $script:Machine.BrowserStore($year)) }
    }
    $running = @([EKTriangle.Core.Env]::FindRevitProcesses())
    Write-Host ('Running Revit: ' + $(if ($running.Count) { [EKTriangle.Core.Env]::DescribeRunning($running) } else { 'none' }))
}

function Assert-RevitClosed {
    $running = @([EKTriangle.Core.Env]::FindRevitProcesses())
    if ($running.Count) { throw ('Close ALL Revit instances first: ' + [EKTriangle.Core.Env]::DescribeRunning($running)) }
}

function Enter-InstallLock {
    $script:Lock = [Threading.Mutex]::new($false, 'Local\EKTriangle.SIMPLE.Installer')
    $owned = $false
    try { $owned = $script:Lock.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $owned = $true }
    if (-not $owned) { $script:Lock.Dispose(); $script:Lock=$null; throw 'Another SIMPLE installer operation is running.' }
}

function Exit-InstallLock {
    if ($null -ne $script:Lock) { $script:Lock.ReleaseMutex(); $script:Lock.Dispose(); $script:Lock=$null }
}
$script:Lock = $null

function Get-SelectedYears([string]$Years, [switch]$Yes) {
    $detected = @($script:SupportedYears | Where-Object { $null -ne $script:Machine.FindRevitDir($_) })
    if (-not $detected.Count) { throw 'No supported Revit installation detected (2024-2027).' }
    if (-not $Years -and -not $Yes) {
        $Years = Read-Host ("Install for {0}? Enter = all, type years to keep, or Q = cancel" -f ($detected -join ','))
        if ($Years -eq 'q') { throw 'Cancelled; no installation changes made.' }
    }
    if ([string]::IsNullOrWhiteSpace($Years)) { return $detected }
    $selected = @()
    foreach ($part in ($Years -split '[,; ]+' | Where-Object { $_ })) {
        $year = 0
        if (-not [int]::TryParse($part, [ref]$year) -or $year -notin $detected) { throw "Not a detected supported Revit year: $part" }
        if ($year -notin $selected) { $selected += $year }
    }
    if (-not $selected.Count) { throw 'Select at least one detected version.' }
    return $selected
}

function Install-Payload {
    $files = @($script:Inventory.Keys | Where-Object { $_.StartsWith('payload\', [StringComparison]::OrdinalIgnoreCase) } | Sort-Object)
    $record = (($files | ForEach-Object { $script:Inventory[$_] + '  ' + $_.Substring(8) }) -join "`r`n") + "`r`n"
    $id = [EKTriangle.Core.FileUtil]::Sha256([Text.Encoding]::UTF8.GetBytes($record))
    $releases = Join-Path $script:Machine.UserStore 'SIMPLE\Payloads'
    $target = Join-Path $releases $id
    if (-not [IO.Directory]::Exists($target)) {
        $stage = $target + '.pending-' + [guid]::NewGuid().ToString('N')
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        foreach ($relative in $files) {
            $destination = Join-Path $stage $relative.Substring(8)
            New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $script:PackageRoot $relative) -Destination $destination
            if ((Get-Sha256 $destination) -ne $script:Inventory[$relative]) { throw "Copied DLL/resource did not verify: $relative" }
        }
        [IO.File]::WriteAllText((Join-Path $stage 'payload-files.txt'), $record, [Text.UTF8Encoding]::new($false))
        # Publish complete verified payload before any manifest can reference it.
        [IO.Directory]::Move($stage, $target)
    }
    foreach ($relative in $files) {
        if ((Get-Sha256 (Join-Path $target $relative.Substring(8))) -ne $script:Inventory[$relative]) { throw 'Installed payload is missing or changed. Stop for IT review.' }
    }
    return $target
}

function Test-BrowserVerified([int]$Year) {
    $status = ([EKTriangle.Core.BrowserManager]::new($script:Machine,$null)).Inspect($Year)
    if ($status.Code -ne 'Applied' -or $null -eq $status.Record) { return $false }
    $record = $status.Record
    return $record['Phase'] -eq 'Applied' -and $record['PatchedSHA256'] -eq $status.Sha256 -and
        $record['Backup'] -and [EKTriangle.Core.FileUtil]::IsUnder($record['Backup'],$script:Machine.BrowserStore($Year)) -and
        [IO.File]::Exists($record['Backup']) -and (Get-Sha256 $record['Backup']) -eq $record['OriginalSHA256']
}

function Write-OperationResults($Report, [string]$Component) {
    foreach ($year in $script:SupportedYears) {
        $outcome = $Report.Get([string]$year, $Component+'Result')
        if ($outcome) { Write-Host ("Revit {0} {1}: {2} - {3}" -f $year,$Component,$outcome,$Report.Get([string]$year,$Component+'ResultText')) }
    }
    if ($Report.Get('Summary','Message')) { Write-Host $Report.Get('Summary','Message') }
}

function Invoke-BrowserWork([int[]]$ApplyYears, [int[]]$RestoreYears) {
    if (-not $ApplyYears.Count -and -not $RestoreYears.Count) { return 0 }
    Assert-RevitClosed
    $report = [EKTriangle.Core.Report]::new()
    if ([EKTriangle.Core.WindowsRegistry]::IsAdministrator()) {
        $log = [EKTriangle.Core.Log]::new((Join-Path $script:Machine.MachineLogDir 'EKTriangle-SIMPLE.log'))
        # Null selfExe prevents copying or depending on any old restore EXE.
        $code = [EKTriangle.Core.Operations]::BrowserApply($script:Machine,$log,[int[]]$ApplyYears,$report,$null,[int[]]$RestoreYears)
    } else {
        Write-Host 'Project Browser step requires administrator permission (standard Windows UAC).'
        $output = Join-Path ([IO.Path]::GetTempPath()) ('EKTriangle-SIMPLE-' + [guid]::NewGuid().ToString('N') + '.json')
        $scriptPath = Join-Path $script:PackageRoot 'BROWSER.ps1'
        $arguments = '-NoLogo -NoProfile -File "' + $scriptPath + '" -ApplyYears "' + ($ApplyYears -join ',') + '" -RestoreYears "' + ($RestoreYears -join ',') + '" -ReportPath "' + $output + '"'
        try {
            $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -Verb RunAs -ArgumentList $arguments -Wait -PassThru -ErrorAction Stop
            if (-not [IO.File]::Exists($output)) { throw 'Elevated PowerShell returned no report. A script policy or application-control block may have prevented it from starting.' }
            $result = [IO.File]::ReadAllText($output) | ConvertFrom-Json
            if ([int]$result.ExitCode -ne $process.ExitCode) { throw 'Administrator step report/exit mismatch.' }
            foreach ($row in $result.Results) { $report.Set([string]$row.Year,'BrowserResult',[string]$row.Outcome); $report.Set([string]$row.Year,'BrowserResultText',[string]$row.Detail) }
            $code = $process.ExitCode
        } finally { if ([IO.File]::Exists($output)) { Remove-Item -LiteralPath $output } }
    }
    Write-OperationResults $report 'Browser'
    if ($code -ne 0) { return [int]$code }
    foreach ($year in @($ApplyYears + $RestoreYears | Select-Object -Unique)) {
        $outcome = $report.Get([string]$year,'BrowserResult')
        if ($outcome -notin 'Done','AlreadyDone','Restored','NotApplicable') { return 4 }
    }
    return 0
}

function Remove-OwnedPayloads {
    # Supports cleanup of verified payloads from the previous CMD installer too.
    # Unknown, changed, or incomplete files are preserved; never delete a whole
    # application tree merely because its folder name starts with EK.
    foreach ($format in @('SIMPLE','CMD')) {
        $root = Join-Path $script:Machine.UserStore ($format + '\Payloads')
        if (-not [IO.Directory]::Exists($root)) { continue }
        foreach ($folder in Get-ChildItem -LiteralPath $root -Directory) {
            $recordPath = Join-Path $folder.FullName 'payload-files.txt'
            if ($folder.Name -notmatch '^[A-F0-9]{64}$' -or -not [IO.File]::Exists($recordPath)) { continue }
            $text = [IO.File]::ReadAllText($recordPath,[Text.Encoding]::UTF8)
            if ([EKTriangle.Core.FileUtil]::Sha256([Text.Encoding]::UTF8.GetBytes($text)) -ne $folder.Name) { continue }
            $preserved = $false
            foreach ($line in ($text -split "`r`n" | Where-Object { $_ })) {
                if ($line -notmatch '^([A-F0-9]{64})  (.+)$') { $preserved=$true; continue }
                $hash = $matches[1]; $relative = $matches[2]
                $path = [IO.Path]::GetFullPath((Join-Path $folder.FullName $relative))
                if (-not [EKTriangle.Core.FileUtil]::IsUnder($path,$folder.FullName)) { $preserved=$true; continue }
                if (-not [IO.File]::Exists($path)) { continue }
                if ((Get-Sha256 $path) -eq $hash) { Remove-Item -LiteralPath $path }
                else { $preserved=$true; Write-Host "Preserved modified file for IT review: $path" }
            }
            if (-not $preserved) { Remove-Item -LiteralPath $recordPath }
            foreach ($directory in @(Get-ChildItem -LiteralPath $folder.FullName -Directory -Recurse | Sort-Object FullName -Descending)) {
                if (-not @(Get-ChildItem -LiteralPath $directory.FullName -Force).Count) { Remove-Item -LiteralPath $directory.FullName }
            }
            if (-not @(Get-ChildItem -LiteralPath $folder.FullName -Force).Count) { Remove-Item -LiteralPath $folder.FullName }
        }
    }
}

function Write-SafeError($ErrorRecord) {
    Write-Host ('STOPPED: ' + $ErrorRecord.Exception.Message) -ForegroundColor Red
    Write-Host 'No security restrictions were changed. If a script or DLL is blocked, stop and ask IT to review CODE-INVENTORY.csv and SHA256SUMS.txt.'
    Write-Host 'Keep the package and recovery data. CHECK_STATUS.cmd is read-only; retry installation or uninstall only after the cause is resolved.'
}
