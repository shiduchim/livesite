[CmdletBinding()]
param([switch]$Yes, [string]$Years = '')
$ErrorActionPreference = 'Stop'
Write-Host 'INSTALL.ps1 started - EK Triangle SIMPLE'
if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') { Write-Host 'STOPPED: company PowerShell language restrictions prevent required .NET operations. Ask IT; no policies were changed.'; exit 5 }
try {
    . (Join-Path $PSScriptRoot 'COMMON.ps1')
    Initialize-Engine
    Assert-RevitClosed
    Show-Status
    [int[]]$selected = @(Get-SelectedYears -Years $Years -Yes:$Yes)
    [int[]]$removed = @($script:SupportedYears | Where-Object { $_ -notin $selected })
    Enter-InstallLock
    try {
        Assert-RevitClosed
        $payload = Install-Payload
        $report = [EKTriangle.Core.Report]::new()
        $log = [EKTriangle.Core.Log]::new((Join-Path $script:Machine.UserLogDir 'EKTriangle-SIMPLE.log'))
        $code = [EKTriangle.Core.Operations]::InstallUser($script:Machine,$log,$selected,$payload,$report,$removed)
        Write-OperationResults $report 'Native'
        if ($code -ne 0) { exit $code }
        $partial = $false
        foreach ($year in $selected) { if ($report.Get([string]$year,'NativeResult') -eq 'Skipped') { $partial=$true } }
        $manager = [EKTriangle.Core.BrowserManager]::new($script:Machine,$null)
        [int[]]$work = @()
        foreach ($year in $selected) {
            $status = $manager.Inspect($year)
            if ($status.Code -notin 'Ready','Applied') { Write-Host "Revit $year Project Browser: SKIPPED - $($status.Text)"; $partial=$true }
            elseif (Test-BrowserVerified $year) { Write-Host "Revit $year Project Browser: AlreadyDone - patch and original backup verified; no administrator step required." }
            else { $work += $year }
        }
        [int[]]$restore = @([EKTriangle.Core.Operations]::YearsNeedingBrowserRestore($script:Machine) | Where-Object { $_ -in $removed })
        $code = Invoke-BrowserWork -ApplyYears $work -RestoreYears $restore
        if ($code -ne 0) { exit $code }
        $native = [EKTriangle.Core.AddinManager]::new($script:Machine,$null)
        foreach ($year in $selected) {
            if ($report.Get([string]$year,'NativeResult') -eq 'Skipped') { continue }
            if (-not $native.Inspect($year).Installed) { throw "Installed manifest/DLL verification failed for $year." }
            foreach ($name in @('EKTriangle.dll','EKTriangleHook.dll')) {
                if ((Get-Sha256 (Join-Path $payload "Revit$year\$name")) -ne $script:Inventory["payload\Revit$year\$name"]) { throw "Installed DLL changed or became unreadable for $year." }
            }
        }
        Show-Status
        Write-Host ('Logs: ' + $script:Machine.UserLogDir + '; original backups: ' + $script:Machine.MachineStore)
        if ($partial) { Write-Host 'Completed with skipped components; see each version above.'; exit 4 }
        Write-Host 'Installation verified. Start Revit to check the triangle appearance and expand/collapse behavior.' -ForegroundColor Green
        exit 0
    } finally { Exit-InstallLock }
} catch {
    if (Get-Command Write-SafeError -ErrorAction SilentlyContinue) { Write-SafeError $_ } else { Write-Host ('STOPPED: ' + $_.Exception.Message) }
    if ($_.Exception.Message -match 'Close ALL Revit|Cancelled;') { exit 2 }
    exit 1
}
