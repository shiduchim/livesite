[CmdletBinding()]
param([switch]$Yes)
$ErrorActionPreference = 'Stop'
Write-Host 'UNINSTALL.ps1 started - EK Triangle SIMPLE'
if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') { Write-Host 'STOPPED: company PowerShell language restrictions prevent required .NET operations. Ask IT; no policies were changed.'; exit 5 }
try {
    . (Join-Path $PSScriptRoot 'COMMON.ps1')
    Initialize-Engine
    Assert-RevitClosed
    if (-not $Yes) {
        if ((Read-Host 'Remove EK Triangle for this account and restore its Project Browser changes? [Y/N]') -ne 'y') { exit 2 }
    }
    Enter-InstallLock
    try {
        Assert-RevitClosed
        $report = [EKTriangle.Core.Report]::new()
        $log = [EKTriangle.Core.Log]::new((Join-Path $script:Machine.UserLogDir 'EKTriangle-SIMPLE.log'))
        $code = [EKTriangle.Core.Operations]::UninstallUser($script:Machine,$log,$report)
        Write-OperationResults $report 'Native'
        if ($code -ne 0) { exit $code }
        [int[]]$restore = @([EKTriangle.Core.Operations]::YearsNeedingBrowserRestore($script:Machine))
        $code = Invoke-BrowserWork -ApplyYears @() -RestoreYears $restore
        if ($code -ne 0) { Write-Host 'RESTORATION UNRESOLVED. Backups and payloads retained.'; exit $code }
        $manager = [EKTriangle.Core.AddinManager]::new($script:Machine,$null)
        foreach ($year in $script:SupportedYears) { if ($manager.Inspect($year).Installed) { throw "Add-in still active for $year." } }
        Remove-OwnedPayloads
        Write-Host 'Uninstall completed. Verified restorations are listed above. Original and legacy backups are retained.' -ForegroundColor Green
        exit 0
    } finally { Exit-InstallLock }
} catch {
    if (Get-Command Write-SafeError -ErrorAction SilentlyContinue) { Write-SafeError $_ } else { Write-Host ('STOPPED: ' + $_.Exception.Message) }
    if ($_.Exception.Message -match 'Close ALL Revit') { exit 2 }
    exit 1
}
