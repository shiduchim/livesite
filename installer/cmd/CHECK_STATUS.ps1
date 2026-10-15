[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Write-Host 'CHECK_STATUS.ps1 started - EK Triangle SIMPLE (read-only)'
if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') { Write-Host 'STOPPED: company PowerShell language restrictions prevent required .NET operations. Ask IT; no policies were changed.'; exit 5 }
try {
    . (Join-Path $PSScriptRoot 'COMMON.ps1')
    Show-LaunchContext
    Initialize-Engine
    Show-Status
    Write-Host 'Read-only status completed. No add-in, Autodesk file, registry setting, log or backup was written.'
    exit 0
} catch {
    if (Get-Command Write-SafeError -ErrorAction SilentlyContinue) { Write-SafeError $_ } else { Write-Host ('STOPPED: ' + $_.Exception.Message) }
    exit 1
}
